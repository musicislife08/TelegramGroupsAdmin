# Priority-High Batch PR Design

Closes #369, #276, #191

## Overview

Single PR to `develop` with three commits addressing all open priority-high issues. Each issue gets its own atomic commit.

**Branch:** `fix/priority-high-batch`

## Commit 1: Wire up AlwaysRun config flag (#369)

**Problem:** The `AlwaysRun` boolean on check configs is persisted and displayed in the UI but `ShouldRunCheckV2()` never reads it. 9 of 12 checks skip trusted/admin users in `ShouldExecute()`, making the AlwaysRun toggle inert.

**Fix:** In `ContentDetectionEngineV2.ShouldRunCheckV2()`, add a second switch expression after the `enabled` check that reads `AlwaysRun` from each check's config. When `AlwaysRun` is true, return true immediately (bypassing `ShouldExecute()`).

### Logic

```
if (!enabled) return false;
if (alwaysRun) return true;   // <-- new: bypass ShouldExecute
return check.ShouldExecute(request);
```

### Files

- `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs` — add `alwaysRun` switch + guard in `ShouldRunCheckV2()`

### Tests

- `AlwaysRun = true` bypasses `ShouldExecute()` for a trusted user
- `AlwaysRun = false` still respects `ShouldExecute()` behavior
- `Enabled = false` short-circuits regardless of `AlwaysRun`

## Commit 2: Rename FileScanResultRecord to FileScanResultDto (#276)

**Problem:** `FileScanResultRecord` is a `class` (not a C# `record`) that stores long-lived scan results. It lacks the `Dto` suffix required by `TableDiscoveryService` for backup discovery, making it invisible to the backup system.

**Fix:** Rename `FileScanResultRecord` to `FileScanResultDto` and rename the file to match. Update all references. No EF Core migration needed — the `[Table("file_scan_results")]` attribute controls the database table name.

**Out of scope:** `FileScanQuotaRecord` stays as-is (ephemeral data, does not need backup).

### Files

- `TelegramGroupsAdmin.Data/Models/FileScanResultRecord.cs` — rename class + file to `FileScanResultDto.cs`
- `TelegramGroupsAdmin.Data/AppDbContext.cs` — update DbSet declaration
- All repositories, mappings, and services that reference `FileScanResultRecord`
- All tests referencing the type

### Verification

After rename, confirm `TableDiscoveryService` would discover `FileScanResultDto` (class name ends in `Dto`).

## Commit 3: Remove hardcoded credentials and harden SQL (#191)

Three sub-changes:

### 3a. Remove hardcoded credentials

**Problem:** `AppDbContextFactory.cs` falls back to `Host=localhost;Database=telegram_groups_admin;Username=tgadmin;Password=changeme` when the environment variable is not set. Embeds credentials in source control.

**Fix:** Replace the `??` fallback with `?? throw new InvalidOperationException(...)` that instructs the developer to set the `ConnectionStrings__DefaultConnection` environment variable.

**File:** `TelegramGroupsAdmin.Data/AppDbContextFactory.cs`

### 3b. Identifier quoting for backup SQL

**Problem:** `BackupService.cs` and `TableExportService.cs` use string interpolation for table/column/constraint names in SQL. While inputs come from EF Core reflection (not user input), this lacks defense-in-depth.

**Fix:** Add a `QuoteIdentifier` helper that wraps identifiers in double quotes with proper escaping (`"` escaped as `""`). Apply to all interpolated identifiers in DDL and DML statements.

PostgreSQL does not support parameterizing identifiers (table/column names) — only data values. Identifier quoting is the correct defense for DDL.

```csharp
private static string QuoteIdentifier(string identifier)
    => $"\"{identifier.Replace("\"", "\"\"")}\"";
```

Place this as a `private static` method in `BackupService`. `TableExportService` and `SequenceIntegrityTests` each get their own copy (test code should not depend on production internals, and these are one-liners not worth extracting to a shared utility).

**Sites to fix:**
- `BackupService.cs:743` — `ALTER TABLE ... DROP CONSTRAINT` (tableName, constraint_name)
- `BackupService.cs:837` — `ALTER TABLE ... ADD CONSTRAINT` (tableName, constraint_name; constraint_def is already valid SQL from `pg_get_constraintdef()`)
- `BackupService.cs:874` — `setval` / `SELECT MAX` (sequenceName, columnName, tableName)
- `TableExportService.cs:74` — `SELECT ... FROM ... ORDER BY` (columnList individual columns, tableName, sortColumn)

### 3c. Parameterized queries in test helpers

**Problem:** Test helpers use string interpolation for `datname` comparisons against `pg_stat_activity` and `pg_sequences`.

**Fix:** Use Npgsql positional parameters (`$1`) for data value comparisons. Use `QuoteIdentifier()` for identifiers that cannot be parameterized.

**Sites to fix:**
- `MigrationTestHelper.cs:175` — `datname = '{_databaseName}'` to parameterized `$1`
- `TestWebApplicationFactory.cs:437` — `datname = '{_databaseName}'` to parameterized `$1`
- `SequenceIntegrityTests.cs:162,164` — `tableName` gets `QuoteIdentifier()`, `sequencename` comparison gets parameterized `$1`
