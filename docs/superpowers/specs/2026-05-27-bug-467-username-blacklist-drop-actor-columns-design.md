# #467 — username_blacklist: drop dead actor columns + lift audit to service layer

Closes #467

## Problem

`username_blacklist` carries three "actor" columns (`web_user_id`, `telegram_user_id`, `system_identifier`) plus a CHECK constraint requiring exactly one to be non-null, plus two FKs with `ON DELETE SET NULL`. The CHECK and SET NULL are mutually exclusive design choices: SET NULL assumes the row can survive without an actor; the CHECK asserts every row always has one. Hard-deleting a parent user trips this conflict, aborting the delete with a confusing CHECK violation.

But the real finding from auditing the codebase: **the actor columns are entirely dead weight**. They are persisted, loaded, mapped through `Actor` — and then never consumed:

- `UsernameBlacklistService.CheckDisplayNameAsync` (the matching logic) reads `Pattern`, `MatchType`, `Enabled`. Never touches `CreatedBy`.
- `UsernameBlacklistSettings.razor` displays Pattern / Match Type / Status / Created date / Notes / Actions. **No "Created By" column.**
- `UsernameBlacklistRepository.AddEntryAsync` writes the actor columns but no consumer reads them for any purpose.

The audit-log coverage that the actor columns *appear* to duplicate is **already wired** — but at the wrong layer. The Razor page (`UsernameBlacklistSettings.razor`) calls `IAuditService.LogEventAsync` directly, separate from the repository call. This violates the codebase's broader "domain semantics live in the service" convention and has three concrete problems:
1. Any future caller of `IUsernameBlacklistRepository` that isn't this Razor page silently skips audit.
2. Data write and audit write are non-transactional; one can succeed and the other fail.
3. Every new UI surface that mutates blacklist has to remember to also call `AuditService`.

Full diagnosis: [issue #467](https://github.com/musicislife08/issues/467).

## Approach: drop the dead columns + lift audit to service layer

Two coupled changes:

1. **Drop the dead actor columns** (and the CHECK + two FKs that depend on them). The bug ceases to exist because the conflicting structure is gone. Three discarded alternatives (FK RESTRICT, orphan trigger, CHECK relaxation) all preserved a column that has no consumer.

2. **Lift audit responsibility to `IUsernameBlacklistService`.** Move the `IAuditService.LogEventAsync` calls out of `UsernameBlacklistSettings.razor` and into new service methods that wrap the repository writes. This fixes a pre-existing architectural gap that the dead-column refactor surfaces.

Both changes are done together because they share the same call-site refactor — the UI page is being touched in either case, and threading audit through the service is the right time to do it.

## Implementation

### 1. Schema migration (drop columns, CHECK, FKs)

Update `AppDbContext.OnModelCreating` — `ConfigureRelationships` block at line 534-557:

```csharp
modelBuilder.Entity<UsernameBlacklistEntryDto>(entity =>
{
    entity.Property(e => e.MatchType).HasDefaultValue(0);

    // Prevent duplicate patterns (case-insensitive, only enabled entries)
    entity.HasIndex(e => e.Pattern)
        .IsUnique()
        .HasFilter("enabled = true")
        .HasDatabaseName("IX_username_blacklist_unique_enabled_pattern");
});
```

Removed: both `HasOne<>().WithMany().HasForeignKey().OnDelete(DeleteBehavior.SetNull)` calls and `entity.ToTable(t => t.HasCheckConstraint(...))`.

Then `dotnet ef migrations add DropUsernameBlacklistActorColumns -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`. Review the generated migration — it should:
- `DROP CONSTRAINT FK_username_blacklist_telegram_users_telegram_user_id`
- `DROP CONSTRAINT FK_username_blacklist_users_web_user_id`
- `DROP CONSTRAINT CK_username_blacklist_exclusive_actor`
- `DROP COLUMN web_user_id`
- `DROP COLUMN telegram_user_id`
- `DROP COLUMN system_identifier`

Order matters: drop FKs and CHECK before dropping the columns those reference. EF Core should generate this correctly; verify before commit.

### 2. DTO change

`TelegramGroupsAdmin.Data/Models/UsernameBlacklistEntryDto.cs`:

```csharp
[Table("username_blacklist")]
public class UsernameBlacklistEntryDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("pattern")]
    [Required]
    [MaxLength(200)]
    public string Pattern { get; set; } = string.Empty;

    [Column("match_type")]
    public int MatchType { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("notes")]
    [MaxLength(500)]
    public string? Notes { get; set; }
}
```

Removed: `WebUserId`, `TelegramUserId`, `SystemIdentifier` properties and the explanatory comment block.

### 3. Domain model change

`TelegramGroupsAdmin.Telegram/Models/UsernameBlacklistEntry.cs`:

```csharp
public sealed record UsernameBlacklistEntry(
    long Id,
    string Pattern,
    BlacklistMatchType MatchType,
    bool Enabled,
    DateTimeOffset CreatedAt,
    string? Notes);
```

Removed: `CreatedBy: Actor` field.

### 4. Mapping change

`TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameBlacklistMappings.cs` becomes a one-shape mapping with no `ActorMappings` calls:

```csharp
internal static class UsernameBlacklistMappings
{
    extension(DataModels.UsernameBlacklistEntryDto data)
    {
        public UiModels.UsernameBlacklistEntry ToModel() => new(
            Id: data.Id,
            Pattern: data.Pattern,
            MatchType: (UiModels.BlacklistMatchType)data.MatchType,
            Enabled: data.Enabled,
            CreatedAt: data.CreatedAt,
            Notes: data.Notes);
    }

    extension(UiModels.UsernameBlacklistEntry ui)
    {
        public DataModels.UsernameBlacklistEntryDto ToDto() => new()
        {
            Id = ui.Id,
            Pattern = ui.Pattern,
            MatchType = (int)ui.MatchType,
            Enabled = ui.Enabled,
            CreatedAt = ui.CreatedAt,
            Notes = ui.Notes
        };
    }
}
```

Removed: optional display-name parameters on `ToModel` (`webUserEmail`, `telegramUsername`, etc.) — no longer needed.

Also: the `UsernameBlacklistRepository.GetAllEntriesAsync` / `GetEnabledEntriesAsync` calls to `.ToModel()` no longer pass display-name lookups. The repository is simplified — no need to JOIN against `users` / `telegram_users` to resolve actor display names.

### 5. Service-layer mutation methods

Add to `IUsernameBlacklistService`:

```csharp
public interface IUsernameBlacklistService
{
    Task<UsernameBlacklistEntry?> CheckDisplayNameAsync(string displayName, CancellationToken ct = default);

    // New: write operations with audit
    Task<long> AddEntryAsync(string pattern, BlacklistMatchType matchType, string? notes, Actor actor, CancellationToken ct = default);
    Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default);
    Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default);
}
```

`pattern` is passed to `Delete`/`SetEnabled`/`UpdateNotes` because the audit log entry needs it for the value string — and looking it up requires an extra DB round-trip if the service doesn't already have it. The UI has the pattern in hand; passing it avoids the round-trip and matches the existing audit-call shape.

Implementation in `UsernameBlacklistService`:

```csharp
public class UsernameBlacklistService(
    IUsernameBlacklistRepository repository,
    IAuditService auditService) : IUsernameBlacklistService
{
    public async Task<long> AddEntryAsync(
        string pattern, BlacklistMatchType matchType, string? notes,
        Actor actor, CancellationToken ct = default)
    {
        var entry = new UsernameBlacklistEntry(
            Id: 0, Pattern: pattern, MatchType: matchType,
            Enabled: true, CreatedAt: DateTimeOffset.UtcNow, Notes: notes);

        var id = await repository.AddEntryAsync(entry, ct);

        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            target: actor,
            value: pattern,
            ct);

        return id;
    }

    public async Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteEntryAsync(id, ct);
        if (deleted)
        {
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryRemoved,
                actor, target: actor, value: pattern, ct);
        }
        return deleted;
    }

    public async Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default)
    {
        var updated = await repository.SetEnabledAsync(id, enabled, ct);
        if (updated)
        {
            await auditService.LogEventAsync(
                enabled ? AuditEventType.BlacklistEntryEnabled : AuditEventType.BlacklistEntryDisabled,
                actor, target: actor, value: pattern, ct);
        }
        return updated;
    }

    public async Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default)
    {
        var updated = await repository.UpdateNotesAsync(id, notes, ct);
        if (updated)
        {
            await auditService.LogEventAsync(
                AuditEventType.BlacklistEntryNotesChanged,
                actor, target: actor, value: pattern, ct);
        }
        return updated;
    }
}
```

Order: data write first, then audit. If audit fails, the data change still happened — the audit exception bubbles up and the operation is marked failed at the caller, but the data persists. This matches existing patterns elsewhere in the codebase. Making the pair transactional (via `IExecutionStrategy.ExecuteAsync` + `BeginTransactionAsync`) is a separate enhancement, out of scope.

### 6. New `AuditEventType` values

Append to `AuditEventType` enum (next unused value is 39):

```csharp
/// <summary>Username blacklist entry added</summary>
BlacklistEntryAdded = 39,

/// <summary>Username blacklist entry removed</summary>
BlacklistEntryRemoved = 40,

/// <summary>Username blacklist entry enabled</summary>
BlacklistEntryEnabled = 41,

/// <summary>Username blacklist entry disabled</summary>
BlacklistEntryDisabled = 42,

/// <summary>Username blacklist entry notes changed</summary>
BlacklistEntryNotesChanged = 43,
```

(Same enum lives in `TelegramGroupsAdmin.Data/Models/AuditEventType.cs` and `TelegramGroupsAdmin.Core/Models/AuditEventType.cs` — update both. Confirm whether one is canonical and the other generated/duplicated; if duplicated, that's a pre-existing smell worth flagging in PR review but out of scope here.)

### 7. UI refactor

`UsernameBlacklistSettings.razor`:
- Inject `IUsernameBlacklistService` instead of `IUsernameBlacklistRepository`. (Repository can stay injected for the read methods `GetAllEntriesAsync` / `GetEnabledEntriesAsync` / `ExistsAsync` until those move to the service too — but per `global_feedback_repository_through_service`, the read methods should also go through the service. Adding the four read methods to the service interface as straight pass-throughs is a small additional cost in this PR; doing so closes the architectural gap completely for this entity.)
- Replace each `BlacklistRepository.X(entry)` + `AuditService.LogEventAsync(...)` pair with a single `BlacklistService.X(args, WebUser!.ToActor())` call.
- Drop the `IAuditService` injection (no longer needed at this layer).
- Drop the `CreatedBy: WebUser!.ToActor()` argument when constructing `UsernameBlacklistEntry` for an `AddEntry` call — domain model no longer has that field.

### 8. Repository unchanged (almost)

`UsernameBlacklistRepository` keeps its existing method signatures; the body simplifies because there are no actor columns to write. `logger.LogInformation` calls stay (low-volume operational logs).

## Files

- `TelegramGroupsAdmin.Data/AppDbContext.cs:534-557` — remove FK + CHECK config.
- `TelegramGroupsAdmin.Data/Models/UsernameBlacklistEntryDto.cs` — drop 3 columns.
- `TelegramGroupsAdmin.Data/Migrations/<timestamp>_DropUsernameBlacklistActorColumns.cs` — new migration.
- `TelegramGroupsAdmin.Telegram/Models/UsernameBlacklistEntry.cs` — drop `CreatedBy`.
- `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameBlacklistMappings.cs` — simplify mappings.
- `TelegramGroupsAdmin.Telegram/Repositories/UsernameBlacklistRepository.cs` — remove `.ToModel()` display-name overload arguments.
- `TelegramGroupsAdmin.Telegram/Services/IUsernameBlacklistService.cs` — add 4 mutation methods (and read pass-throughs).
- `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs` — implement.
- `TelegramGroupsAdmin.Data/Models/AuditEventType.cs` and `TelegramGroupsAdmin.Core/Models/AuditEventType.cs` — add 5 enum values.
- `TelegramGroupsAdmin/Components/Shared/Settings/UsernameBlacklistSettings.razor` — switch to service injection; drop direct `AuditService` calls.

## Tests

Unit tests for `UsernameBlacklistService`:
- `AddEntryAsync` calls repository, then calls `IAuditService.LogEventAsync` with `BlacklistEntryAdded` and the pattern as value.
- `DeleteEntryAsync` only writes audit when repository returns true (entry actually deleted). Returns false → no audit call.
- `SetEnabledAsync(true)` → `BlacklistEntryEnabled`; `SetEnabledAsync(false)` → `BlacklistEntryDisabled`.
- `UpdateNotesAsync` → `BlacklistEntryNotesChanged`.
- If `IAuditService.LogEventAsync` throws, the exception bubbles up (no swallow).

Integration test for migration:
- Apply migration to a seeded test DB containing rows with various actor columns populated.
- Assert columns + CHECK + FKs are dropped; pattern / match_type / enabled / created_at / notes rows survive intact.

Update existing tests:
- `UsernameBlacklistServiceTests` — extend with new mutation tests.
- Any tests asserting `CreatedBy` on read paths — remove assertions (field no longer exists).

## Acceptance Criteria

- [ ] Migration drops `web_user_id`, `telegram_user_id`, `system_identifier`, `CK_username_blacklist_exclusive_actor`, and both FKs from `username_blacklist`.
- [ ] `UsernameBlacklistEntry` domain model has no `CreatedBy` field.
- [ ] `IUsernameBlacklistService` exposes Add / Delete / SetEnabled / UpdateNotes (and optionally GetAll / GetEnabled / Exists pass-throughs).
- [ ] `UsernameBlacklistSettings.razor` calls the service for every mutation; no direct `IAuditService` or `IUsernameBlacklistRepository` calls remain in this page.
- [ ] All blacklist mutations produce an audit-log entry with the dedicated `BlacklistEntry*` event type.
- [ ] Hard-deleting a `users` row that previously created a blacklist entry no longer fails (no FK to enforce; the blacklist entry's provenance lives in audit_log).
- [ ] All existing tests pass; new service unit tests pass.

## Out of Scope

- Making the data-write + audit-write transactional (separate enhancement).
- Deduplicating `AuditEventType` enum across Core and Data namespaces (pre-existing smell).
- Moving the `IsBanned` / `IsTrusted` / display-name JOIN paths out of `UsernameBlacklistRepository` reads (the simplification falls out of this work, but no behavior change).
- Backfilling historical audit_log entries from the dead actor columns before migration. Historical provenance for pre-existing blacklist entries is lost. Per the project's "no compat shim" stance, this is acceptable — new audit data starts at this migration.

## Related

- **#466** lands first on this branch. It unblocks restore by bringing `username_blacklist` back into the wipe-list (so SET NULL never fires during restore — though after this PR there is no SET NULL on the table at all).
- After both #466 and #467 land, `UsernameBlacklistEntryDto` is fully a "real" backup-discoverable entity AND has no FK/CHECK conflict on parent delete.
