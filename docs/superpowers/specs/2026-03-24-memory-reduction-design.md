# Memory Reduction: Backup Streaming + GC Tuning

## Problem

Production RSS is 2.4 GiB after 44 hours of uptime. Target is <1 GiB average for a homelab single-instance app.

Key findings from production analysis:
- LOH: 395 MiB with 35 MiB fragmented
- GC committed: 922 MiB vs 535 MiB in use (387 MiB headroom)
- Gen2 collections (108) > Gen0 (78) — LOH pressure triggering full GCs
- 64 GiB lifetime allocations in 44 hours
- No GC tuning configured (default Server GC with no conservation flags)

## Strategy

Two phases, both in a single PR:

1. **GC tuning** — Dockerfile env vars (as defaults, overridable via compose) to make Server GC + DATAS more memory-conservative
2. **Backup streaming** — refactor hourly backup to stream tar.gz directly to disk

## Phase 1: GC Tuning

Add environment variables to Dockerfile:

```dockerfile
ENV DOTNET_GCConserveMemory=7 \
    DOTNET_GCHighMemPercent=46
```

### DOTNET_GCConserveMemory=7

Scale of 0-9. At 7, the GC:
- Works harder to keep the heap small (more frequent, shorter collections)
- **Automatically compacts LOH** when fragmentation is too high (directly addresses the 395 MiB / 35 MiB fragmented LOH)
- Feeds into DATAS gen0 budget formula: `(20 - conserve_memory) / sqrt(app_size_MB)` — DATAS internally defaults to 5 for this formula, so setting 7 tightens the budget moderately (not from 0)

**CPU risk:** ConserveMemory=7 increases GC CPU overhead due to more frequent collections. Monitor CPU% after deployment — if it spikes, dial back to 5.

### DOTNET_GCHighMemPercent=70 (0x46)

Lowers the threshold where GC starts aggressive compaction from the default 90% to 70%. Causes GC to react earlier when memory pressure rises. Soft trigger, not a hard cap — the app can still grow if it genuinely needs memory during bursts.

### Why no HeapHardLimit

The app processes continuous Telegram messages, OpenAI API calls, and periodic backup/ML retraining simultaneously. A hard limit risks `OutOfMemoryException` during burst scenarios (spam wave + backup + retraining), which crashes the singleton process. The conservation flags nudge GC to be frugal without creating a ceiling.

### DATAS (already active)

`DOTNET_GCDynamicAdaptationMode=1` is enabled by default starting in .NET 9 (and therefore active in .NET 10). DATAS dynamically adjusts heap count from 1 (like Workstation) up to core count (like Server), adapting to workload. Combined with `ConserveMemory=7`, DATAS will use a tighter gen0 budget and compact more aggressively.

## Phase 2: Backup Streaming

### Current flow (per hourly backup)

```
ExportAsync() → byte[]
  DB → Dictionary → SerializeToUtf8Bytes(37MB byte[])     ← LOH
  → EncryptBackup(37MB byte[])                             ← LOH
  → MemoryStream for tar writer (grows to ~186MB)          ← LOH
  → tarStream.ToArray() (186MB byte[])                     ← LOH
  → File.WriteAllBytesAsync()
  → ValidateBackupAsync(byte[]) re-parses entire archive   ← LOH
Peak: ~460 MB of LOH allocations
```

### New flow

```
ExportToFileAsync(filepath)
  DB → Dictionary → SerializeToUtf8Bytes(37MB byte[])     ← LOH (kept, sunk cost)
  → EncryptBackup(37MB byte[])                             ← LOH (kept, AES-GCM requires full buffer)
  → TarWriter(GZipStream(FileStream(tempPath)))            ← streams to disk, no memory buffer
  → GIFs: WriteEntryAsync(filePath, entryName)             ← streams from disk
  → Validate via GetMetadataAsync(tempPath)                ← streams from file
  → File.Move(tempPath, filepath)                          ← atomic rename on success
  → finally: delete tempPath if it still exists            ← cleanup on failure
Peak: ~74 MB (JSON + encrypted copy, both short-lived)
```

### Atomic file write

The streaming approach means a failure midway leaves a partial file on disk. The in-memory approach either fully succeeded or failed cleanly. To preserve this guarantee:

- Write to a temp path in the same directory (e.g., `filepath + ".tmp"`)
- Validate the temp file (read metadata back)
- `File.Move(tempPath, filepath)` — atomic rename on same filesystem
- `finally` block deletes the temp file if it still exists (failure cleanup)

### Interface changes

`IBackupService`:

```csharp
// REMOVE:
Task<byte[]> ExportAsync();
Task<byte[]> ExportAsync(string passphraseOverride);

// ADD:
Task ExportToFileAsync(string filepath, CancellationToken cancellationToken = default);
Task ExportToFileAsync(string filepath, string passphraseOverride, CancellationToken cancellationToken = default);
```

Restore methods stay `byte[]`-based (infrequent, user-initiated).
`GetMetadataAsync(byte[])` stays for the restore UI (uploaded file already in memory).
`IsEncryptedAsync` — both overloads stay.

### CreateBackupWithRetentionAsync changes

```csharp
// Before:
var backupBytes = await ExportAsync();
await File.WriteAllBytesAsync(filepath, backupBytes, cancellationToken);
return new BackupResult(filename, filepath, backupBytes.Length, deletedCount);

// After:
await ExportToFileAsync(filepath, cancellationToken);
var fileInfo = new FileInfo(filepath);
return new BackupResult(filename, filepath, fileInfo.Length, deletedCount);
```

`BackupResult.SizeBytes` is already `long`. The change here is the source: `backupBytes.Length` (int, implicit widening) → `fileInfo.Length` (long, native).

### Validation change

Current: `ValidateBackupAsync(byte[])` re-parses the entire archive from memory.
New: Use existing `GetMetadataAsync(string filepath)` which streams from disk. Same checks (version, table count), zero memory.

### Test impact

Integration tests that call `ExportAsync()` switch to `ExportToFileAsync(tempFilePath)` then `File.ReadAllBytesAsync(tempFilePath)` for round-trip assertions. Same verification logic, slightly more setup.

CLI `--backup` in Program.cs switches to `ExportToFileAsync(filepath)` directly — actually simpler. Pass `CancellationToken.None` or wire up `app.Lifetime.ApplicationStopping`.

Unit tests for `ScheduledBackupJob` mock `IBackupService.CreateBackupWithRetentionAsync` and do not call `ExportAsync` directly — no changes needed.

Blazor UI (`BackupRestore.razor`) calls `CreateBackupWithRetentionAsync` which is refactored internally — no UI code changes needed.

### Encryption unchanged

`BackupEncryptionService` stays `byte[] → byte[]`. AES-256-GCM requires the full plaintext in memory. The 37MB JSON + 37MB encrypted are acceptable given the overall savings.

Future follow-ups could reduce the 74 MB further:
- `JsonSerializer.SerializeAsync` to a `MemoryStream` instead of `SerializeToUtf8Bytes` (drops peak to ~37 MB by avoiding the separate JSON byte array)
- Chunked encryption to eliminate the remaining buffer entirely

## Expected impact

| Metric | Before | After (estimated) |
|---|---|---|
| Backup peak memory | ~460 MB | ~74 MB |
| LOH size | 395 MiB | Significantly reduced (auto-compaction + less churn) |
| GC committed headroom | 387 MiB | Reduced by ConserveMemory pressure |
| Gen2 > Gen0 anomaly | Yes | Should normalize (less LOH-triggered full GCs) |
| Target RSS | 2.4 GiB | <1 GiB |

If these changes don't achieve <1 GiB, next steps would be:
- Investigate WTelegram native memory (1.5 GiB native gap)
- Profile Npgsql connection buffer pooling
- Consider Workstation GC as a more aggressive fallback
- Add chunked encryption to eliminate the remaining 74 MB

## Files to modify

- `TelegramGroupsAdmin/Dockerfile` — add GC env vars (defaults, overridable via compose)
- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupService.cs` — new interface methods
- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs` — streaming export, atomic write, updated retention method
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/ScheduledBackupJob.cs` — adapt to new interface
- `TelegramGroupsAdmin/Program.cs` — CLI backup path
- `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs` — file-based export

**No changes needed:**
- `BackupResult.cs` — `SizeBytes` is already `long`
- `ScheduledBackupJobTests.cs` — mocks `CreateBackupWithRetentionAsync`, doesn't touch `ExportAsync`
- `BackupRestore.razor` — calls `CreateBackupWithRetentionAsync` (refactored internally)
