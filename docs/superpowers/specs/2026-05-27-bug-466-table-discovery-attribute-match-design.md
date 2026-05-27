# #466 — TableDiscoveryService silent skip of irregular DTO names

Closes #466

## Problem

`TableDiscoveryService.FindDtoForTable` maps Postgres tables to DTO types using four name-based candidates (`{Pascal}Dto`, `{Pascal}RecordDto`, `{Singular}Dto`, `{Singular}RecordDto`). `UsernameBlacklistEntryDto` doesn't match any candidate for `username_blacklist` (the `Entry` infix slips through), so discovery silently logs `No DTO found for table 'username_blacklist', skipping`.

This drops the table from both flows that share the discovery service:

1. **Restore-wipe** (`BackupService.cs:470` → `WipeAllTablesAsync` at `:488`): the wipe-list excludes `username_blacklist`, so when `users` is wiped, the FK `ON DELETE SET NULL` fires, all three actor columns end up NULL, the `CK_username_blacklist_exclusive_actor` (`= 1`) constraint trips, and the whole transaction rolls back. This is the visible symptom that broke a prod-to-dev restore.
2. **Backup-export** (`BackupService.cs:122`): every backup created before this fix is missing `username_blacklist` rows. Silent data loss — restoring any pre-fix backup yields zero blacklist entries.

`UsernameBlacklistEntryDto` is the only currently-silently-skipped DTO; the issue's audit confirmed every other table-backed DTO resolves via one of the four candidates. The fix-order discipline matters: **#466 lands before #467** so the restore unblocks first.

Full diagnosis: [issue #466](https://github.com/musicislife08/issues/466).

## Approach: pure `[Table]` attribute match

EF Core requires `[Table("...")]` on every snake_case-mapped DTO in this project (per `TelegramGroupsAdmin.Data/CLAUDE.md`). Use that as the authoritative lookup and rip the name-convention fallback entirely.

Rejected alternatives:
- Keep the convention fallback as defensive code — violates project rule against dead code and backward-compat shims. The fallback would only help a future DTO that violated the `[Table]` convention, which is itself a bug.
- Rename `UsernameBlacklistEntryDto` to `UsernameBlacklistDto` — fixes one case, leaves the silent-skip class of bug intact. PR #119 used this approach for `PendingNotificationRecord`; that fix has now happened once and the issue says next time we should fix the discovery, not the name.

## Implementation

### 1. Rewrite `FindDtoForTable`

`TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableDiscoveryService.cs:80-96`:

```csharp
internal static Type? FindDtoForTable(string tableName, IReadOnlyList<Type> dtoTypes)
{
    return dtoTypes.FirstOrDefault(dto =>
        dto.GetCustomAttribute<TableAttribute>()?.Name?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
}
```

- Marked `static` — no instance state needed.
- Returns the first match, which is fine because `[Table]` names are unique by DB constraint (no two DTOs can claim the same table).

### 2. Delete dead helpers

Remove `ToPascalCase` (lines 98-106) and `Singularize` (lines 108-116). Both are unreachable after the rewrite.

### 3. Add required usings

`using System.ComponentModel.DataAnnotations.Schema;` and `using System.Reflection;` at the top of the file.

### 4. Keep the Debug-level skip log

Line 69 (`No DTO found for table '{TableName}', skipping`) stays as-is. Tables like `qrtz_*`, `__EFMigrationsHistory`, and Data Protection key tables legitimately have no DTO. Logging at Debug is correct.

### 5. Regression test — DTO-must-be-mappable invariant

New test in `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs` (or extend `BackupServiceTests`):

```csharp
[Test]
public void EveryTableBackedDtoIsDiscoverable()
{
    var expectedNonTableBacked = new HashSet<string>
    {
        nameof(InviteWithCreatorDto),         // join projection
        nameof(RawAlgorithmPerformanceStatsDto), // keyless, configured for SqlQuery
    };

    var dtoTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
        .Where(t => t.Name.EndsWith("Dto") && t.IsClass)
        .ToList();

    var missingTableAttr = dtoTypes
        .Where(t => !expectedNonTableBacked.Contains(t.Name))
        .Where(t => t.GetCustomAttribute<TableAttribute>() is null)
        .Select(t => t.Name)
        .ToList();

    Assert.That(missingTableAttr, Is.Empty,
        $"These DTOs need [Table(\"...\")] (or add to expectedNonTableBacked if intentional): {string.Join(", ", missingTableAttr)}");
}
```

This catches future drift in two directions:
- New DTO added without `[Table]` → test fails, surfaces the gap before backup data loss.
- New entry added to `expectedNonTableBacked` → the explicit list of "intentional exceptions" forces a deliberate decision rather than silent inclusion.

## Files

- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableDiscoveryService.cs` — rewrite `FindDtoForTable`, delete helpers, add usings.
- `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs` (new file) or extension to `BackupServiceTests.cs` — invariant test.

## Tests

In addition to the invariant test above:

- **Discovery resolves `username_blacklist` → `UsernameBlacklistEntryDto`** — direct positive test of the regression case.
- **Discovery silently skips `qrtz_locks`** — assert the result `Dictionary` has no entry for Quartz tables (or whatever system tables are present in the test DB).
- **Existing backup integration tests** — `BackupServiceTests` should pass without change. If any test was implicitly depending on the silent-skip behavior for `username_blacklist`, it'll surface here as a regression.

## Acceptance Criteria

- [ ] `FindDtoForTable` is a one-liner over `[Table]` attribute matching; `ToPascalCase` and `Singularize` removed.
- [ ] Invariant test asserts every `Data.Models.*Dto` has `[Table]` or is in the explicit exception list.
- [ ] `username_blacklist` resolves to `UsernameBlacklistEntryDto` in the mapping.
- [ ] No `"No DTO found for table 'username_blacklist'"` log line on a fresh discovery run.
- [ ] All existing backup/restore tests pass.
- [ ] Restore against a dev DB that has `username_blacklist` rows succeeds — verifying the surface symptom is cleared. (This still requires #467 to land for the full restore correctness, but #466 unblocks the immediate wipe-list inclusion.)

## Operational Note (for the PR description)

Backups taken **before** this fix lands are missing `username_blacklist` data. Restoring such a backup yields zero blacklist entries — accurate to the historical capture, but historically incomplete. If any prod-to-dev sync needs blacklist rows, manually export and import that one table until a fresh backup is taken post-fix. Call this out in the release notes.

## Out of Scope

- #344 (source generator for backup/restore DTO metadata) — longer-term replacement for reflection-based discovery; orthogonal.
- Renaming `UsernameBlacklistEntryDto` to remove the `Entry` infix — not necessary once attribute-match lands, and the name has informational value (it's an "entry" in a blacklist).
- Any DTO migration or schema change — this is a discovery-layer fix only.

## Related

- **#467** lands second on this branch; it fixes the latent FK-vs-CHECK conflict that this discovery bug merely *masked* during restore.
- **#119** (merged) — the prior rename-as-bugfix for `PendingNotificationRecord`. This spec prevents future renames-as-bugfixes by switching to attribute-based discovery.
