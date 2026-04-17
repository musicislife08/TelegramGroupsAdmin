# Priority-High Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three priority-high issues (#369, #276, #191) in a single PR with one commit per issue.

**Architecture:** Three independent fixes: (1) wire up existing `AlwaysRun` config flag in the content detection engine, (2) rename `FileScanResultRecord` → `FileScanResultDto` so backup discovers it, (3) remove hardcoded credentials and harden SQL with identifier quoting and parameterized queries.

**Tech Stack:** .NET 10, EF Core 10, Npgsql 10, NUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-04-08-priority-high-batch-design.md`

---

## File Map

### Commit 1 — AlwaysRun (#369)
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs:282-304`
- Modify: `TelegramGroupsAdmin.UnitTests/ContentDetection/ContentDetectionEngineV2Tests.cs` (add tests in `ShouldRunCheckV2 Guards` region)

### Commit 2 — FileScanResultRecord rename (#276)
- Rename: `TelegramGroupsAdmin.Data/Models/FileScanResultRecord.cs` → `FileScanResultDto.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs:83,721-724`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/ModelMappings.cs:36,50`
- Modify: `TelegramGroupsAdmin.Data/Migrations/AppDbContextModelSnapshot.cs:1761` (string reference)

### Commit 3 — Security hardening (#191)
- Create: `TelegramGroupsAdmin.Core/Utilities/SqlHelper.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContextFactory.cs:17-18`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs:743,837,874`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableExportService.cs:64-74`
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs:174-176`
- Modify: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs:434-437`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Migrations/SequenceIntegrityTests.cs:159-164`

---

## Task 1: Create feature branch

- [ ] **Step 1: Create and switch to feature branch**

```bash
git checkout -b fix/priority-high-batch develop
```

---

## Task 2: Write AlwaysRun tests (Commit 1 — #369)

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/ContentDetection/ContentDetectionEngineV2Tests.cs`

- [ ] **Step 1: Add three AlwaysRun test cases**

Add these tests inside the `#region ShouldRunCheckV2 Guards` section (after the existing guard tests, before `#endregion`). These tests use the existing `BuildCheck`, `BuildPermissiveConfig`, and `BuildEngine` helpers.

```csharp
[Test]
public async Task CheckMessageAsync_AlwaysRun_True_TrustedUser_CheckStillRuns()
{
    // Arrange - Spacing enabled + AlwaysRun, but user is trusted
    var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);
    // ShouldExecute returns false (simulates trusted user skip)
    check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(false);

    var config = BuildPermissiveConfig();
    config.Spacing.Enabled = true;
    config.Spacing.AlwaysRun = true;

    _configRepository
        .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(config);

    var engine = BuildEngine([check]);
    var request = new ContentCheckRequest
    {
        Message = "test message",
        User = TestUser,
        Chat = TestChat,
        IsUserTrusted = true
    };

    // Act
    var result = await engine.CheckMessageAsync(request);

    // Assert - AlwaysRun bypasses ShouldExecute, so check runs
    await check.Received(1).CheckAsync(Arg.Any<ContentCheckRequestBase>());
    Assert.That(result.TotalScore, Is.EqualTo(3.0));
}

[Test]
public async Task CheckMessageAsync_AlwaysRun_False_TrustedUser_CheckSkipped()
{
    // Arrange - Spacing enabled but NOT AlwaysRun, user is trusted
    var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);
    check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(false);

    var config = BuildPermissiveConfig();
    config.Spacing.Enabled = true;
    config.Spacing.AlwaysRun = false;

    _configRepository
        .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(config);

    var engine = BuildEngine([check]);
    var request = new ContentCheckRequest
    {
        Message = "test message",
        User = TestUser,
        Chat = TestChat,
        IsUserTrusted = true
    };

    // Act
    var result = await engine.CheckMessageAsync(request);

    // Assert - ShouldExecute returned false, AlwaysRun is false, so check is skipped
    await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
    Assert.That(result.TotalScore, Is.EqualTo(0.0));
}

[Test]
public async Task CheckMessageAsync_AlwaysRun_True_Disabled_CheckStillSkipped()
{
    // Arrange - Spacing disabled, even with AlwaysRun = true
    var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);

    var config = BuildPermissiveConfig();
    config.Spacing.Enabled = false;
    config.Spacing.AlwaysRun = true;

    _configRepository
        .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
        .Returns(config);

    var engine = BuildEngine([check]);
    var request = BuildRequest("test message");

    // Act
    var result = await engine.CheckMessageAsync(request);

    // Assert - Enabled=false short-circuits, AlwaysRun doesn't matter
    await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
    Assert.That(result.TotalScore, Is.EqualTo(0.0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "AlwaysRun" --no-restore -v quiet`

Expected: `CheckMessageAsync_AlwaysRun_True_TrustedUser_CheckStillRuns` FAILS (check not called because AlwaysRun isn't wired up yet). The other two should PASS (they test existing behavior).

---

## Task 3: Implement AlwaysRun in ShouldRunCheckV2 (Commit 1 — #369)

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs:282-304`

- [ ] **Step 1: Add AlwaysRun switch and guard**

In `ShouldRunCheckV2()`, between `if (!enabled) return false;` (line 300-301) and `return check.ShouldExecute(request);` (line 303), insert the AlwaysRun logic:

```csharp
    private bool ShouldRunCheckV2(IContentCheckV2 check, ContentCheckRequest request, ContentDetectionConfig config)
    {
        var enabled = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.Enabled,
            CheckName.Bayes => config.Bayes.Enabled,
            // CAS moved to user join flow (WelcomeService) - checks USER not MESSAGE
            CheckName.Similarity => config.Similarity.Enabled,
            CheckName.Spacing => config.Spacing.Enabled,
            CheckName.InvisibleChars => config.InvisibleChars.Enabled,
            CheckName.ThreatIntel => config.ThreatIntel.Enabled && request.Urls.Any(),
            CheckName.UrlBlocklist => config.UrlBlocklist.Enabled && request.Urls.Any(),
            CheckName.ImageSpam => config.ImageSpam.Enabled && (request.ImageData != null || !string.IsNullOrEmpty(request.PhotoFileId) || !string.IsNullOrEmpty(request.PhotoLocalPath)),
            CheckName.VideoSpam => config.VideoSpam.Enabled && !string.IsNullOrEmpty(request.VideoLocalPath),
            CheckName.ChannelReply => config.ChannelReply.Enabled && request.Metadata.IsReplyToChannelPost,
            _ => false
        };

        if (!enabled)
            return false;

        var alwaysRun = check.CheckName switch
        {
            CheckName.StopWords => config.StopWords.AlwaysRun,
            CheckName.Bayes => config.Bayes.AlwaysRun,
            CheckName.Similarity => config.Similarity.AlwaysRun,
            CheckName.Spacing => config.Spacing.AlwaysRun,
            CheckName.InvisibleChars => config.InvisibleChars.AlwaysRun,
            CheckName.ThreatIntel => config.ThreatIntel.AlwaysRun,
            CheckName.UrlBlocklist => config.UrlBlocklist.AlwaysRun,
            CheckName.ImageSpam => config.ImageSpam.AlwaysRun,
            CheckName.VideoSpam => config.VideoSpam.AlwaysRun,
            CheckName.ChannelReply => config.ChannelReply.AlwaysRun,
            _ => false
        };

        if (alwaysRun)
            return true;

        return check.ShouldExecute(request);
    }
```

- [ ] **Step 2: Run tests to verify all three pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "AlwaysRun" --no-restore -v quiet`

Expected: All 3 AlwaysRun tests PASS.

- [ ] **Step 3: Run full engine test suite to verify no regressions**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "ContentDetectionEngineV2Tests" --no-restore -v quiet`

Expected: All existing tests still PASS.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs \
       TelegramGroupsAdmin.UnitTests/ContentDetection/ContentDetectionEngineV2Tests.cs
git commit -F- <<'EOF'
fix: wire up AlwaysRun config flag in ShouldRunCheckV2

Closes #369

The AlwaysRun boolean on check configs was persisted and displayed in
the UI but ShouldRunCheckV2() never read it. When AlwaysRun is true,
the engine now bypasses ShouldExecute() so the check runs even for
trusted/admin users.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 4: Rename FileScanResultRecord → FileScanResultDto (Commit 2 — #276)

**Files:**
- Rename: `TelegramGroupsAdmin.Data/Models/FileScanResultRecord.cs` → `FileScanResultDto.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/ModelMappings.cs`
- Modify: `TelegramGroupsAdmin.Data/Migrations/AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Rename the file**

```bash
git mv TelegramGroupsAdmin.Data/Models/FileScanResultRecord.cs TelegramGroupsAdmin.Data/Models/FileScanResultDto.cs
```

- [ ] **Step 2: Rename the class in the new file**

In `TelegramGroupsAdmin.Data/Models/FileScanResultDto.cs`, change:

```csharp
// Before:
public class FileScanResultRecord
// After:
public class FileScanResultDto
```

- [ ] **Step 3: Update AppDbContext DbSet and model builder**

In `TelegramGroupsAdmin.Data/AppDbContext.cs`:

Line 83 — DbSet:
```csharp
// Before:
public DbSet<FileScanResultRecord> FileScanResults => Set<FileScanResultRecord>();
// After:
public DbSet<FileScanResultDto> FileScanResults => Set<FileScanResultDto>();
```

Lines 721-724 — model builder:
```csharp
// Before:
modelBuilder.Entity<FileScanResultRecord>()
    .HasIndex(fsr => fsr.FileHash);  // Primary cache lookup
modelBuilder.Entity<FileScanResultRecord>()
    .HasIndex(fsr => new { fsr.Scanner, fsr.ScannedAt });  // Analytics queries
// After:
modelBuilder.Entity<FileScanResultDto>()
    .HasIndex(fsr => fsr.FileHash);  // Primary cache lookup
modelBuilder.Entity<FileScanResultDto>()
    .HasIndex(fsr => new { fsr.Scanner, fsr.ScannedAt });  // Analytics queries
```

- [ ] **Step 4: Update ModelMappings**

In `TelegramGroupsAdmin.ContentDetection/Repositories/ModelMappings.cs`:

Line 36:
```csharp
// Before:
public static DomainModels.FileScanResultModel ToModel(this DataModels.FileScanResultRecord dto)
// After:
public static DomainModels.FileScanResultModel ToModel(this DataModels.FileScanResultDto dto)
```

Line 50:
```csharp
// Before:
public static DataModels.FileScanResultRecord ToDto(this DomainModels.FileScanResultModel model)
// After:
public static DataModels.FileScanResultDto ToDto(this DomainModels.FileScanResultModel model)
```

- [ ] **Step 5: Update AppDbContextModelSnapshot string reference**

In `TelegramGroupsAdmin.Data/Migrations/AppDbContextModelSnapshot.cs`, line 1761:

```csharp
// Before:
modelBuilder.Entity("TelegramGroupsAdmin.Data.Models.FileScanResultRecord", b =>
// After:
modelBuilder.Entity("TelegramGroupsAdmin.Data.Models.FileScanResultDto", b =>
```

Do NOT touch any migration `Designer.cs` files — those are frozen history.

- [ ] **Step 6: Build to verify all references resolved**

Run: `dotnet build --no-restore -v quiet`

Expected: Build succeeds with no errors. If any other files reference `FileScanResultRecord`, the build will fail and you'll know what else to update.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor: rename FileScanResultRecord to FileScanResultDto

Closes #276

FileScanResultRecord was a class (not a C# record) that stored
long-lived scan results but lacked the Dto suffix required by
TableDiscoveryService for backup discovery. The table name is
unchanged via [Table("file_scan_results")], so no migration needed.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 5: Create SqlHelper utility (Commit 3 — #191)

**Files:**
- Create: `TelegramGroupsAdmin.Core/Utilities/SqlHelper.cs`

- [ ] **Step 1: Create the shared utility**

```csharp
namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Safe SQL identifier quoting for dynamic DDL/DML where parameterization is not possible.
/// PostgreSQL does not support parameterizing identifiers (table/column names) — only data values.
/// </summary>
public static class SqlHelper
{
    /// <summary>
    /// Wraps a SQL identifier in double quotes with proper escaping.
    /// Embedded double quotes are escaped by doubling them per SQL standard.
    /// </summary>
    public static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build TelegramGroupsAdmin.Core --no-restore -v quiet`

Expected: Build succeeds.

---

## Task 6: Remove hardcoded credentials (Commit 3 — #191)

**Files:**
- Modify: `TelegramGroupsAdmin.Data/AppDbContextFactory.cs:17-18`

- [ ] **Step 1: Replace fallback with throw**

In `TelegramGroupsAdmin.Data/AppDbContextFactory.cs`, replace lines 15-18:

```csharp
// Before:
// Try environment variable first, fall back to dummy connection string for design-time operations
// Actual connection string comes from configuration at runtime
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? "Host=localhost;Database=telegram_groups_admin;Username=tgadmin;Password=changeme";

// After:
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings__DefaultConnection environment variable is required for EF Core " +
        "design-time operations (migrations). Set it before running dotnet ef commands.");
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build TelegramGroupsAdmin.Data --no-restore -v quiet`

Expected: Build succeeds.

---

## Task 7: Harden BackupService SQL (Commit 3 — #191)

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs:743,837,874`

- [ ] **Step 1: Add using directive**

Add to the top of `BackupService.cs`:

```csharp
using TelegramGroupsAdmin.Core.Utilities;
```

- [ ] **Step 2: Fix DROP CONSTRAINT (line 743)**

```csharp
// Before:
$"ALTER TABLE {tableName} DROP CONSTRAINT \"{fk.constraint_name}\""

// After:
$"ALTER TABLE {SqlHelper.QuoteIdentifier(tableName)} DROP CONSTRAINT {SqlHelper.QuoteIdentifier(fk.constraint_name)}"
```

- [ ] **Step 3: Fix ADD CONSTRAINT (line 837)**

```csharp
// Before:
$"ALTER TABLE {tableName} ADD CONSTRAINT \"{fk.constraint_name}\" {fk.constraint_def}"

// After:
$"ALTER TABLE {SqlHelper.QuoteIdentifier(tableName)} ADD CONSTRAINT {SqlHelper.QuoteIdentifier(fk.constraint_name)} {fk.constraint_def}"
```

Note: `fk.constraint_def` comes from `pg_get_constraintdef()` and is already valid SQL — do not quote it.

- [ ] **Step 4: Fix setval/SELECT MAX (line 874)**

```csharp
// Before:
var resetSql = $"SELECT setval('{sequenceName}', COALESCE((SELECT MAX({columnName}) FROM {tableName} WHERE {columnName} > 0), 1))";
var newSeqValue = await connection.ExecuteScalarAsync<long>(resetSql);

// After:
var resetSql = $"SELECT setval(@seqName::regclass, COALESCE((SELECT MAX({SqlHelper.QuoteIdentifier(columnName)}) FROM {SqlHelper.QuoteIdentifier(tableName)} WHERE {SqlHelper.QuoteIdentifier(columnName)} > 0), 1))";
var newSeqValue = await connection.ExecuteScalarAsync<long>(resetSql, new { seqName = sequenceName });
```

The sequence name is passed as a parameterized value (`@seqName`) cast to `regclass`, while column/table identifiers are quoted.

- [ ] **Step 5: Verify it builds**

Run: `dotnet build TelegramGroupsAdmin.BackgroundJobs --no-restore -v quiet`

Expected: Build succeeds.

---

## Task 8: Harden TableExportService SQL (Commit 3 — #191)

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableExportService.cs:64-74`

- [ ] **Step 1: Add using directive**

Add to the top of `TableExportService.cs`:

```csharp
using TelegramGroupsAdmin.Core.Utilities;
```

- [ ] **Step 2: Fix column list building (line 64-65)**

```csharp
// Before:
var columnList = string.Join(", ", columnMappings.Select(m =>
    m.IsJsonb ? $"{m.ColumnName}::text AS {m.ColumnName}" : m.ColumnName));

// After:
var columnList = string.Join(", ", columnMappings.Select(m =>
    m.IsJsonb
        ? $"{SqlHelper.QuoteIdentifier(m.ColumnName)}::text AS {SqlHelper.QuoteIdentifier(m.ColumnName)}"
        : SqlHelper.QuoteIdentifier(m.ColumnName)));
```

- [ ] **Step 3: Fix SELECT statement (line 74)**

```csharp
// Before:
var sql = $"SELECT {columnList} FROM {tableName} ORDER BY {sortColumn}";

// After:
var sql = $"SELECT {columnList} FROM {SqlHelper.QuoteIdentifier(tableName)} ORDER BY {SqlHelper.QuoteIdentifier(sortColumn)}";
```

`columnList` is already quoted per-column from step 2 — do not double-quote the whole string.

- [ ] **Step 4: Verify it builds**

Run: `dotnet build TelegramGroupsAdmin.BackgroundJobs --no-restore -v quiet`

Expected: Build succeeds.

---

## Task 9: Parameterize test helper SQL (Commit 3 — #191)

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs:174-176`
- Modify: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs:434-437`
- Modify: `TelegramGroupsAdmin.IntegrationTests/Migrations/SequenceIntegrityTests.cs:159-164`

- [ ] **Step 1: Fix MigrationTestHelper (line 174-176)**

```csharp
// Before:
await using var terminateCmd = new NpgsqlCommand(
    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}'",
    connection);

// After:
await using var terminateCmd = new NpgsqlCommand(
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1",
    connection);
terminateCmd.Parameters.Add(new NpgsqlParameter { Value = _databaseName });
```

- [ ] **Step 2: Fix TestWebApplicationFactory (lines 434-437)**

```csharp
// Before:
using var terminateCmd = connection.CreateCommand();
terminateCmd.CommandText = $@"
    SELECT pg_terminate_backend(pid)
    FROM pg_stat_activity
    WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid()";

// After:
using var terminateCmd = connection.CreateCommand();
terminateCmd.CommandText = @"
    SELECT pg_terminate_backend(pid)
    FROM pg_stat_activity
    WHERE datname = $1 AND pid <> pg_backend_pid()";
terminateCmd.Parameters.Add(new NpgsqlParameter { Value = _databaseName });
```

- [ ] **Step 3: Fix SequenceIntegrityTests (lines 159-164)**

Add a using at the top of the file:
```csharp
using TelegramGroupsAdmin.Core.Utilities;
```

Then fix the SQL:
```csharp
// Before:
var checkSequenceSql = $@"
    SELECT
        COALESCE(s.last_value, 0) as sequence_value,
        COALESCE((SELECT MAX(id) FROM {tableName}), 0) as max_id
    FROM pg_sequences s
    WHERE s.sequencename = '{seqName}'";

await using var connection = new Npgsql.NpgsqlConnection(helper.ConnectionString);
await connection.OpenAsync();
await using var cmd = new Npgsql.NpgsqlCommand(checkSequenceSql, connection);

// After:
var checkSequenceSql = $@"
    SELECT
        COALESCE(s.last_value, 0) as sequence_value,
        COALESCE((SELECT MAX(id) FROM {SqlHelper.QuoteIdentifier(tableName)}), 0) as max_id
    FROM pg_sequences s
    WHERE s.sequencename = $1";

await using var connection = new Npgsql.NpgsqlConnection(helper.ConnectionString);
await connection.OpenAsync();
await using var cmd = new Npgsql.NpgsqlCommand(checkSequenceSql, connection);
cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = seqName });
```

`tableName` is an identifier → `QuoteIdentifier()`. `seqName` is a data value compared against `pg_sequences` → parameterized `$1`.

- [ ] **Step 4: Build the full solution**

Run: `dotnet build --no-restore -v quiet`

Expected: Build succeeds with no errors.

---

## Task 10: Final verification and commit (Commit 3 — #191)

- [ ] **Step 1: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --no-restore -v quiet`

Expected: All tests PASS.

- [ ] **Step 2: Verify migrations still work**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`

Expected: Exits cleanly (validates migrations apply without error).

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Core/Utilities/SqlHelper.cs \
       TelegramGroupsAdmin.Data/AppDbContextFactory.cs \
       TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs \
       TelegramGroupsAdmin.BackgroundJobs/Services/Backup/Handlers/TableExportService.cs \
       TelegramGroupsAdmin.IntegrationTests/TestHelpers/MigrationTestHelper.cs \
       TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs \
       TelegramGroupsAdmin.IntegrationTests/Migrations/SequenceIntegrityTests.cs
git commit -F- <<'EOF'
security: remove hardcoded credentials and harden SQL construction

Closes #191

- Replace hardcoded fallback connection string in AppDbContextFactory
  with a descriptive InvalidOperationException
- Add SqlHelper.QuoteIdentifier() for safe PostgreSQL identifier quoting
- Apply identifier quoting to BackupService and TableExportService
  dynamic SQL (ALTER TABLE, setval, SELECT)
- Parameterize datname comparisons in test helpers (MigrationTestHelper,
  TestWebApplicationFactory) and sequence name lookups (SequenceIntegrityTests)

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 11: Create PR

- [ ] **Step 1: Push branch and create PR**

```bash
git push -u origin fix/priority-high-batch
gh pr create --base develop --title "fix: wire up AlwaysRun, rename FileScanResultDto, harden SQL" --body "$(cat <<'PREOF'
## Summary

Closes #369, #276, #191

Addresses all three open priority-high issues in separate commits:

- **fix: wire up AlwaysRun config flag** (#369) — `ShouldRunCheckV2()` now reads the `AlwaysRun` property from check configs and bypasses `ShouldExecute()` when true, so checks run even for trusted/admin users
- **refactor: rename FileScanResultRecord → FileScanResultDto** (#276) — class was invisible to the backup system's `EndsWith("Dto")` discovery filter. No migration needed (table name unchanged via `[Table]` attribute)
- **security: remove hardcoded credentials & harden SQL** (#191) — removes `Password=changeme` fallback from `AppDbContextFactory`, adds `SqlHelper.QuoteIdentifier()` for safe PostgreSQL identifier quoting, parameterizes data value comparisons in test helpers

## Test plan

- [ ] Unit tests for AlwaysRun: enabled+always_run bypasses ShouldExecute, disabled+always_run still skipped, enabled without always_run respects ShouldExecute
- [ ] Build succeeds after FileScanResultDto rename
- [ ] `dotnet run -- --migrate-only` succeeds (no accidental migration changes)
- [ ] Existing unit test suite passes

🤖 Generated with [Claude Code](https://claude.com/claude-code)
PREOF
)"
```
