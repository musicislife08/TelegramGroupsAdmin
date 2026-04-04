# PR 436 Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 4 findings from the claude-review action on PR 436 — including converting the restore path from byte[] to streaming filepath-based APIs to eliminate 190 MB allocations.

**Architecture:** Tasks 1, 5, and 6 are independent one-liner fixes. Tasks 2-4 are the streaming restore refactor: interface first, then implementation, then UI. Task 7 updates integration tests. Task 8 updates component test mocks.

**Tech Stack:** .NET 10, EF Core 10, Blazor Server, MudBlazor 9, RecyclableMemoryStream

---

### Task 1: BayesClassifier Long Token Guard

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs:124`

- [ ] **Step 1: Add length guard after numeric filter**

In `BayesClassifier.cs`, insert after the `int.TryParse` check (line 124) and before `wordCount++` (line 126):

```csharp
            // Filter: pure numeric tokens
            if (int.TryParse(wordSpan, out _))
                continue;

            // Filter: tokens exceeding stackalloc buffer — no Bayesian signal in 256+ char tokens
            if (wordSpan.Length > maxWordLength)
                continue;

            wordCount++;
```

- [ ] **Step 2: Verify build**

Run: `dotnet build TelegramGroupsAdmin.ContentDetection/TelegramGroupsAdmin.ContentDetection.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs
git commit -m "fix: guard BayesClassifier against tokens exceeding stackalloc buffer

Tokens longer than 256 chars (the stackalloc size) would cause
ArgumentOutOfRangeException on the lowerBuf slice. Skip them —
they carry no Bayesian classification signal."
```

---

### Task 2: Update IBackupService Interface — Remove byte[] APIs, Add filepath Restore

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs`

- [ ] **Step 1: Replace byte[] overloads with filepath-based restore**

Replace the full contents of `IBackupService.cs` with:

```csharp
namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Service for creating and restoring full system backups
/// </summary>
public interface IBackupService
{
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

    /// <summary>
    /// Restore system from backup file on disk (WIPES ALL DATA FIRST).
    /// Auto-detects encryption from tar entry names. Uses passphrase from DB config.
    /// </summary>
    /// <param name="filepath">Path to backup .tar.gz file</param>
    Task RestoreAsync(string filepath);

    /// <summary>
    /// Restore system from backup file on disk with explicit passphrase.
    /// Falls back to DB config passphrase if explicit passphrase fails decryption.
    /// </summary>
    /// <param name="filepath">Path to backup .tar.gz file</param>
    /// <param name="passphrase">Passphrase to try first for decryption</param>
    Task RestoreAsync(string filepath, string passphrase);

    /// <summary>
    /// Get backup metadata by streaming from disk without loading the entire file into memory.
    /// </summary>
    /// <param name="filepath">Path to the backup .tar.gz file</param>
    Task<BackupMetadata> GetMetadataAsync(string filepath);

    /// <summary>
    /// Check if backup file on disk contains an encrypted database by streaming only the tar entry names.
    /// Avoids loading the entire file into memory.
    /// </summary>
    /// <param name="filepath">Path to the backup .tar.gz file</param>
    /// <returns>True if encrypted, false if plain</returns>
    Task<bool> IsEncryptedAsync(string filepath);

    /// <summary>
    /// Create a backup and save to disk with retention cleanup
    /// Used by both scheduled backups and manual "Backup Now" button
    /// </summary>
    /// <param name="backupDirectory">Directory to save backup</param>
    /// <param name="retentionConfig">Retention policy configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with filename, path, size, and cleanup count</returns>
    Task<BackupResult> CreateBackupWithRetentionAsync(
        string backupDirectory,
        RetentionConfig retentionConfig,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify build fails (expected — callers still use old signatures)**

Run: `dotnet build TelegramGroupsAdmin.sln 2>&1 | head -50`
Expected: Compile errors in BackupService.cs, BackupRestore.razor, RestoreBackupModal.razor, and test projects referencing removed byte[] overloads.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs
git commit -m "refactor: replace byte[] backup APIs with filepath-based streaming

Remove RestoreAsync(byte[]), IsEncryptedAsync(byte[]),
GetMetadataAsync(byte[]) from IBackupService interface.
Add RestoreAsync(string filepath) overloads.
Callers updated in subsequent commits."
```

---

### Task 3: Implement Streaming RestoreAsync in BackupService

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`

- [ ] **Step 1: Replace RestoreAsync(byte[]) overloads and RestoreInternalAsync**

In `BackupService.cs`, find the two public `RestoreAsync` methods (starting around line 356) and `RestoreInternalAsync` (around line 408). Replace all three methods with:

```csharp
    public async Task RestoreAsync(string filepath)
    {
        await RestoreInternalAsync(filepath, passphrase: null);
    }

    public async Task RestoreAsync(string filepath, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        _logger.LogInformation("Restoring backup with explicit passphrase");
        await RestoreInternalAsync(filepath, passphrase);
    }

    private async Task RestoreInternalAsync(string filepath, string? passphrase)
    {
        _logger.LogWarning("Starting full system restore - THIS WILL WIPE ALL DATA");

        // Media files are streamed to a temp directory during tar extraction to avoid
        // buffering potentially hundreds of GIF files in memory. The temp directory is
        // moved to the final location only after the DB transaction commits successfully.
        var mediaTempDir = Directory.CreateTempSubdirectory(BackupConstants.MediaTempDirPrefix);
        var mediaFileCount = 0;

        try
        {
            // Stream directly from file — never load entire backup into memory
            await using var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);

            BackupMetadata? metadata = null;
            Dictionary<string, List<object>>? databaseData = null;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            // Read all tar entries — media files stream directly to temp disk
            while (await tarReader.GetNextEntryAsync() is { } entry)
            {
                if (entry.DataStream == null)
                    continue;

                if (entry.Name == "metadata.json")
                {
                    metadata = await JsonSerializer.DeserializeAsync<BackupMetadata>(entry.DataStream, jsonOptions);
                    _logger.LogDebug("Read metadata.json from tar stream");
                }
                else if (entry.Name == "database.json.enc")
                {
                    // Stream encrypted data into a pooled stream
                    using var encryptedMs = _streamManager.GetStream("BackupService.Restore.Encrypted");
                    await entry.DataStream.CopyToAsync(encryptedMs);

                    using var decryptedMs = _streamManager.GetStream("BackupService.Restore.Decrypted");
                    var resolvedPassphrase = await DecryptWithFallbackAsync(encryptedMs, decryptedMs, passphrase);

                    decryptedMs.Position = 0;
                    databaseData = await JsonSerializer.DeserializeAsync<Dictionary<string, List<object>>>(decryptedMs, jsonOptions);
                    _logger.LogInformation("Decrypted database with {Source}: {EncryptedSize} bytes → {DecryptedSize} bytes",
                        resolvedPassphrase == passphrase ? "explicit passphrase" : "config passphrase",
                        encryptedMs.Length, decryptedMs.Length);
                }
                else if (entry.Name == "database.json")
                {
                    // Legacy unencrypted backup — deserialize directly from tar entry stream
                    using var ms = _streamManager.GetStream("BackupService.Restore.Unencrypted");
                    await entry.DataStream.CopyToAsync(ms);
                    ms.Position = 0;
                    databaseData = await JsonSerializer.DeserializeAsync<Dictionary<string, List<object>>>(ms, jsonOptions);
                    _logger.LogInformation("Read unencrypted database: {Size} bytes", ms.Length);
                }
                else if (entry.Name.StartsWith("media/"))
                {
                    // Path.GetFullPath resolves "../" segments to prevent path traversal.
                    var targetPath = Path.GetFullPath(Path.Combine(mediaTempDir.FullName, entry.Name));
                    if (!targetPath.StartsWith(mediaTempDir.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Skipping suspicious tar entry path: {EntryPath}", entry.Name);
                        continue;
                    }

                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (targetDir != null && !Directory.Exists(targetDir))
                        Directory.CreateDirectory(targetDir);

                    // Stream directly to disk — no in-memory buffering
                    await using var fileOut = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                    await entry.DataStream.CopyToAsync(fileOut);
                    mediaFileCount++;
                    _logger.LogDebug("Streamed media file to temp: {Name}", entry.Name);
                }
            }
```

The rest of `RestoreInternalAsync` (from `if (metadata == null)` through the end) remains unchanged.

- [ ] **Step 2: Add DecryptWithFallbackAsync helper**

Add this private method to `BackupService`, after `RestoreInternalAsync`:

```csharp
    /// <summary>
    /// Attempts decryption with explicit passphrase first, falling back to DB config passphrase.
    /// Resets stream positions before each attempt.
    /// </summary>
    /// <returns>The passphrase that succeeded</returns>
    private async Task<string> DecryptWithFallbackAsync(Stream encryptedStream, Stream decryptedStream, string? explicitPassphrase)
    {
        // Try explicit passphrase first
        if (!string.IsNullOrWhiteSpace(explicitPassphrase))
        {
            try
            {
                encryptedStream.Position = 0;
                decryptedStream.Position = 0;
                decryptedStream.SetLength(0);
                _encryptionService.DecryptBackup(encryptedStream, decryptedStream, explicitPassphrase);
                return explicitPassphrase;
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Explicit passphrase failed, trying config passphrase");
            }
        }

        // Fall back to config passphrase
        string configPassphrase;
        try
        {
            configPassphrase = await _passphraseService.GetDecryptedPassphraseAsync();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Backup is encrypted but no passphrase is available. " +
                "Configure a passphrase in Settings → Backup & Restore, or provide one explicitly.");
        }

        // Don't retry with the same passphrase
        if (configPassphrase == explicitPassphrase)
        {
            throw new CryptographicException(
                "Passphrase is incorrect. The backup was encrypted with a different passphrase.");
        }

        encryptedStream.Position = 0;
        decryptedStream.Position = 0;
        decryptedStream.SetLength(0);
        _encryptionService.DecryptBackup(encryptedStream, decryptedStream, configPassphrase);
        return configPassphrase;
    }
```

- [ ] **Step 4: Remove dead byte[] methods**

Remove these methods from `BackupService.cs`:
- `TarContainsEncryptedDatabaseAsync(byte[] backupBytes)` (around line 391)
- `GetMetadataInternalAsync(byte[] backupBytes)` if it exists (called by `GetMetadataAsync(byte[])`)
- `GetMetadataAsync(byte[] backupBytes)` (around line 893)
- `IsEncryptedAsync(byte[] backupBytes)` (around line 928)

Keep: `GetMetadataAsync(string filepath)` and `IsEncryptedAsync(string filepath)` — they already exist and stream from disk.

- [ ] **Step 5: Add using for CryptographicException if not present**

Check the top of `BackupService.cs` for `using System.Security.Cryptography;`. Add it if missing (needed for `CryptographicException` in `DecryptWithFallbackAsync`).

- [ ] **Step 6: Verify build**

Run: `dotnet build TelegramGroupsAdmin.BackgroundJobs/TelegramGroupsAdmin.BackgroundJobs.csproj`
Expected: Build succeeded. (UI projects and tests will still fail — they reference old APIs.)

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs
git commit -m "refactor: streaming restore from filepath instead of byte[]

RestoreInternalAsync now opens FileStream directly — never loads the
full 190 MB backup into managed memory. Passphrase resolution uses
explicit-first with config fallback. Removes dead byte[] helper methods."
```

---

### Task 4: Update UI Components — Stream Uploads to Temp Files

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/BackupRestore.razor`
- Modify: `TelegramGroupsAdmin/Components/Shared/RestoreBackupModal.razor`

- [ ] **Step 1: Update BackupRestore.razor — remove _backupBytes field and byte[] usage**

In `BackupRestore.razor`, find the `@code` section. Make these changes:

Replace the `_backupBytes` field declaration (around line 289):
```csharp
    private byte[]? _backupBytes;
```
with:
```csharp
    private string? _uploadTempFilePath;
```

Replace the upload file handler method (the one that reads `IBrowserFile` and calls `ms.ToArray()`). Find the method that handles `OnFilesChanged` or similar for the upload, and change it from buffering to temp-file streaming. The method currently does:
```csharp
await using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 500);
using var ms = new MemoryStream();
await stream.CopyToAsync(ms);
_backupBytes = ms.ToArray();
```

Replace with:
```csharp
// Stream upload to temp file — never buffer entire backup in managed memory
CleanupTempFile();
_uploadTempFilePath = Path.Combine(Path.GetTempPath(), $"tga_upload_{Guid.NewGuid():N}.tar.gz");
await using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 500);
await using var tempFile = new FileStream(_uploadTempFilePath, FileMode.Create, FileAccess.Write);
await stream.CopyToAsync(tempFile);
```

Replace all `IsEncryptedAsync(_backupBytes)` calls with `IsEncryptedAsync(_uploadTempFilePath)`.

Replace all `GetMetadataAsync(_backupBytes)` calls with `GetMetadataAsync(_uploadTempFilePath)`.

In the `RestoreFromUpload` method, replace:
```csharp
await PerformRestoreAsync(_backupBytes, passphrase);
```
with:
```csharp
await PerformRestoreAsync(_uploadTempFilePath!, passphrase);
```

In the "Restore from server" method (`RestoreFromBrowser` or similar), replace:
```csharp
var bytes = await File.ReadAllBytesAsync(backup.FilePath);
// ... IsEncryptedAsync(bytes) ...
await PerformRestoreAsync(bytes, passphrase);
```
with:
```csharp
var isEncrypted = await BackupService.IsEncryptedAsync(backup.FilePath);
// ... passphrase prompt logic stays the same ...
await PerformRestoreAsync(backup.FilePath, passphrase);
```

Update `PerformRestoreAsync` signature from `(byte[] backupBytes, string? passphrase)` to `(string filepath, string? passphrase)`:
```csharp
    private async Task PerformRestoreAsync(string filepath, string? passphrase)
    {
        _isRestoring = true;
        StateHasChanged();

        try
        {
            if (passphrase != null)
            {
                await BackupService.RestoreAsync(filepath, passphrase);
            }
            else
            {
                await BackupService.RestoreAsync(filepath);
            }

            Snackbar.Add("System restored successfully! You will be logged out.", Severity.Success);
            await Task.Delay(2000);
            Navigation.NavigateTo("/", forceLoad: true);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Restore failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isRestoring = false;
            StateHasChanged();
        }
    }
```

Replace all remaining `_backupBytes != null` checks with `_uploadTempFilePath != null`.

Replace all `_backupBytes = null` assignments with a call to `CleanupTempFile()`.

Add cleanup helper and `IDisposable`:
```csharp
    private void CleanupTempFile()
    {
        if (_uploadTempFilePath != null && File.Exists(_uploadTempFilePath))
        {
            try { File.Delete(_uploadTempFilePath); }
            catch { /* best effort */ }
        }
        _uploadTempFilePath = null;
    }

    public void Dispose()
    {
        CleanupTempFile();
    }
```

Add `@implements IDisposable` at the top of the file if not already present.

- [ ] **Step 2: Update RestoreBackupModal.razor — same temp-file pattern**

In `RestoreBackupModal.razor`, apply the same pattern:

Replace field:
```csharp
    private byte[]? _backupBytes;
```
with:
```csharp
    private string? _uploadTempFilePath;
```

Replace upload buffering with temp-file streaming (same pattern as Step 1).

Replace `IsEncryptedAsync(_backupBytes)` → `IsEncryptedAsync(_uploadTempFilePath)`.

Replace `GetMetadataAsync(_backupBytes)` → `GetMetadataAsync(_uploadTempFilePath)`.

Replace `RestoreAsync(_backupBytes, _passphrase)` → `RestoreAsync(_uploadTempFilePath!, _passphrase)`.

Replace `RestoreAsync(_backupBytes)` → `RestoreAsync(_uploadTempFilePath!)`.

Replace all `_backupBytes = null` with `CleanupTempFile()`.

Replace all `_backupBytes != null` with `_uploadTempFilePath != null`.

Add the same `CleanupTempFile()` method and `@implements IDisposable`.

- [ ] **Step 3: Verify build of web project**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded. (Test projects may still fail.)

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/BackupRestore.razor TelegramGroupsAdmin/Components/Shared/RestoreBackupModal.razor
git commit -m "refactor: stream backup uploads to temp files instead of byte[]

Both BackupRestore and RestoreBackupModal now stream IBrowserFile
uploads to temp files on disk. Server restores pass filepath directly.
Eliminates 190 MB managed memory allocation during restore."
```

---

### Task 5: Register.razor — Remove Stale Absolute URL

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/Register.razor:214-215`

- [ ] **Step 1: Replace absolute URL with relative path**

In `Register.razor`, replace lines 214-215:
```csharp
            var apiUrl = new Uri(Navigation.BaseUri).GetLeftPart(UriPartial.Authority) + "/api/auth/register";
            var response = await InternalApiClient.CreateClient().PostAsJsonAsync(apiUrl, new
```

with:
```csharp
            var response = await InternalApiClient.CreateClient().PostAsJsonAsync("/api/auth/register", new
```

- [ ] **Step 2: Verify build**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Components/Pages/Register.razor
git commit -m "fix: use relative URL for registration API call

Remove stale absolute URL construction from Navigation.BaseUri.
InternalApiClient.CreateClient() already sets BaseAddress from
AppOptions.BaseUrl — use relative path like ResendVerification does."
```

---

### Task 6: BackupEndpoints.cs — Remove Dead Branch

**Files:**
- Modify: `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs:41-42`

- [ ] **Step 1: Remove unreachable condition**

In `BackupEndpoints.cs`, replace lines 41-42:
```csharp
            if (!fullPath.StartsWith(resolvedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && fullPath != resolvedDirectory)
```

with:
```csharp
            if (!fullPath.StartsWith(resolvedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
```

- [ ] **Step 2: Verify build**

Run: `dotnet build TelegramGroupsAdmin/TelegramGroupsAdmin.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs
git commit -m "fix: remove unreachable branch in backup download path traversal check

BackupFilenameRegex guarantees a non-empty filename, so
Path.Combine always produces a path longer than the directory.
The fullPath == resolvedDirectory branch was dead code."
```

---

### Task 7: Update Integration Tests for Filepath-Based APIs

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs`

- [ ] **Step 1: Change ExportBackupAsBytesAsync to ExportBackupToTempFileAsync**

Replace the helper method (around line 142):

```csharp
    /// <summary>
    /// Helper to export backup to temp file and return the filepath for streaming tests.
    /// Caller is responsible for cleanup.
    /// </summary>
    private async Task<string> ExportBackupToTempFileAsync(string? passphraseOverride = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_backup_{Guid.NewGuid():N}.tar.gz");
        if (passphraseOverride != null)
        {
            await _backupService!.ExportToFileAsync(tempPath, passphraseOverride, CancellationToken.None);
        }
        else
        {
            await _backupService!.ExportToFileAsync(tempPath, CancellationToken.None);
        }
        return tempPath;
    }
```

- [ ] **Step 2: Update all test methods to use filepath APIs**

Every test that called `ExportBackupAsBytesAsync()` and then `RestoreAsync(bytes)` / `GetMetadataAsync(bytes)` / `IsEncryptedAsync(bytes)` needs updating. The pattern for each test is:

**Before:**
```csharp
var backupBytes = await ExportBackupAsBytesAsync();
await _backupService!.RestoreAsync(backupBytes);
```

**After:**
```csharp
var backupPath = await ExportBackupToTempFileAsync();
try
{
    await _backupService!.RestoreAsync(backupPath);
}
finally
{
    File.Delete(backupPath);
}
```

Apply this pattern to every test method that uses `ExportBackupAsBytesAsync`. Key methods to update:

- `ExportToFileAsync_WithDbPassphrase_ShouldCreateEncryptedBackup` — use filepath for `IsEncryptedAsync(path)` and `GetMetadataAsync(path)`
- `ExportToFileAsync_WithExplicitPassphrase_ShouldOverrideDbPassphrase` — use `IsEncryptedAsync(path)` and `RestoreAsync(path, passphrase)`
- `ExportToFileAsync_ShouldIncludeAllExpectedTables` — use `GetMetadataAsync(path)`
- `ExportToFileAsync_ShouldDecryptDataProtectionFields` — just verify export succeeds
- `DiscoverTablesAsync_ShouldDiscoverAllMappedTables` — use `GetMetadataAsync(path)`
- `DiscoverTablesAsync_ShouldExcludeSystemTables` — verify export bytes exist via `File.Exists`
- `RestoreAsync_EncryptedBackupWithPassphrase_ShouldRestoreSuccessfully` — use `RestoreAsync(path, passphrase)`
- `RestoreAsync_WithDbPassphrase_ShouldRestoreWithoutExplicitPassphrase` — use `RestoreAsync(path)`
- `RestoreAsync_WrongPassphrase_ShouldThrowException` — use `RestoreAsync(path, "wrong")`
- `RestoreAsync_ShouldWipeAllTablesFirst` — use `RestoreAsync(path)`
- `RestoreAsync_ShouldHandleSelfReferencingForeignKeys` — use `RestoreAsync(path)`
- `RestoreAsync_ShouldReencryptDataProtectionFields` — use `RestoreAsync(path)`
- `RestoreAsync_ShouldResetSequences` — use `RestoreAsync(path)`
- `RestoreAsync_ShouldRespectForeignKeyOrder` — use `RestoreAsync(path)`
- `GetMetadataAsync_FromEncryptedBackup_ShouldReturnMetadata` — use `GetMetadataAsync(path)`
- `GetMetadataAsync_FromDbPassphraseBackup_ShouldReturnMetadata` — use `GetMetadataAsync(path)`
- `IsEncryptedAsync_WithEncryptedBackup_ShouldReturnTrue` — use `IsEncryptedAsync(path)`
- Any remaining tests using the old byte[] pattern

For tests that assert on `backupBytes.Length`, replace with `new FileInfo(backupPath).Length`.

- [ ] **Step 3: Run integration tests**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests/ --filter "FullyQualifiedName~BackupServiceTests" -v normal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs
git commit -m "test: update BackupServiceTests for filepath-based restore APIs

Replace ExportBackupAsBytesAsync with ExportBackupToTempFileAsync.
All tests use filepath-based RestoreAsync, GetMetadataAsync,
IsEncryptedAsync instead of byte[] overloads."
```

---

### Task 8: Update Component Test Mocks

**Files:**
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/RestoreBackupModalTests.cs`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/BackupRestoreTests.cs`

- [ ] **Step 1: Update mock setups for new IBackupService signatures**

In both test files, find any mock setups like:
```csharp
BackupService.RestoreAsync(Arg.Any<byte[]>()).Returns(Task.CompletedTask);
BackupService.RestoreAsync(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(Task.CompletedTask);
BackupService.IsEncryptedAsync(Arg.Any<byte[]>()).Returns(true);
BackupService.GetMetadataAsync(Arg.Any<byte[]>()).Returns(someMetadata);
```

Replace with:
```csharp
BackupService.RestoreAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
BackupService.RestoreAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
BackupService.IsEncryptedAsync(Arg.Any<string>()).Returns(true);
BackupService.GetMetadataAsync(Arg.Any<string>()).Returns(someMetadata);
```

Note: since `string filepath` replaced `byte[]`, and the filepath restore also uses `string passphrase`, the two-arg `RestoreAsync` mock needs `Arg.Any<string>(), Arg.Any<string>()`.

- [ ] **Step 2: Run component tests**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests/ -v normal`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.ComponentTests/Components/RestoreBackupModalTests.cs TelegramGroupsAdmin.ComponentTests/Components/BackupRestoreTests.cs
git commit -m "test: update component test mocks for filepath-based backup APIs"
```

---

### Task 9: Full Build and Test Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 2: Run all tests**

Run: `dotnet test TelegramGroupsAdmin.sln -v normal --no-build 2>&1 | tee /tmp/test-results.txt`
Expected: All tests pass.

- [ ] **Step 3: Verify migration check**

Run: `dotnet run --project TelegramGroupsAdmin/TelegramGroupsAdmin.csproj -- --migrate-only`
Expected: Clean startup, migrations applied, exits.
