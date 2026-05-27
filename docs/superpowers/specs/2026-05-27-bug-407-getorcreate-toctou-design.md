# #407 — GetOrCreate / TagDefinitions TOCTOU races

Closes #407

## Problem

Three repository methods still use read-then-write under `IDbContextFactory`, which removes the implicit serialization the old shared `AppDbContext` provided (PR #326). Two concurrent callers can both pass the "doesn't exist" check and race to insert, producing a `DbUpdateException` on the loser. `IncrementUsageAsync` additionally has a lost-update race where two concurrent reads of `usage_count = 5` both write `6` instead of `7`.

Full diagnosis: [issue #407](https://github.com/musicislife08/issues/407).

## Approach: `ON CONFLICT` across all three

Use PostgreSQL `ON CONFLICT` clauses via `ExecuteSqlAsync(FormattableString)`. Matches the established precedent in `TelegramUserRepository.UpsertAsync` at line 131. Rejected a `try/catch DbUpdateException` alternative after second-opinion review (concurrency specialist) — it has worse correctness traps:
- Leaves the failed insert in `Added` state in the change tracker (refactor footgun).
- Catching `DbUpdateException` and matching `SqlState = "23505"` is not constraint-specific; silently swallows unrelated unique violations if a future migration adds a second unique index.
- Same round-trip count as `INSERT ON CONFLICT DO NOTHING` + `SELECT`.

## Implementation

### 1. `TelegramUserRepository.GetOrCreateAsync` (line 43)

Replace the read → check → conditional insert with:

```csharp
var now = DateTimeOffset.UtcNow;
var isTrusted = TelegramConstants.IsSystemUser(user.Id);

await context.Database.ExecuteSqlAsync($"""
    INSERT INTO telegram_users (
        telegram_user_id, username, first_name, last_name,
        is_bot, is_trusted, is_banned, bot_dm_enabled,
        first_seen_at, last_seen_at, created_at, updated_at, is_active
    ) VALUES (
        {user.Id}, {user.Username}, {user.FirstName}, {user.LastName},
        {isBot}, {isTrusted}, {false}, {false},
        {now}, {now}, {now}, {now}, {false}
    )
    ON CONFLICT (telegram_user_id) DO NOTHING
    """, cancellationToken);

var entity = await context.TelegramUsers
    .AsNoTracking()
    .FirstAsync(u => u.TelegramUserId == user.Id, cancellationToken);

return entity.ToModel();
```

- Two round-trips always (insert + select). Acceptable: call site is `WelcomeService.cs:142`, fires once per new chat member, not on every message.
- Preserves existing semantic: on conflict, no field mutations (`last_seen_at`, `username`, etc. stay as they were). `UpsertAsync` (which DOES bump those fields) remains the high-frequency path.
- The trust-account log line (`"Created Telegram system account ... with automatic trust"`) is dropped — same reason as #3 below: we no longer have a clean "did we insert?" signal without `RETURNING (xmax = 0)`. The trust state is already discoverable from `IsTrusted` on the returned model.

### 2. `TagDefinitionsRepository.CreateAsync` (line 43)

```csharp
var normalizedTag = tagName.Trim().ToLowerInvariant();
var now = DateTimeOffset.UtcNow;
var dataColor = (Data.Models.TagColor)color;

await context.Database.ExecuteSqlAsync($"""
    INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
    VALUES ({normalizedTag}, {(int)dataColor}, {0}, {now})
    ON CONFLICT (tag_name) DO NOTHING
    """, cancellationToken);

var definition = await context.TagDefinitions
    .AsNoTracking()
    .FirstAsync(td => td.TagName == normalizedTag, cancellationToken);

return definition.ToModel();
```

- The existing "Tag definition already exists" warning log is dropped (we no longer distinguish insert vs hit). The returned model is identical either way.

### 3. `TagDefinitionsRepository.IncrementUsageAsync` (line 125)

```csharp
var normalizedTag = tagName.ToLowerInvariant();
var now = DateTimeOffset.UtcNow;
var primaryColor = (int)Data.Models.TagColor.Primary;

await context.Database.ExecuteSqlAsync($"""
    INSERT INTO tag_definitions (tag_name, color, usage_count, created_at)
    VALUES ({normalizedTag}, {primaryColor}, {1}, {now})
    ON CONFLICT (tag_name) DO UPDATE SET usage_count = tag_definitions.usage_count + 1
    """, cancellationToken);
```

- Single statement, atomic. Eliminates both the duplicate-key race and the lost-increment race.
- Drop the `"Auto-created tag definition"` info log. Detecting insert-vs-update would require either `RETURNING (xmax = 0)` materialized via `Database.SqlQuery<>` or a follow-up SELECT — and the auto-create event isn't load-bearing for any consumer.
- `DecrementUsageAsync` (line 157) is NOT changed. Read-modify-save is safe there because the method returns early when the row doesn't exist; there's no auto-create branch. A lost-decrement race is theoretically possible (two admins removing same tag from same user simultaneously) but the consequence is bounded (count drifts by ±1) and the operation is admin-driven and rare. Out of scope.

## Files

- `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs:43-86`
- `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:43-73`
- `TelegramGroupsAdmin.Telegram/Repositories/TagDefinitionsRepository.cs:125-155`

## Tests

Integration tests under `TelegramGroupsAdmin.IntegrationTests/Repositories/` (real PostgreSQL via existing test infrastructure):

1. **`GetOrCreateAsync` race**: two `Task.Run` calls with the same brand-new `telegramUserId` racing through `IDbContextFactory` contexts. Both must succeed; both must return models with the same `TelegramUserId`; only one row exists in the table.

2. **`Tags.CreateAsync` race**: two concurrent calls with the same `tagName`. Both succeed; only one row.

3. **`IncrementUsageAsync` race — lost-increment**: pre-create the tag with `usage_count = 0`. Fire N concurrent `IncrementUsageAsync(tagName)` calls. Final count must equal N. (Validates the atomic increment.)

4. **`IncrementUsageAsync` race — concurrent first-application**: two concurrent `IncrementUsageAsync(brand-new-tag)` calls. Both succeed. Tag exists with `usage_count = 2` (or `1` if implemented as "DO NOTHING then SELECT — bump after" — the spec above uses `DO UPDATE SET +1` so final is 2).

Mock count: zero. These tests run against the real DB to exercise PostgreSQL `ON CONFLICT` semantics.

## Acceptance Criteria

- [ ] All three methods use `ExecuteSqlAsync(FormattableString)` with `ON CONFLICT` clauses; no `try/catch DbUpdateException`.
- [ ] No raw string interpolation in SQL — all values pass through `FormattableString` parameter binding.
- [ ] Integration tests above pass against PostgreSQL under concurrent load.
- [ ] Existing unit tests for these repositories continue to pass (with mock-call-count assertions updated if needed).
- [ ] No DTOs ever enter the EF Core change tracker for these three methods.

## Out of Scope

- `DecrementUsageAsync` — see rationale in §3 above.
- Any other repository methods with read-then-write shapes — fix as found in their own issues.
- Changing the call sites (`WelcomeService`, `UserTagsRepository.AddTagAsync`) — they're correct callers; only the repository internals change.
