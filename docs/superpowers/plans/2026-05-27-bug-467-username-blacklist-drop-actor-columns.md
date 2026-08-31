# #467 username_blacklist Drop Actor Columns + Lift Audit to Service — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the FK/CHECK conflict on `username_blacklist` by dropping the dead actor columns. Lift audit-log responsibility from `UsernameBlacklistSettings.razor` into a set of new `IUsernameBlacklistService` mutation methods. Add dedicated `AuditEventType` values so future audit queries can filter by event type instead of grepping a stringly-typed value.

**Architecture:** Five coordinated changes that compile-clean at each task boundary:
1. Append five new `AuditEventType` values in both Core and Data namespaces.
2. Extend `IUsernameBlacklistService` with `AddEntryAsync` / `DeleteEntryAsync` / `SetEnabledAsync` / `UpdateNotesAsync`, each writing audit + delegating to repo.
3. Switch `UsernameBlacklistSettings.razor` to call the service (drops direct `IAuditService` calls from the page).
4. Drop `CreatedBy` from the `UsernameBlacklistEntry` domain record; simplify mapping (no `ActorMappings`).
5. Drop the three actor columns + CHECK + two FKs from `UsernameBlacklistEntryDto` + `AppDbContext` Fluent API; generate EF Core migration.

**Tech Stack:** .NET 10, Blazor Server, EF Core 10 (code-first migrations), PostgreSQL 18, NUnit.

**Spec:** `docs/superpowers/specs/2026-05-27-bug-467-username-blacklist-drop-actor-columns-design.md`

---

## File Structure

- Modify: `TelegramGroupsAdmin.Core/Models/AuditEventType.cs` — append 5 enum values
- Modify: `TelegramGroupsAdmin.Data/Models/AuditEventType.cs` — same 5 values (duplicate enum noted as pre-existing smell, out of scope to dedupe)
- Modify: `TelegramGroupsAdmin.Telegram/Services/IUsernameBlacklistService.cs` — 4 new method signatures
- Modify: `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs` — 4 new method implementations
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/UsernameBlacklistSettings.razor` — call service, drop direct audit calls
- Modify: `TelegramGroupsAdmin.Telegram/Models/UsernameBlacklistEntry.cs` — drop `CreatedBy`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameBlacklistMappings.cs` — simplify
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/UsernameBlacklistRepository.cs` — remove display-name-overload arguments
- Modify: `TelegramGroupsAdmin.Data/Models/UsernameBlacklistEntryDto.cs` — drop 3 columns
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs:534-557` — drop FK + CHECK config
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_DropUsernameBlacklistActorColumns.cs` — auto-generated
- Create: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs`

---

## Task 1: Append new AuditEventType values

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Models/AuditEventType.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/AuditEventType.cs`

- [ ] **Step 1: Add 5 enum values to Core/Models/AuditEventType.cs**

Append after the last value (line 130, value `TelegramAccountLinked = 38`):

```csharp
// Username Blacklist (39-43)
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

- [ ] **Step 2: Mirror in Data/Models/AuditEventType.cs**

Add the same five values with the same numeric assignments. (The duplication across Core and Data namespaces is a pre-existing smell, not in scope to fix.)

- [ ] **Step 3: Build**

Run: `dotnet build`

Expected: 0 errors.

---

## Task 2: Unit test for `UsernameBlacklistService.AddEntryAsync` writes audit + delegates to repo

**Files:**
- Create: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

[TestFixture]
public class UsernameBlacklistServiceMutationTests
{
    private IUsernameBlacklistRepository _repo = null!;
    private IAuditService _audit = null!;
    private UsernameBlacklistService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IUsernameBlacklistRepository>();
        _audit = Substitute.For<IAuditService>();
        _service = new UsernameBlacklistService(_repo, _audit);
    }

    [Test]
    public async Task AddEntryAsync_CallsRepoThenWritesAuditWithDedicatedEventType()
    {
        _repo.AddEntryAsync(Arg.Any<UsernameBlacklistEntry>(), Arg.Any<CancellationToken>())
            .Returns(42L);

        var actor = Actor.WebAdmin;
        var id = await _service.AddEntryAsync(
            "spam-pattern",
            BlacklistMatchType.Exact,
            notes: "test notes",
            actor: actor,
            ct: CancellationToken.None);

        Assert.That(id, Is.EqualTo(42L));

        await _repo.Received(1).AddEntryAsync(
            Arg.Is<UsernameBlacklistEntry>(e =>
                e.Pattern == "spam-pattern"
                && e.MatchType == BlacklistMatchType.Exact
                && e.Notes == "test notes"
                && e.Enabled == true),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            actor,
            "spam-pattern",
            Arg.Any<CancellationToken>());
    }
}
```

(Confirm the actual `IAuditService.LogEventAsync` signature before finalizing — the order of `actor`, `target`, and `value` arguments must match the production interface. If `LogEventAsync` uses named parameters in production calls today, mirror that.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests.AddEntryAsync_CallsRepoThenWritesAuditWithDedicatedEventType"`

Expected: FAIL — `UsernameBlacklistService.AddEntryAsync` does not exist; constructor doesn't accept `IAuditService`.

---

## Task 3: Implement `UsernameBlacklistService.AddEntryAsync`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/IUsernameBlacklistService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs`

- [ ] **Step 1: Extend the service interface**

`IUsernameBlacklistService.cs`:

```csharp
public interface IUsernameBlacklistService
{
    Task<UsernameBlacklistEntry?> CheckDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);

    Task<long> AddEntryAsync(
        string pattern,
        BlacklistMatchType matchType,
        string? notes,
        Actor actor,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement on the service**

`UsernameBlacklistService.cs`:

```csharp
public class UsernameBlacklistService(
    IUsernameBlacklistRepository repository,
    IAuditService auditService) : IUsernameBlacklistService
{
    public async Task<UsernameBlacklistEntry?> CheckDisplayNameAsync(
        string displayName, CancellationToken cancellationToken = default)
    {
        // ... existing body ...
    }

    public async Task<long> AddEntryAsync(
        string pattern,
        BlacklistMatchType matchType,
        string? notes,
        Actor actor,
        CancellationToken ct = default)
    {
        var entry = new UsernameBlacklistEntry(
            Id: 0,
            Pattern: pattern,
            MatchType: matchType,
            Enabled: true,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: actor,            // remove this argument when domain model loses CreatedBy in Task 9
            Notes: notes);

        var id = await repository.AddEntryAsync(entry, ct);

        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryAdded,
            actor,
            actor,
            pattern,
            ct);

        return id;
    }
}
```

The `CreatedBy: actor` argument is transitional — removed in Task 9 once the domain model is updated. Keeping it here keeps the compile clean while we incrementally land changes.

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~AddEntryAsync_CallsRepoThenWritesAuditWithDedicatedEventType"`

Expected: PASS.

---

## Task 4: Add DeleteEntryAsync (test + impl)

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/IUsernameBlacklistService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs`

- [ ] **Step 1: Add failing test for `DeleteEntryAsync`**

```csharp
[Test]
public async Task DeleteEntryAsync_WhenRepoReturnsTrue_WritesAudit()
{
    _repo.DeleteEntryAsync(7L, Arg.Any<CancellationToken>()).Returns(true);
    var actor = Actor.WebAdmin;

    var result = await _service.DeleteEntryAsync(7L, "test-pattern", actor, CancellationToken.None);

    Assert.That(result, Is.True);
    await _audit.Received(1).LogEventAsync(
        AuditEventType.BlacklistEntryRemoved,
        actor, actor, "test-pattern",
        Arg.Any<CancellationToken>());
}

[Test]
public async Task DeleteEntryAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
{
    _repo.DeleteEntryAsync(7L, Arg.Any<CancellationToken>()).Returns(false);
    var actor = Actor.WebAdmin;

    var result = await _service.DeleteEntryAsync(7L, "test-pattern", actor, CancellationToken.None);

    Assert.That(result, Is.False);
    await _audit.DidNotReceive().LogEventAsync(
        Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor>(),
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Verify both tests fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests.DeleteEntryAsync"`

Expected: FAIL — method doesn't exist.

- [ ] **Step 3: Implement interface + service method**

Interface addition:

```csharp
Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default);
```

Service implementation:

```csharp
public async Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default)
{
    var deleted = await repository.DeleteEntryAsync(id, ct);
    if (deleted)
    {
        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryRemoved,
            actor, actor, pattern, ct);
    }
    return deleted;
}
```

- [ ] **Step 4: Verify tests pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests.DeleteEntryAsync"`

Expected: PASS (2 tests).

---

## Task 5: Add SetEnabledAsync (test + impl)

**Files:**
- Modify: same three files

- [ ] **Step 1: Add failing tests**

```csharp
[Test]
public async Task SetEnabledAsync_True_WritesEnabledAuditEvent()
{
    _repo.SetEnabledAsync(7L, true, Arg.Any<CancellationToken>()).Returns(true);
    var actor = Actor.WebAdmin;

    var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: true, actor, CancellationToken.None);

    Assert.That(result, Is.True);
    await _audit.Received(1).LogEventAsync(
        AuditEventType.BlacklistEntryEnabled,
        actor, actor, "test-pattern",
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SetEnabledAsync_False_WritesDisabledAuditEvent()
{
    _repo.SetEnabledAsync(7L, false, Arg.Any<CancellationToken>()).Returns(true);
    var actor = Actor.WebAdmin;

    var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: false, actor, CancellationToken.None);

    Assert.That(result, Is.True);
    await _audit.Received(1).LogEventAsync(
        AuditEventType.BlacklistEntryDisabled,
        actor, actor, "test-pattern",
        Arg.Any<CancellationToken>());
}

[Test]
public async Task SetEnabledAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
{
    _repo.SetEnabledAsync(7L, true, Arg.Any<CancellationToken>()).Returns(false);
    var actor = Actor.WebAdmin;

    var result = await _service.SetEnabledAsync(7L, "test-pattern", enabled: true, actor, CancellationToken.None);

    Assert.That(result, Is.False);
    await _audit.DidNotReceive().LogEventAsync(
        Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor>(),
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run tests to confirm failure**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests.SetEnabledAsync"`

Expected: FAIL.

- [ ] **Step 3: Implement interface + service method**

Interface:

```csharp
Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default);
```

Service:

```csharp
public async Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default)
{
    var updated = await repository.SetEnabledAsync(id, enabled, ct);
    if (updated)
    {
        var eventType = enabled
            ? AuditEventType.BlacklistEntryEnabled
            : AuditEventType.BlacklistEntryDisabled;
        await auditService.LogEventAsync(eventType, actor, actor, pattern, ct);
    }
    return updated;
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UsernameBlacklistServiceMutationTests.SetEnabledAsync"`

Expected: PASS (3 tests).

---

## Task 6: Add UpdateNotesAsync (test + impl)

**Files:**
- Modify: same three files

- [ ] **Step 1: Add failing tests**

```csharp
[Test]
public async Task UpdateNotesAsync_WhenRepoReturnsTrue_WritesAudit()
{
    _repo.UpdateNotesAsync(7L, "new notes", Arg.Any<CancellationToken>()).Returns(true);
    var actor = Actor.WebAdmin;

    var result = await _service.UpdateNotesAsync(7L, "test-pattern", "new notes", actor, CancellationToken.None);

    Assert.That(result, Is.True);
    await _audit.Received(1).LogEventAsync(
        AuditEventType.BlacklistEntryNotesChanged,
        actor, actor, "test-pattern",
        Arg.Any<CancellationToken>());
}

[Test]
public async Task UpdateNotesAsync_WhenRepoReturnsFalse_DoesNotWriteAudit()
{
    _repo.UpdateNotesAsync(7L, "new notes", Arg.Any<CancellationToken>()).Returns(false);
    var actor = Actor.WebAdmin;

    var result = await _service.UpdateNotesAsync(7L, "test-pattern", "new notes", actor, CancellationToken.None);

    Assert.That(result, Is.False);
    await _audit.DidNotReceive().LogEventAsync(
        Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor>(),
        Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Confirm failure**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UpdateNotesAsync"`

Expected: FAIL.

- [ ] **Step 3: Implement interface + service method**

Interface:

```csharp
Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default);
```

Service:

```csharp
public async Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default)
{
    var updated = await repository.UpdateNotesAsync(id, notes, ct);
    if (updated)
    {
        await auditService.LogEventAsync(
            AuditEventType.BlacklistEntryNotesChanged,
            actor, actor, pattern, ct);
    }
    return updated;
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UpdateNotesAsync"`

Expected: PASS (2 tests).

---

## Task 7: Switch UI to use the new service mutation methods

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/Settings/UsernameBlacklistSettings.razor`

- [ ] **Step 1: Update injection**

At the top of the file (lines 1-5), replace:

```razor
@inject IUsernameBlacklistRepository BlacklistRepository
@inject IAuditService AuditService
```

with:

```razor
@inject IUsernameBlacklistService BlacklistService
@inject IUsernameBlacklistRepository BlacklistRepository
```

(Repository stays injected because the page still calls `GetAllEntriesAsync` and `ExistsAsync` for reads. Optional follow-up: expose read methods on the service too, then drop the repo injection — out of scope for #467.)

`IAuditService` injection is removed entirely from this page.

- [ ] **Step 2: Rewrite the `AddEntry` method**

Replace lines 115-162 of the `@code { }` block:

```csharp
private async Task AddEntry()
{
    if (_busy) return;
    _busy = true;
    try
    {
        var dialog = await DialogService.ShowAsync<AddBlacklistEntryDialog>("Add Blacklist Entry");
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: AddBlacklistEntryData data })
        {
            if (await BlacklistRepository.ExistsAsync(data.Pattern))
            {
                Snackbar.Add($"Pattern \"{data.Pattern}\" already exists", Severity.Warning);
                return;
            }

            await BlacklistService.AddEntryAsync(
                pattern: data.Pattern,
                matchType: BlacklistMatchType.Exact,
                notes: data.Notes,
                actor: WebUser!.ToActor());

            Snackbar.Add($"Blacklisted \"{data.Pattern}\"", Severity.Success);
            await LoadEntries();
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Error adding entry: {ex.Message}", Severity.Error);
    }
    finally
    {
        _busy = false;
    }
}
```

- [ ] **Step 3: Rewrite `ToggleEnabled`**

Replace lines 164-185:

```csharp
private async Task ToggleEnabled(UsernameBlacklistEntry entry)
{
    var newState = !entry.Enabled;
    var ok = await BlacklistService.SetEnabledAsync(
        entry.Id, entry.Pattern, newState, WebUser!.ToActor());

    if (ok)
    {
        Snackbar.Add($"{(newState ? "Enabled" : "Disabled")} \"{entry.Pattern}\"", Severity.Success);
        await LoadEntries();
    }
    else
    {
        Snackbar.Add("Entry not found — it may have been deleted", Severity.Warning);
        await LoadEntries();
    }
}
```

- [ ] **Step 4: Rewrite `DeleteEntry`**

Replace lines 187-215:

```csharp
private async Task DeleteEntry(UsernameBlacklistEntry entry)
{
    var confirmed = await DialogService.ShowMessageBoxAsync(
        "Delete Blacklist Entry",
        $"Remove \"{entry.Pattern}\" from the blacklist?",
        yesText: "Delete",
        cancelText: "Cancel");

    if (confirmed == true)
    {
        var ok = await BlacklistService.DeleteEntryAsync(
            entry.Id, entry.Pattern, WebUser!.ToActor());

        if (ok)
        {
            Snackbar.Add($"Deleted \"{entry.Pattern}\"", Severity.Success);
            await LoadEntries();
        }
        else
        {
            Snackbar.Add("Entry not found — it may have already been deleted", Severity.Warning);
            await LoadEntries();
        }
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build`

Expected: 0 errors. (The domain model still has `CreatedBy` — we drop it in Task 9. The transitional `CreatedBy: actor` argument inside `UsernameBlacklistService.AddEntryAsync` keeps the compile clean until then.)

---

## Task 8: Drop AppDbContext FK + CHECK config, drop DTO columns, generate migration

**Files:**
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs:534-557`
- Modify: `TelegramGroupsAdmin.Data/Models/UsernameBlacklistEntryDto.cs`
- Create: `TelegramGroupsAdmin.Data/Migrations/<timestamp>_DropUsernameBlacklistActorColumns.cs`

- [ ] **Step 1: Simplify the Fluent API block**

Replace lines 534-557 of `AppDbContext.cs` with:

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

(Removed: both `HasOne<>().WithMany().HasForeignKey().OnDelete(DeleteBehavior.SetNull)` calls and the `entity.ToTable(t => t.HasCheckConstraint(...))` call.)

- [ ] **Step 2: Drop the three columns from the DTO**

Replace `UsernameBlacklistEntryDto.cs` body:

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

(Removed: `WebUserId`, `TelegramUserId`, `SystemIdentifier` properties and the explanatory comment.)

- [ ] **Step 3: Generate the EF Core migration**

Run: `dotnet ef migrations add DropUsernameBlacklistActorColumns -p TelegramGroupsAdmin.Data -s TelegramGroupsAdmin`

- [ ] **Step 4: Review the generated migration**

Open `TelegramGroupsAdmin.Data/Migrations/<timestamp>_DropUsernameBlacklistActorColumns.cs`. Confirm the `Up` method:
- Drops both FK constraints (`FK_username_blacklist_telegram_users_telegram_user_id` and `FK_username_blacklist_users_web_user_id`)
- Drops the check constraint (`CK_username_blacklist_exclusive_actor`)
- Drops the three columns (`web_user_id`, `telegram_user_id`, `system_identifier`)

Order should be: drop FKs and CHECK *before* dropping the columns those reference. EF Core usually generates this correctly — if it generates DROP COLUMN before DROP CONSTRAINT (Postgres allows this in some cases but it's noisy), reorder manually.

Confirm the `Down` method reconstructs the columns + CHECK + FKs (it should mirror the `Up` reversed). The `Down` is the rollback path; correctness matters even if we expect not to use it.

- [ ] **Step 5: Build**

Run: `dotnet build`

Expected: 0 errors. The mapping in `UsernameBlacklistMappings.cs` and the domain record will currently fail to compile if they reference the dropped properties — Task 9 fixes that. To stay compile-clean across task boundaries, do Step 6 immediately.

- [ ] **Step 6: Defer build verification**

Build will fail at this point because `UsernameBlacklistMappings.ToDto/ToModel` still reference `WebUserId`/`TelegramUserId`/`SystemIdentifier` on the DTO. This is expected — Task 9 lands the matching code changes. If staging this PR is helpful, you can interleave Step 1-2 of Task 9 here. For a clean sequence of commits, run them together as one logical change (described in Task 9).

---

## Task 9: Drop CreatedBy from domain model + simplify mapping + simplify repository

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Models/UsernameBlacklistEntry.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameBlacklistMappings.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/UsernameBlacklistRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs` (remove transitional `CreatedBy: actor`)

- [ ] **Step 1: Drop `CreatedBy` from the domain record**

Replace `UsernameBlacklistEntry.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a username blacklist entry.
/// Repos accept/return this; never expose the Dto.
/// </summary>
public sealed record UsernameBlacklistEntry(
    long Id,
    string Pattern,
    BlacklistMatchType MatchType,
    bool Enabled,
    DateTimeOffset CreatedAt,
    string? Notes);
```

(Drop the `using TelegramGroupsAdmin.Core.Models;` if `Actor` was the only thing imported.)

- [ ] **Step 2: Simplify `UsernameBlacklistMappings.cs`**

Replace the full body:

```csharp
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

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

(Removed: `using TelegramGroupsAdmin.Core.Repositories.Mappings;`, all `ActorMappings` calls, all `web*Email/telegram*Name` parameters on `ToModel`.)

- [ ] **Step 3: Simplify `UsernameBlacklistRepository`**

The repository methods `GetEnabledEntriesAsync` and `GetAllEntriesAsync` previously may have JOIN'd `users` / `telegram_users` to resolve display names for `ActorMappings.ToActor`. Now `.ToModel()` takes no arguments — the JOINs are unnecessary if they existed.

Review the current `UsernameBlacklistRepository.cs`:
- `GetEnabledEntriesAsync` calls `dtos.Select(d => d.ToModel())` — no change needed; the new `ToModel()` is parameterless.
- `GetAllEntriesAsync` same.

Confirm no parameters are being passed; remove any if so.

- [ ] **Step 4: Remove transitional `CreatedBy: actor` from `UsernameBlacklistService.AddEntryAsync`**

In `UsernameBlacklistService.cs`, change:

```csharp
var entry = new UsernameBlacklistEntry(
    Id: 0,
    Pattern: pattern,
    MatchType: matchType,
    Enabled: true,
    CreatedAt: DateTimeOffset.UtcNow,
    CreatedBy: actor,            // <-- remove this line
    Notes: notes);
```

To:

```csharp
var entry = new UsernameBlacklistEntry(
    Id: 0,
    Pattern: pattern,
    MatchType: matchType,
    Enabled: true,
    CreatedAt: DateTimeOffset.UtcNow,
    Notes: notes);
```

- [ ] **Step 5: Build**

Run: `dotnet build`

Expected: 0 errors. All code now matches the new domain shape.

- [ ] **Step 6: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: all tests pass. If any test asserts on `entry.CreatedBy`, remove the assertion — the field no longer exists.

---

## Task 10: Integration test for the migration

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Migrations/DropUsernameBlacklistActorColumnsMigrationTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

[TestFixture]
public class DropUsernameBlacklistActorColumnsMigrationTests : IntegrationTestBase
{
    [Test]
    public async Task UsernameBlacklist_HasNoActorColumns_NoCheckConstraint_NoFks()
    {
        await using var ctx = await ContextFactory.CreateDbContextAsync();

        var conn = ctx.Database.GetDbConnection();
        await conn.OpenAsync();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'username_blacklist'
                ORDER BY column_name";

            using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

            Assert.That(columns, Does.Not.Contain("web_user_id"));
            Assert.That(columns, Does.Not.Contain("telegram_user_id"));
            Assert.That(columns, Does.Not.Contain("system_identifier"));

            Assert.That(columns, Does.Contain("id"));
            Assert.That(columns, Does.Contain("pattern"));
            Assert.That(columns, Does.Contain("enabled"));
            Assert.That(columns, Does.Contain("created_at"));
            Assert.That(columns, Does.Contain("notes"));
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT conname
                FROM pg_constraint
                WHERE conrelid = 'public.username_blacklist'::regclass";

            using var reader = await cmd.ExecuteReaderAsync();
            var constraints = new List<string>();
            while (await reader.ReadAsync())
                constraints.Add(reader.GetString(0));

            Assert.That(constraints, Does.Not.Contain("CK_username_blacklist_exclusive_actor"));
            Assert.That(constraints, Does.Not.Contain("FK_username_blacklist_telegram_users_telegram_user_id"));
            Assert.That(constraints, Does.Not.Contain("FK_username_blacklist_users_web_user_id"));
        }
    }
}
```

- [ ] **Step 2: Run the test (migration is applied automatically by `IntegrationTestBase`)**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DropUsernameBlacklistActorColumnsMigrationTests"`

Expected: PASS.

---

## Task 11: Final verification + commit

- [ ] **Step 1: Run the entire integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`

Expected: all tests pass. (Watch for any test that previously seeded `username_blacklist` rows with actor columns — those seed calls need to drop the actor arguments now.)

- [ ] **Step 2: Run the entire unit test suite**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: all tests pass.

- [ ] **Step 3: Run component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests`

Expected: all tests pass. (If any bUnit test renders `UsernameBlacklistSettings.razor` and mocks the old `IUsernameBlacklistRepository.AddEntryAsync/DeleteEntryAsync/SetEnabledAsync/UpdateNotesAsync` calls, switch those mocks to `IUsernameBlacklistService` methods.)

- [ ] **Step 4: Build the full solution**

Run: `dotnet build`

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/AuditEventType.cs \
        TelegramGroupsAdmin.Data/Models/AuditEventType.cs \
        TelegramGroupsAdmin.Data/Models/UsernameBlacklistEntryDto.cs \
        TelegramGroupsAdmin.Data/AppDbContext.cs \
        TelegramGroupsAdmin.Data/Migrations/ \
        TelegramGroupsAdmin.Telegram/Models/UsernameBlacklistEntry.cs \
        TelegramGroupsAdmin.Telegram/Repositories/Mappings/UsernameBlacklistMappings.cs \
        TelegramGroupsAdmin.Telegram/Repositories/UsernameBlacklistRepository.cs \
        TelegramGroupsAdmin.Telegram/Services/IUsernameBlacklistService.cs \
        TelegramGroupsAdmin.Telegram/Services/UsernameBlacklistService.cs \
        TelegramGroupsAdmin/Components/Shared/Settings/UsernameBlacklistSettings.razor \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UsernameBlacklistServiceMutationTests.cs \
        TelegramGroupsAdmin.IntegrationTests/Migrations/DropUsernameBlacklistActorColumnsMigrationTests.cs

git commit -m "$(cat <<'EOF'
fix(blacklist): drop dead actor columns + lift audit to service layer

Closes #467.

username_blacklist drops the three actor columns (web_user_id,
telegram_user_id, system_identifier) along with the CK_username_blacklist_exclusive_actor
CHECK and both ON DELETE SET NULL FKs. The columns had no consumer
(not displayed in the UI, not used by matching logic) and were the sole
cause of the FK/CHECK conflict on parent hard-deletes.

Audit responsibility moves from UsernameBlacklistSettings.razor into a
set of new IUsernameBlacklistService mutation methods (AddEntryAsync,
DeleteEntryAsync, SetEnabledAsync, UpdateNotesAsync), each of which
writes a dedicated AuditEventType.BlacklistEntry* event in addition to
delegating the data write to the repository. The UI page no longer
injects IAuditService; future non-UI callers of the service cannot
bypass audit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Migration Notes for Release

Historical creator data on `username_blacklist` is lost at this migration boundary. Per the project's no-compat-shim stance, this is acceptable — new audit data starts at this migration. Any operator who wants pre-migration creator info must inspect a pre-migration backup or restore one to a side environment.

The audit_log table already received entries for every blacklist mutation from the prior UI-level audit calls, so historical *audit* coverage is intact. Only the row-level denormalized creator columns are lost.
