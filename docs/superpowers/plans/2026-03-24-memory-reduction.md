# Memory Reduction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce production RSS from 2.4 GiB to <1 GiB by adding GC tuning and streaming backups directly to disk.

**Architecture:** Two-phase approach in a single PR. Phase 1 adds GC conservation env vars to the Dockerfile. Phase 2 refactors `BackupService.ExportAsync` from returning `byte[]` to streaming a tar.gz archive directly to a `FileStream`, with atomic temp-file write for failure safety.

**Tech Stack:** .NET 10, AES-256-GCM, System.Formats.Tar, GZipStream, PostgreSQL

**Spec:** `docs/superpowers/specs/2026-03-24-memory-reduction-design.md`

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `TelegramGroupsAdmin/Dockerfile` | Modify | Add GC tuning env vars |
| `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs` | Modify | Replace `ExportAsync` with `ExportToFileAsync` |
| `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs` | Modify | Streaming export, atomic write, updated retention |
| `TelegramGroupsAdmin/Program.cs` | Modify | CLI `--backup` uses file-based export |
| `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs` | Modify | File-based export assertions |

**No changes needed:** `BackupResult.cs` (SizeBytes already `long`), `ScheduledBackupJob.cs` (only calls `CreateBackupWithRetentionAsync`, not `ExportAsync` — spec listed it but verified unnecessary), `ScheduledBackupJobTests.cs` (mocks `CreateBackupWithRetentionAsync`), `BackupRestore.razor` (calls `CreateBackupWithRetentionAsync` internally), `BackupEncryptionService.cs` (stays `byte[]` → `byte[]`).

---

### Task 1: GC Tuning — Dockerfile Environment Variables

**Files:**
- Modify: `TelegramGroupsAdmin/Dockerfile:241-248`

- [ ] **Step 1: Add GC conservation env vars to Dockerfile**

In the existing `ENV` block (line 243), add the GC tuning variables:

```dockerfile
# Set environment variables for production
# TZ can be overridden at runtime: -e TZ=America/Phoenix
# GC tuning: ConserveMemory=7 auto-compacts LOH, HighMemPercent=70% triggers earlier compaction
# These are defaults — override via compose.yml if CPU overhead is too high (dial ConserveMemory back to 5)
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=UTC \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_GCConserveMemory=7 \
    DOTNET_GCHighMemPercent=46
```

Note: `DOTNET_GCHighMemPercent=46` is hex for 70 decimal (env vars use hex per .NET convention).

- [ ] **Step 2: Verify Dockerfile builds**

Run: `docker build -t tga-test -f TelegramGroupsAdmin/Dockerfile . --no-cache --target final 2>&1 | tail -5`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Dockerfile
git commit -m "perf: add GC conservation env vars to reduce memory footprint

DOTNET_GCConserveMemory=7 enables automatic LOH compaction and tighter heap.
DOTNET_GCHighMemPercent=70 (0x46) triggers aggressive GC at lower memory threshold.
Both are defaults overridable via compose.yml."
```

---

### Task 2: Interface Change — Replace ExportAsync with ExportToFileAsync

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs`

- [ ] **Step 1: Update IBackupService interface**

Replace the two `ExportAsync` methods with `ExportToFileAsync`:

```csharp
// REMOVE these two methods:
// Task<byte[]> ExportAsync();
// Task<byte[]> ExportAsync(string passphraseOverride);

// ADD these two methods:

/// <summary>
/// Export all system data to a tar.gz file streamed directly to disk.
/// Uses passphrase from database config for encryption.
/// Writes to a temp file first, then atomically renames on success.
/// </summary>
/// <param name="filepath">Destination file path for the backup</param>
/// <param name="cancellationToken">Cancellation token</param>
Task ExportToFileAsync(string filepath, CancellationToken cancellationToken = default);

/// <summary>
/// Export all system data to a tar.gz file with explicit passphrase (for CLI usage).
/// Writes to a temp file first, then atomically renames on success.
/// </summary>
/// <param name="filepath">Destination file path for the backup</param>
/// <param name="passphraseOverride">Passphrase to use (overrides DB config)</param>
/// <param name="cancellationToken">Cancellation token</param>
Task ExportToFileAsync(string filepath, string passphraseOverride, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Verify build fails (callers still reference old methods)**

Run: `dotnet build --no-restore 2>&1 | grep "error CS" | head -10`
Expected: Compile errors in `BackupService.cs`, `Program.cs`, and `BackupServiceTests.cs` referencing `ExportAsync`

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs
git commit -m "refactor: replace ExportAsync byte[] with ExportToFileAsync streaming interface"
```

---

### Task 3: Streaming Export Implementation

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs:75-237`

- [ ] **Step 1: Replace the public ExportAsync methods**

Replace lines 75-90 (the two public `ExportAsync` overloads) with:

```csharp
public async Task ExportToFileAsync(string filepath, CancellationToken cancellationToken = default)
{
    await ExportToFileInternalAsync(filepath, cancellationToken: cancellationToken);
}

/// <summary>
/// Export backup with explicit passphrase override (for CLI usage)
/// </summary>
public async Task ExportToFileAsync(string filepath, string passphraseOverride, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(passphraseOverride))
        throw new ArgumentException("Passphrase cannot be empty", nameof(passphraseOverride));

    _logger.LogInformation("Starting backup export with explicit passphrase");
    await ExportToFileInternalAsync(filepath, passphraseOverride: passphraseOverride, cancellationToken: cancellationToken);
}
```

- [ ] **Step 2: Rewrite ExportInternalAsync to stream to FileStream**

Replace `ExportInternalAsync` (lines 96-237) with `ExportToFileInternalAsync`. The key changes:
- Accepts `string filepath` parameter
- Writes to `filepath + ".tmp"` temp path
- Tar writer targets `GZipStream(FileStream)` instead of `GZipStream(MemoryStream)`
- Validates by reading metadata from the temp file on disk
- Atomically renames temp → final on success
- `finally` block cleans up temp file on failure

```csharp
private async Task ExportToFileInternalAsync(
    string filepath,
    string? passphraseOverride = null,
    CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Starting full system backup export (tar.gz format, streaming to disk)");

    var backup = new SystemBackup
    {
        Metadata = new BackupMetadata
        {
            Version = CurrentVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            AppVersion = "1.0.0"
        }
    };

    await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

    // Discover all tables dynamically from database
    var allTables = await _tableDiscoveryService.DiscoverTablesAsync(connection);

    // Exclude repullable cached data (blocklist domains can be re-synced)
    var excludedTables = new HashSet<string> { "cached_blocked_domains" };
    var tables = allTables.Where(kvp => !excludedTables.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    _logger.LogInformation("Discovered {TableCount} tables to backup (excluded {ExcludedCount} repullable tables)",
        tables.Count, excludedTables.Count);

    backup.Metadata.Tables = tables.Keys.ToList();
    backup.Metadata.TableCount = tables.Count;

    // Export each table using reflection
    foreach (var (tableName, dtoType) in tables)
    {
        try
        {
            _logger.LogDebug("Exporting table: {TableName}", tableName);
            var records = await _tableExportService.ExportTableAsync(connection, tableName, dtoType);
            backup.Data[tableName] = records;
            _logger.LogDebug("Exported {Count} records from {TableName}", records.Count, tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export table {TableName}", tableName);

            // Notify Owners about backup failure
            _ = _notificationService.SendBackupFailedAsync(
                tableName: tableName,
                error: ex.Message,
                ct: CancellationToken.None);

            throw;
        }
    }

    // JSON serialization options (exclude [NotMapped] properties)
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = new NotMappedPropertiesIgnoringResolver()
    };

    // Serialize database content (kept as byte[] — AES-GCM requires full buffer)
    var databaseJson = JsonSerializer.SerializeToUtf8Bytes(backup.Data, jsonOptions);
    _logger.LogInformation("Serialized database to JSON: {Size} bytes", databaseJson.Length);

    // Determine passphrase: explicit override takes priority, then DB config
    string? passphrase = passphraseOverride;

    if (passphrase == null)
    {
        var encryptionConfig = await _configService.GetEncryptionConfigAsync();
        if (encryptionConfig?.Enabled != true)
        {
            throw new InvalidOperationException(
                "Backup encryption is not configured. Please set up encryption before creating backups. " +
                "Navigate to Settings → Backup & Restore to enable encryption.");
        }

        passphrase = await _passphraseService.GetDecryptedPassphraseAsync();
    }

    // Encrypt database content (AES-GCM requires full plaintext in memory)
    var databaseContent = _encryptionService.EncryptBackup(databaseJson, passphrase);
    _logger.LogInformation("Encrypted database: {OriginalSize} bytes → {EncryptedSize} bytes",
        databaseJson.Length, databaseContent.Length);

    // Count media files for metadata
    var banGifDir = Path.Combine(_mediaBasePath, "media", "ban-gifs");
    var gifFiles = Directory.Exists(banGifDir)
        ? Directory.GetFiles(banGifDir, "*.gif")
        : [];
    backup.Metadata.MediaFileCount = gifFiles.Length;

    // Serialize metadata (always unencrypted - readable by backup browser)
    var metadataJson = JsonSerializer.SerializeToUtf8Bytes(backup.Metadata, jsonOptions);

    // Stream tar.gz directly to disk via temp file for atomic write
    var tempPath = filepath + ".tmp";
    try
    {
        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var tarWriter = new TarWriter(gzipStream, leaveOpen: true))
        {
            // Add metadata.json (unencrypted - readable by backup browser without passphrase)
            var metadataEntry = new PaxTarEntry(TarEntryType.RegularFile, "metadata.json")
            {
                DataStream = new MemoryStream(metadataJson)
            };
            await tarWriter.WriteEntryAsync(metadataEntry, cancellationToken);
            _logger.LogDebug("Added metadata.json to archive: {Size} bytes", metadataJson.Length);

            // Add encrypted database entry
            var databaseEntry = new PaxTarEntry(TarEntryType.RegularFile, "database.json.enc")
            {
                DataStream = new MemoryStream(databaseContent)
            };
            await tarWriter.WriteEntryAsync(databaseEntry, cancellationToken);
            _logger.LogDebug("Added database.json.enc to archive: {Size} bytes", databaseContent.Length);

            // Add ban celebration GIFs (streamed from disk, not buffered)
            if (gifFiles.Length > 0)
            {
                foreach (var gifPath in gifFiles)
                {
                    var entryName = $"media/ban-gifs/{Path.GetFileName(gifPath)}";
                    await tarWriter.WriteEntryAsync(gifPath, entryName, cancellationToken);
                }
                _logger.LogInformation("Added {Count} ban celebration GIFs to archive", gifFiles.Length);
            }
        }

        var archiveSize = new FileInfo(tempPath).Length;
        _logger.LogInformation("Created tar.gz archive: {Size} bytes", archiveSize);

        // Validate by reading metadata back from the file on disk
        var metadata = await GetMetadataAsync(tempPath);
        if (string.IsNullOrEmpty(metadata.Version) || metadata.TableCount <= 0)
        {
            throw new InvalidOperationException("Backup validation failed - archive may be corrupted");
        }

        _logger.LogInformation("✅ Backup validated successfully");

        // Atomic rename: temp → final (same filesystem = atomic on Linux)
        File.Move(tempPath, filepath, overwrite: true);
    }
    finally
    {
        // Clean up temp file on failure
        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
```

- [ ] **Step 3: Update CreateBackupWithRetentionAsync**

Replace lines 1040-1051 in `CreateBackupWithRetentionAsync`:

```csharp
// Before:
// var backupBytes = await ExportAsync();
// await File.WriteAllBytesAsync(filepath, backupBytes, cancellationToken);

// After:
await ExportToFileAsync(filepath, cancellationToken);
```

And update the return statement (line 1090):

```csharp
// Before:
// return new BackupResult(filename, filepath, backupBytes.Length, deletedCount);

// After:
return new BackupResult(filename, filepath, new FileInfo(filepath).Length, deletedCount);
```

- [ ] **Step 4: Remove old ValidateBackupAsync(byte[]) private method**

The `ValidateBackupAsync(byte[])` method (lines 965-992) is no longer called. Delete it. Validation now happens inline via `GetMetadataAsync(string filepath)` in `ExportToFileInternalAsync`.

- [ ] **Step 5: Verify build compiles (except Program.cs and tests)**

Run: `dotnet build --no-restore TelegramGroupsAdmin.BackgroundJobs/TelegramGroupsAdmin.BackgroundJobs.csproj 2>&1 | tail -5`
Expected: Build succeeds for the BackgroundJobs project

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs
git commit -m "perf: stream backup tar.gz directly to FileStream instead of memory

Eliminates ~386 MB of LOH allocations per hourly backup:
- TarWriter writes to GZipStream(FileStream) instead of MemoryStream
- Removes tarStream.ToArray() copy (186 MB)
- Validates from file on disk instead of re-parsing byte[]
- Atomic temp file write with cleanup on failure

JSON serialization (37 MB) and AES-GCM encryption (37 MB) remain
as byte[] — AES-GCM requires full plaintext buffer."
```

---

### Task 4: CLI Backup Path Update

**Files:**
- Modify: `TelegramGroupsAdmin/Program.cs:234-239`

- [ ] **Step 1: Update --backup CLI handler**

Replace lines 234-239:

```csharp
// Before:
// app.Logger.LogInformation("Creating encrypted backup...");
// var backupBytes = await backupService.ExportAsync(passphrase);
// await File.WriteAllBytesAsync(backupPath, backupBytes);
//
// app.Logger.LogInformation("✅ Encrypted backup created: {Path} ({Size:F2} MB)",
//     backupPath, backupBytes.Length / 1024.0 / 1024.0);

// After:
app.Logger.LogInformation("Creating encrypted backup...");
await backupService.ExportToFileAsync(backupPath, passphrase, CancellationToken.None);

var backupSize = new FileInfo(backupPath).Length;
app.Logger.LogInformation("✅ Encrypted backup created: {Path} ({Size:F2} MB)",
    backupPath, backupSize / 1024.0 / 1024.0);
```

- [ ] **Step 2: Verify full solution builds**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeds (only test project may still have errors)

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Program.cs
git commit -m "refactor: CLI --backup uses streaming ExportToFileAsync"
```

---

### Task 5: Integration Test Updates

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs`

- [ ] **Step 1: Update export tests to use file-based API**

Each test that calls `ExportAsync()` needs to:
1. Create a temp file path
2. Call `ExportToFileAsync(tempPath)` instead
3. Read the file back with `File.ReadAllBytesAsync(tempPath)` where byte[] assertions are needed
4. Clean up the temp file

Example pattern for `ExportAsync_WithDbPassphrase_ShouldCreateEncryptedBackup`:

```csharp
[Test]
public async Task ExportToFileAsync_WithDbPassphrase_ShouldCreateEncryptedBackup()
{
    // Arrange
    var tempPath = Path.Combine(Path.GetTempPath(), $"test_backup_{Guid.NewGuid():N}.tar.gz");

    try
    {
        // Act
        await _backupService!.ExportToFileAsync(tempPath);

        // Assert
        Assert.That(File.Exists(tempPath), Is.True);
        Assert.That(new FileInfo(tempPath).Length, Is.GreaterThan(0));

        // Verify backup contains encrypted database entry (use file-based overload)
        var isEncrypted = await _backupService.IsEncryptedAsync(tempPath);
        Assert.That(isEncrypted, Is.True, "Backup should be encrypted when passphrase is configured");

        // Verify can extract metadata (use file-based overload)
        var metadata = await _backupService.GetMetadataAsync(tempPath);
        Assert.That(metadata, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata.Version, Is.EqualTo("3.0"));
            Assert.That(metadata.TableCount, Is.GreaterThan(0));
        }
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }
}
```

Apply the same pattern to **all 21 call sites** of `ExportAsync()` in this file. There are three categories:

**Export-only tests** (use file-based overloads for assertions):
- `ExportAsync_WithExplicitPassphrase_ShouldOverrideDbPassphrase` — use `ExportToFileAsync(tempPath, explicitPassphrase)`, use `IsEncryptedAsync(tempPath)` for assertion
- `ExportAsync_ShouldIncludeAllExpectedTables` — use file-based `GetMetadataAsync(tempPath)`
- `ExportAsync_ShouldDecryptDataProtectionFields` — just verify export succeeds without throwing
- `DiscoverTablesAsync_ShouldFindAllDatabaseTables` — same pattern
- `ExportAsync_WithCorruptedJsonbColumn_ShouldFailFastWithClearError` (line 539)
- `ExportAsync_WithValidJsonbColumn_ShouldSucceed` (line 558)

**Export-then-restore round-trip tests** (export to file, read back as `byte[]` for `RestoreAsync`):
- `RestoreAsync_EncryptedBackupWithPassphrase_ShouldRestoreSuccessfully` (line 277)
- `RestoreAsync_WithDbPassphrase_ShouldRestoreWithoutExplicitPassphrase` (line 307)
- `RestoreAsync_WrongPassphrase_ShouldThrowException` (line 324)
- `RestoreAsync_ShouldWipeAllTablesFirst` (line 338)
- `RestoreAsync_ShouldHandleSelfReferencingForeignKeys` (line 362)
- `RestoreAsync_ShouldReencryptDataProtectionFields` (line 382)
- `RestoreAsync_ShouldResetSequences` (line 405)
- `Restore_WithComplexDependencyGraph_ShouldSucceed` (line 441)
- `ExportAndRestore_ShouldPreserveDateTimeOffsetTimezone` (line 594)
- `ExportAndRestore_ShouldPreserveEnumValues` (line 620)

**Metadata/encryption check tests** (use file-based overloads):
- `GetMetadataAsync_FromEncryptedBackup_ShouldReturnMetadata` (line 468)
- `GetMetadataAsync_FromDbPassphraseBackup_ShouldReturnMetadata` (line 487)
- `IsEncryptedAsync_WithEncryptedBackup_ShouldReturnTrue` (line 501)

**Note:** Round-trip tests use `File.ReadAllBytesAsync(tempPath)` to get bytes for `RestoreAsync(byte[])`. This re-introduces memory allocation in tests, which is acceptable — the goal is production memory reduction, not test memory. Do NOT refactor `RestoreAsync` to be file-based (out of scope per spec).

- [ ] **Step 2: Verify tests compile**

Run: `dotnet build --no-restore TelegramGroupsAdmin.IntegrationTests/TelegramGroupsAdmin.IntegrationTests.csproj 2>&1 | tail -5`
Expected: Build succeeds

- [ ] **Step 3: Run integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BackupServiceTests" --no-build`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs
git commit -m "test: update backup integration tests for file-based ExportToFileAsync"
```

---

### Task 6: Full Build and Test Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build --no-restore`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test --no-build`
Expected: All tests pass

- [ ] **Step 3: Verify with --migrate-only**

Run: `dotnet run --project TelegramGroupsAdmin -- --migrate-only`
Expected: "Migration complete. Exiting (--migrate-only flag)."

- [ ] **Step 4: Final commit if any cleanup needed, then push**

```bash
git push origin perf/memory-reduction
```
