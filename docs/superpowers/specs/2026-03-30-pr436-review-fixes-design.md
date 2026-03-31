# PR 436 Review Fixes — Streaming Restore + Misc Fixes

## Context

The `claude-review` GitHub Action reviewed PR 436 (perf: memory tuning phase 2) and identified 6 findings. After manual triage, 4 require action. Finding 2 (restore path) expanded in scope after discovering the restore path still buffers the entire 190 MB backup as `byte[]`, undermining the streaming work in this PR.

## Fixes

### Fix 1: BayesClassifier Long Token Guard

**File:** `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs`

**Problem:** `stackalloc char[256]` buffer is sliced with `lowerBuf[..wordSpan.Length]` without a length guard. The regex `\b[\w']+\b` can match tokens longer than 256 chars, causing `ArgumentOutOfRangeException`. Verified zero occurrences in current 24,754 messages (URLs break at non-`\w` chars), but spam with long encoded strings could trigger it.

**Fix:** After the existing numeric filter (line 124) and before `wordCount++`, add:

```csharp
if (wordSpan.Length > maxWordLength)
    continue;
```

No impact on classification — tokens > 256 chars have no Bayesian signal.

### Fix 2: Streaming Restore Path

**Files:**
- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs`
- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`
- `TelegramGroupsAdmin/Components/Shared/BackupRestore.razor`
- `TelegramGroupsAdmin/Components/Shared/RestoreBackupModal.razor`

**Problem:** Both `RestoreAsync` overloads accept `byte[]`, forcing the entire backup (~190 MB) into managed memory. The UI components call `File.ReadAllBytesAsync` (server restore) or `IBrowserFile.OpenReadStream() → MemoryStream.ToArray()` (upload restore). This is the remaining allocation hotspot the PR was supposed to eliminate.

#### Interface Changes

Replace the `byte[]` overloads with filepath-based overloads:

```csharp
// Remove:
Task RestoreAsync(byte[] backupBytes);
Task RestoreAsync(byte[] backupBytes, string passphrase);
Task<bool> IsEncryptedAsync(byte[] backupBytes);
Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes);

// Keep (already exist):
Task<bool> IsEncryptedAsync(string filepath);
Task<BackupMetadata> GetMetadataAsync(string filepath);

// Add:
Task RestoreAsync(string filepath);
Task RestoreAsync(string filepath, string passphrase);
```

#### Implementation: `RestoreAsync(string filepath)`

Opens `FileStream` → `GZipStream` → `TarReader`. Iterates entries:
- `metadata.json` → deserialize directly from entry stream
- `database.json.enc` → encrypted path (see passphrase resolution below)
- `database.json` → deserialize directly from entry stream (no decryption)
- `media/*` → stream to temp directory (existing behavior)

#### Implementation: `RestoreAsync(string filepath, string passphrase)`

Same as above but passes the explicit passphrase. Falls back to config if explicit fails.

#### Passphrase Resolution

When `database.json.enc` is encountered:

**Explicit passphrase path** (`RestoreAsync(filepath, passphrase)`):
1. Try explicit passphrase
2. If `CryptographicException` → try config passphrase via `_passphraseService.GetDecryptedPassphraseAsync()` (if available and different from explicit)
3. If that also fails or doesn't exist → throw with clear message

**No-passphrase path** (`RestoreAsync(filepath)`):
1. Try config passphrase via `_passphraseService.GetDecryptedPassphraseAsync()`
2. If no config passphrase available → throw: "Backup is encrypted but no passphrase is configured"
3. If wrong key → `CryptographicException` propagates

#### Encryption Detection

The tar entry name (`database.json.enc` vs `database.json`) determines whether decryption is needed — detected inline during the tar iteration loop. No separate pre-scan required.

`BackupEncryptionService.IsEncrypted(Stream)` detects the encryption *format* (TGAENC vs TGAEC2) and is used internally by `DecryptBackup(Stream, Stream, string)`. No changes needed to the encryption service.

#### Internal Changes

- `RestoreInternalAsync` signature changes from `(byte[], string?)` to `(string filepath, string?)` — opens the file stream itself
- Remove `TarContainsEncryptedDatabaseAsync(byte[])` — encryption detected inline during tar iteration
- Remove `GetMetadataInternalAsync(byte[])` if no callers remain
- Remove `IsEncryptedAsync(byte[])` — replaced by `IsEncryptedAsync(string)`

#### UI Changes

**`BackupRestore.razor` — "Restore from server" path:**
- Remove `File.ReadAllBytesAsync(backup.FilePath)` (line 586)
- Call `BackupService.RestoreAsync(backup.FilePath, passphrase)` directly
- Use `IsEncryptedAsync(backup.FilePath)` for encryption check (already exists)

**`BackupRestore.razor` — "Upload & restore" path:**
- Stream `IBrowserFile.OpenReadStream()` to a temp file instead of `MemoryStream.ToArray()`
- Call `BackupService.RestoreAsync(tempFilePath, passphrase)` on the temp file
- Clean up temp file in `finally` block
- Use `IsEncryptedAsync(tempFilePath)` and `GetMetadataAsync(tempFilePath)` on the temp file

**`RestoreBackupModal.razor`:**
- Same pattern: stream upload to temp file, use filepath-based APIs
- Remove `_backupBytes` field entirely

#### Memory Profile

| Path | Before | After |
|---|---|---|
| Restore from server | ~190 MB `byte[]` + decrypt buffers | FileStream → GZip → Tar streaming, ~2 MB decrypt buffer (TGAEC2) |
| Upload & restore | ~190 MB `byte[]` in managed memory | Temp file on disk + same streaming restore |
| Metadata/encryption check | `byte[]` round-trip | `string filepath` overloads (already exist) |

### Fix 3: Register.razor Stale Absolute URL

**File:** `TelegramGroupsAdmin/Components/Pages/Register.razor`

**Problem:** Line 214 constructs a full absolute URL from `Navigation.BaseUri` while `InternalApiClient.CreateClient()` already sets `BaseAddress` from `AppOptions.BaseUrl`. Leftover from pre-`AppOptions` dynamic URL code.

**Fix:** Remove line 214 (`var apiUrl = ...`) and change `PostAsJsonAsync(apiUrl, ...)` to `PostAsJsonAsync("/api/auth/register", ...)` — matching the pattern in `ResendVerification.razor`.

### Fix 4: Dead Branch in BackupEndpoints.cs Path Traversal

**File:** `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs`

**Problem:** `&& fullPath != resolvedDirectory` on line 42 is unreachable — `BackupFilenameRegex` guarantees a non-empty filename, so `Path.Combine` always produces a longer path.

**Fix:** Remove `&& fullPath != resolvedDirectory` from the condition. The `StartsWith` check alone provides complete path traversal protection.

## Out of Scope

- `RotateBackupPassphraseJob` — operates on individual backup files with `File.ReadAllBytes`. Smaller files, separate concern.
- `BackupEncryptionService` / `IBackupEncryptionService` — no changes. Stream APIs already correct.
- RateLimitService comment (finding 5) — skipped, code is correct as-is.
- Legacy decrypt buffer comment (finding 6) — addressed implicitly by finding 2 work.

## Test Impact

- Existing `BackupServiceTests` need updating for new `RestoreAsync` signatures
- `RestoreBackupModalTests` component tests may need signature updates
- No new test files expected — existing coverage adapts to new signatures
