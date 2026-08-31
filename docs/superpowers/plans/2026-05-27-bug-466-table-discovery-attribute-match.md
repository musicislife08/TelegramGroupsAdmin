# #466 TableDiscoveryService [Table] Attribute Match — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `TableDiscoveryService` from silently skipping table-backed DTOs whose names don't fit the four naming-convention candidates (`UsernameBlacklistEntryDto` for `username_blacklist`). Switch to `[Table]` attribute matching and delete the dead convention helpers.

**Architecture:** `FindDtoForTable` reduces to a single LINQ query over `dto.GetCustomAttribute<TableAttribute>()`. `ToPascalCase` and `Singularize` go away entirely. A new invariant test in the integration project enforces "every `Data.Models.*Dto` either has `[Table]` or is in an explicit exception list" — catches future drift at CI time rather than via silent backup data loss.

**Tech Stack:** .NET 10 reflection, EF Core `[Table]` attribute, NUnit.

**Spec:** `docs/superpowers/specs/2026-05-27-bug-466-table-discovery-attribute-match-design.md`

---

## File Structure

- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableDiscoveryService.cs` — rewrite, drop helpers
- Create: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs`

---

## Task 1: Invariant test — every Data.Models.*Dto class has [Table] or is in the exception list

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using NUnit.Framework;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Backup;

[TestFixture]
public class TableDiscoveryServiceTests
{
    [Test]
    public void EveryTableBackedDtoHasTableAttribute()
    {
        // DTOs that are intentionally not backed by a regular table:
        //  - InviteWithCreatorDto: join projection
        //  - RawAlgorithmPerformanceStatsDto: keyless, configured for SqlQuery
        var expectedNonTableBacked = new HashSet<string>
        {
            "InviteWithCreatorDto",
            "RawAlgorithmPerformanceStatsDto",
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
            $"These DTOs need [Table(\"...\")] (or add to expectedNonTableBacked if intentional): " +
            string.Join(", ", missingTableAttr));
    }
}
```

- [ ] **Step 2: Run test to verify state of the world**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TableDiscoveryServiceTests.EveryTableBackedDtoHasTableAttribute"`

Expected: PASS — per the issue's audit, every table-backed DTO already has `[Table]`. If it fails, the failure message lists which DTOs need attention. The test is a permanent regression guard from this point forward.

---

## Task 2: Unit test — FindDtoForTable resolves username_blacklist via [Table]

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs`

- [ ] **Step 1: Add positive-match test**

```csharp
[Test]
public void FindDtoForTable_ResolvesUsernameBlacklist_ToUsernameBlacklistEntryDto()
{
    var dtoTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
        .Where(t => t.Name.EndsWith("Dto") && t.IsClass)
        .ToList();

    var result = TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers
        .TableDiscoveryService.FindDtoForTable("username_blacklist", dtoTypes);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Name, Is.EqualTo("UsernameBlacklistEntryDto"));
}

[Test]
public void FindDtoForTable_ReturnsNullForUnmatchedTable()
{
    var dtoTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
        .Where(t => t.Name.EndsWith("Dto") && t.IsClass)
        .ToList();

    var result = TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers
        .TableDiscoveryService.FindDtoForTable("qrtz_locks", dtoTypes);

    Assert.That(result, Is.Null);
}

[Test]
public void FindDtoForTable_IsCaseInsensitive()
{
    var dtoTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t.Namespace == "TelegramGroupsAdmin.Data.Models")
        .Where(t => t.Name.EndsWith("Dto") && t.IsClass)
        .ToList();

    var result = TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers
        .TableDiscoveryService.FindDtoForTable("USERNAME_BLACKLIST", dtoTypes);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Name, Is.EqualTo("UsernameBlacklistEntryDto"));
}
```

(Requires `FindDtoForTable` to be `internal static` — accessible to the integration-test assembly via `[assembly: InternalsVisibleTo]`. If the IntegrationTests project doesn't currently have access, add `[assembly: InternalsVisibleTo("TelegramGroupsAdmin.IntegrationTests")]` to `TelegramGroupsAdmin.BackgroundJobs/AssemblyInfo.cs` or equivalent — there's likely already such a directive for other internal-tested code; check before adding.)

- [ ] **Step 2: Run the positive-match test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~FindDtoForTable_ResolvesUsernameBlacklist"`

Expected: FAIL — current `FindDtoForTable` returns `null` for `username_blacklist` (the bug).

---

## Task 3: Rewrite FindDtoForTable, delete dead helpers

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableDiscoveryService.cs`

- [ ] **Step 1: Replace `FindDtoForTable` and remove the helpers**

Replace lines 76-116 with:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

// ... rest of usings ...

internal static Type? FindDtoForTable(string tableName, IReadOnlyList<Type> dtoTypes)
{
    return dtoTypes.FirstOrDefault(dto =>
        dto.GetCustomAttribute<TableAttribute>()?.Name?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
}
```

Also: change the caller signature in `DiscoverTablesAsync` from `List<Type>` to `IReadOnlyList<Type>` if the IDE prompts on type-mismatch — `FindDtoForTable` now takes `IReadOnlyList<Type>`. (If preferred, leave both as `List<Type>` — the `List<T>` argument satisfies `IReadOnlyList<T>` covariantly.)

Add the `using System.ComponentModel.DataAnnotations.Schema;` and `using System.Reflection;` at the top of the file. Remove the old `using Dapper;` if no longer needed (it should still be needed for `connection.QueryAsync<string>` in `DiscoverTablesAsync`).

Delete entirely:
- `ToPascalCase` (was lines 98-106)
- `Singularize` (was lines 108-116)
- The XML-doc comment block above `FindDtoForTable` referencing naming conventions and examples.

- [ ] **Step 2: Run the FindDtoForTable tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TableDiscoveryServiceTests.FindDtoForTable"`

Expected: PASS (3 tests).

- [ ] **Step 3: Run the invariant test**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~EveryTableBackedDtoHasTableAttribute"`

Expected: PASS.

---

## Task 4: End-to-end discovery test against a real test database

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs`

- [ ] **Step 1: Add full-discovery test**

```csharp
[Test]
public async Task DiscoverTablesAsync_IncludesUsernameBlacklist()
{
    using var connection = new Npgsql.NpgsqlConnection(ConnectionString); // from IntegrationTestBase
    await connection.OpenAsync();

    var logger = NSubstitute.Substitute.For<
        Microsoft.Extensions.Logging.ILogger<
            TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers.TableDiscoveryService>>();
    var service = new TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Handlers
        .TableDiscoveryService(logger);

    var mapping = await service.DiscoverTablesAsync(connection);

    Assert.That(mapping.ContainsKey("username_blacklist"), Is.True,
        $"Expected username_blacklist in mapping. Actual keys: {string.Join(", ", mapping.Keys)}");
    Assert.That(mapping["username_blacklist"].Name, Is.EqualTo("UsernameBlacklistEntryDto"));
}
```

(`ConnectionString` accessor depends on the existing integration-test base; if the base exposes an `AppDbContext` factory instead, use `factory.CreateDbContext().Database.GetDbConnection().ConnectionString` to obtain it, or follow the pattern in `BackupServiceTests.cs`.)

- [ ] **Step 2: Run the discovery test**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~DiscoverTablesAsync_IncludesUsernameBlacklist"`

Expected: PASS.

---

## Task 5: Final verification + commit

- [ ] **Step 1: Run existing backup integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BackupServiceTests"`

Expected: all tests pass. (If any test implicitly relied on the silent-skip behavior of `username_blacklist`, it surfaces here.)

- [ ] **Step 2: Run the full integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`

Expected: all tests pass.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build`

Expected: 0 errors. Warn-as-error should not introduce new warnings (we removed code; nothing added).

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableDiscoveryService.cs \
        TelegramGroupsAdmin.IntegrationTests/Services/Backup/TableDiscoveryServiceTests.cs

git commit -m "$(cat <<'EOF'
fix(backup): use [Table] attribute for DTO discovery, drop convention fallback

Closes #466.

TableDiscoveryService.FindDtoForTable now matches DTOs by the [Table]
attribute exclusively. ToPascalCase and Singularize convention helpers
are deleted. UsernameBlacklistEntryDto (and any future DTO whose name
doesn't fit the four naming candidates) is now discovered correctly,
unblocking backup-export and restore-wipe coverage for username_blacklist.

A new invariant test asserts every Data.Models.*Dto class has [Table] or
is in an explicit non-table-backed exception list — catches future drift
at CI time rather than via silent backup data loss.

Operational note: backups taken before this fix lands are missing
username_blacklist data. Re-take a fresh backup post-merge if blacklist
provenance is needed for any prod-to-dev sync.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
