# Memory Tuning Phase 2 — Design Spec

**Date:** 2026-03-28
**Parent Issue:** #423
**Branch:** `perf/memory-tuning-phase2`
**PR Target:** `develop`

## Context

An 18-hour Prometheus/Grafana investigation (2026-03-27) confirmed no memory leak — RSS oscillates 810-1272 MiB in a sawtooth driven by concurrent heavy operations. Phase 1 (#421, #422) added observability. Phase 2 addresses root causes.

Backup analysis (2026-03-28) revealed the database JSON portion is ~250 MB uncompressed (not 38 MB as originally estimated), making peak in-memory pressure ~500 MB during backup encryption. This makes streaming and pool-based allocation changes critical.

## Scope

All 12 sub-issues (#424-#435), 1 inline Tier 3 fix (stagger retrains), and a backup download streaming fix discovered during analysis. Single PR with separate commits per issue.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Tier 4 inclusion | Include all tiers | Natural phasing via commit order; dependencies respected |
| Backup format migration | Write new, read both | Don't break existing backups; tracking issue to remove legacy path later |
| RecyclableMemoryStream pool config | Hardcoded defaults, 512 MB max stream, pool self-sizes | Pool is inherently dynamic; env var knobs are premature |
| LOH compaction after retrain | Skip | RecyclableMemoryStream (#425) eliminates the LOH pressure that makes compaction necessary |
| Stagger retrains | Delay only, no schedule overlap detection | Admins override schedules via config; log warning about offsetting jobs |
| Verification | Full `dotnet run` + SIGTERM | `--migrate-only` misses DI and middleware failures; bot disabled locally |

## NuGet Packages

**Directory.Packages.props additions:**
- `Microsoft.IO.RecyclableMemoryStream` — pool-based stream allocation for #425, #426

No other new packages. `FrozenDictionary` is inbox (System.Collections.Frozen), `LoggerMessage` is a source generator in Microsoft.Extensions.Logging, `IMemoryCache` is in Microsoft.Extensions.Caching.Memory.

## DI Registration (ServiceCollectionExtensions.cs)

- Singleton `RecyclableMemoryStreamManager` (consumed by BackupService, ProfileScanService, TelegramImageService)
- Replace raw `AddScoped<HttpClient>` (lines 58-68) with named client via `AddHttpClient` + `IHttpClientFactory`
- Add `services.AddMemoryCache()` if not already present

## Wave 1 — Independent Fixes

### #425 — RecyclableMemoryStream for Backup Pipeline + Streaming + Download Fix

**BackupService:**
- Replace `JsonSerializer.SerializeToUtf8Bytes()` with `JsonSerializer.SerializeAsync()` to `RecyclableMemoryStream`
- Update `EncryptBackup` to accept `Stream` input instead of `byte[]`
- Null `backup.Data` after serialization to release object graph early
- Streams disposed immediately after writing to tar — no coexisting 250 MB buffers

**Download endpoint:**
- Stream the tar.gz file directly to HTTP response with chunked transfer encoding
- Eliminates the 261 MB JSON deserialization error on download

**Encryption interface:**
- `EncryptBackup` accepts `Stream` input instead of `byte[]` (preparation for #434) but still reads the full stream into a buffer internally for single-shot AES-GCM — the peak memory improvement in Wave 1 comes from streaming JSON serialization and early null of `backup.Data`, NOT from streaming encryption. The full 500 MB → 2 MB improvement requires #434 (Wave 3)

### #427 — MLContext Elimination + ITransformer Disposal

- Remove redundant `new MLContext()` in `SaveModelAsync` (line 170) and `LoadModelAsync` (line 237)
- Store training MLContext as a field, protected by existing `_retrainingSemaphore`
- Dispose old `ITransformer` on model swap before `Interlocked.Exchange` of `_currentModel` — `ITransformer` does not extend `IDisposable`, so use conditional cast: `(oldContainer?.Model as IDisposable)?.Dispose()`

### #428 — LoggerMessage Source Generator

- Add `[LoggerMessage]` partial method definitions for hot-path log calls in `MessageProcessingService` and other detection pipeline services
- Generated methods check `IsEnabled()` before evaluating arguments — compatible with dynamic UI log level toggle
- `.ToLogDebug()` extension methods remain but only called when level is enabled

### #429 — IHttpClientFactory

- Replace `AddScoped<HttpClient>` lambda (lines 58-68) with named client `AddHttpClient("BlazorServer", ...)`
- Inject `IHttpClientFactory` at usage sites, call `CreateClient("BlazorServer")`
- Dynamic `BaseAddress` from `HttpContext` set via `DelegatingHandler` or at call time

### #430 — IMemoryCache for Auth Services

- `IntermediateAuthService`: replace `ConcurrentDictionary<string, TokenData>` with `IMemoryCache` + `AbsoluteExpiration`
- `PendingRecoveryCodesService`: same pattern, cache handles eviction
- `RateLimitService`: same pattern, sliding expiration for attempt windows
- Eliminates all fire-and-forget cleanup code
- **MemoryMetrics compatibility:** Each service exposes an `EntryCount` property consumed by `MemoryMetrics.cs` for Prometheus gauges. `IMemoryCache` has no count API, so maintain an `Interlocked` counter (increment on set, decrement on eviction callback) alongside the cache to preserve gauge accuracy

### #431 — EF Core 10 LeftJoin + AsNoTracking

- Replace `GroupJoin/SelectMany/DefaultIfEmpty` patterns with `LeftJoin` across 7 repositories:
  - MLTrainingDataRepository (4 patterns)
  - DetectionResultsRepository (4 chained joins)
  - StopWordsRepository
  - MessageHistoryRepository
  - InviteRepository
  - AnalyticsRepository
  - TelegramUserRepository
- **Query syntax caveat:** `LeftJoin` is method-syntax only. Queries currently in query syntax (e.g., MLTrainingDataRepository implicit ham, MessageHistoryRepository) must be converted to method syntax. Correlated subquery patterns (e.g., `from mt in context.MessageTranslations.Where(mt => mt.MessageId == m.MessageId)`) may not convert directly to `LeftJoin` since it requires independent key selectors — these will use the standard method-syntax `GroupJoin`/`SelectMany`/`DefaultIfEmpty` if `LeftJoin` cannot express the correlation.
- Fix missing `AsNoTracking()` on read-only queries

### #432 — VirusTotal Stream-Based JSON + Disposal

- Replace 5 `ReadAsStringAsync()` calls with `JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())`
- Wrap all `HttpResponseMessage` in `using` declarations
- Ensure error paths dispose responses

### #433 — Server-Side Take() on Implicit Ham Query

- Add `.Take(maxImplicitHam * 3)` before `.ToListAsync()` in MLTrainingDataRepository (~line 190)
- 3x over-fetch accounts for SimHash deduplication removal (~30-50%)

### Tier 3: Stagger Retrains

- Schedule the second retrain (Bayes) with a 5-minute `startAt` offset via the Quartz trigger rather than `Task.Delay` — avoids blocking the TrainingHandler for 5 minutes during the spam moderation flow
- Log warning at startup if backup and retrain schedules overlap within 10 minutes

## Wave 2 — Depends on Wave 1

### #424 — FrozenDictionary + Pre-Computed Aggregates for Bayes

**Depends on:** #428 (LoggerMessage covering hot paths)

- Convert `_spamWordCounts` / `_hamWordCounts` to `FrozenDictionary<string, int>` via `.ToFrozenDictionary()` at training time
- Pre-compute and cache as fields:
  - `_vocabularySize` (replaces `.Keys.Union().Count()`)
  - `_spamWordTotal` / `_hamWordTotal` (replaces `.Values.Sum()`)
  - `_laplaceDenominatorSpam` / `_laplaceDenominatorHam`
- `significantWords` list allocation stays (it's the return value)

### #426 — RecyclableMemoryStream for Image Processing

**Depends on:** #425 (pool singleton registered)

- Inject `RecyclableMemoryStreamManager` into ProfileScanService and TelegramImageService
- Replace `new MemoryStream()` with `manager.GetStream()` for photo downloads
- Eliminate `.ToArray()` — pass stream directly to `Image.Load(stream)`
- `ResizeForVisionAsync` signature: `byte[]` → `Stream` input

## Wave 3 — Breaking Changes

### #434 — Chunked AEAD Streaming Encryption

**Depends on:** #425 (RecyclableMemoryStream pipeline)

**Protocol:**
- 1 MB plaintext chunks, each encrypted independently with AES-GCM
- Per-chunk nonce: XOR a 64-bit big-endian chunk counter into the last 8 bytes of the 12-byte base nonce
- Final chunk sentinel (length = 0) for truncation detection
- File header: magic bytes + version byte + salt + base nonce

**Dual-read:**
- Detect format by magic header bytes → route to `DecryptChunked` or `DecryptLegacy`
- `EncryptBackup` always writes new chunked format
- Tracking issue opened to remove `DecryptLegacy` later

**Memory impact:** Peak drops from ~500 MB to ~2 MB (one read + one write chunk buffer)

### #435 — Span-Based Tokenizer with FrozenDictionary.AlternateLookup

**Depends on:** #424 (FrozenDictionary adopted)

- Replace `Regex.Matches()` with `Regex.EnumerateMatches()` — `ValueMatch` structs, no `Match` allocations
- Use `ReadOnlySpan<char>` slices from original message instead of new `string` per word
- `FrozenDictionary.GetAlternateLookup<ReadOnlySpan<char>>()` for zero-allocation dictionary lookups
- `ClassifyMessage` must remain synchronous (spans can't cross async boundaries) — confirmed synchronous from exploration

## Commit Plan

| # | Issue | Commit Message |
|---|-------|---------------|
| 1 | #425 | `perf: RecyclableMemoryStream for backup pipeline + streaming download` |
| 2 | #427 | `perf: eliminate redundant MLContext instances + dispose ITransformer` |
| 3 | #428 | `perf: LoggerMessage source generator for hot-path logging` |
| 4 | #429 | `fix: replace scoped HttpClient with IHttpClientFactory` |
| 5 | #430 | `perf: IMemoryCache for auth services` |
| 6 | #431 | `refactor: EF Core 10 LeftJoin adoption + AsNoTracking fix` |
| 7 | #432 | `perf: VirusTotal stream-based JSON + response disposal` |
| 8 | #433 | `perf: server-side Take() on implicit ham training query` |
| 9 | T3 | `perf: stagger concurrent retrains` |
| 10 | #424 | `perf: FrozenDictionary + pre-computed aggregates for Bayes` |
| 11 | #426 | `perf: RecyclableMemoryStream for image processing` |
| 12 | #434 | `perf: chunked AEAD streaming encryption for backups` |
| 13 | #435 | `perf: span-based tokenizer with FrozenDictionary.AlternateLookup` |

## Build & Test Strategy

- `dotnet build` after each commit
- `dotnet run` → wait for startup → SIGTERM after each commit (validates full DI + middleware)
- Full test suite after Wave 1 completes and after final commit
- Subagents do file edits only — no build/test in parallel

## Verification (Observable on Grafana)

| Metric | Current | Target |
|--------|---------|--------|
| RSS range | 810-1272 MiB | 800-1000 MiB |
| Native gap steady state | 380-508 MiB | 250-350 MiB |
| Native gap peak | 808 MiB | <500 MiB |
| LOH fragmentation | 38% | <15% |
| Backup peak memory (after Wave 1) | ~500 MB | ~300 MB (streaming serialization + early null, still single-shot AES-GCM) |
| Backup peak memory (after Wave 3) | ~300 MB | ~2 MB (chunked AEAD) |

## PR Structure

- Single PR to `develop`: `perf: memory tuning phase 2`
- PR body: `Closes #424, Closes #425, Closes #426, Closes #427, Closes #428, Closes #429, Closes #430, Closes #431, Closes #432, Closes #433, Closes #434, Closes #435`
- One new tracking issue: remove legacy decryption path from #434
