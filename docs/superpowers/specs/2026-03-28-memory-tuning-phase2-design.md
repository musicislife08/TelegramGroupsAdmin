# Memory Tuning Phase 2 — Design Spec

**Date:** 2026-03-28
**Parent Issue:** #423
**Branch:** `perf/memory-tuning-phase2`
**PR Target:** `develop`

## Context

An 18-hour Prometheus/Grafana investigation (2026-03-27) confirmed no memory leak — RSS oscillates 810-1272 MiB in a sawtooth driven by concurrent heavy operations. Phase 1 (#421, #422) added observability. Phase 2 addresses root causes.

Backup analysis (2026-03-28) revealed the database JSON portion is ~250 MB uncompressed (not 38 MB as originally estimated), making peak in-memory pressure ~500 MB during backup encryption. This makes streaming and pool-based allocation changes critical.

## Scope

All 12 sub-issues (#424-#435), 1 inline Tier 3 fix (merge classifier retraining jobs), and a backup download streaming fix discovered during analysis. Single PR with separate commits per issue.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Tier 4 inclusion | Include all tiers | Natural phasing via commit order; dependencies respected |
| Backup format migration | Write new, read both | Don't break existing backups; tracking issue to remove legacy path later |
| RecyclableMemoryStream pool config | Hardcoded defaults, 512 MB max stream, pool self-sizes | Pool is inherently dynamic; env var knobs are premature |
| LOH compaction after retrain | Skip | RecyclableMemoryStream (#425) eliminates the LOH pressure that makes compaction necessary |
| Retrain consolidation | Merge SDCA + Bayes into one job | Both load identical data; sequential by design eliminates concurrency concern |
| #430 scope | RateLimitService only; keep ConcurrentDictionary for token services | Atomic `TryRemove` is a security property for consume-once tokens; IMemoryCache cannot replicate it |
| #429 approach | Typed `InternalApiClient` wrapper + IHttpClientFactory | Preserves dynamic BaseAddress from HttpContext while gaining socket pooling |
| #425 encryption interface | Keep `byte[]` through Waves 1-2; full Stream refactor in Wave 3 only | Avoids two-phase interface churn; Wave 1 benefit comes from streaming serialization, not encryption |
| Download endpoint | Minimal API with multipart/range support | Blazor Server can't stream via HttpContext.Response from component event handlers |
| Verification | Full `dotnet run` + SIGTERM | `--migrate-only` misses DI and middleware failures; bot disabled locally |

## NuGet Packages

**Directory.Packages.props additions:**
- `Microsoft.IO.RecyclableMemoryStream` — pool-based stream allocation for #425, #426
- `Microsoft.Extensions.ML` — `PredictionEnginePool<T,T>` for thread-safe ML.NET prediction (#427)

No other new packages. `FrozenDictionary` is inbox (System.Collections.Frozen), `LoggerMessage` is a source generator in Microsoft.Extensions.Logging, `IMemoryCache` is in Microsoft.Extensions.Caching.Memory.

## DI Registration

**Main app ServiceCollectionExtensions.cs:**
- Singleton `RecyclableMemoryStreamManager` (consumed by BackupService, ProfileScanService, TelegramImageService — registered here since it's the composition root's extensions file, visible to all via DI)
- Replace raw `AddScoped<HttpClient>` (lines 58-68) with `AddHttpClient("Internal")` + scoped `InternalApiClient` typed wrapper
- `services.AddMemoryCache()` explicitly in `AddApplicationServices()` (co-located with auth singletons, even though `AddHybridCache` already calls it)

## Wave 1 — Independent Fixes

### #425 — RecyclableMemoryStream for Backup Pipeline + Streaming Download

**BackupService:**
- Replace `JsonSerializer.SerializeToUtf8Bytes()` with `JsonSerializer.SerializeAsync()` to `RecyclableMemoryStream`
- Call `.ToArray()` on the recyclable stream to pass to existing `EncryptBackup(byte[], string)` — pool-backed, avoids LOH fragmentation even though it produces a contiguous byte[]
- Null `backup.Data` after serialization to release object graph early
- Encryption interface stays `byte[]` in Wave 1 — the full Stream refactor happens in Wave 3 (#434)

**Download endpoint:**
- New minimal API endpoint: `GET /api/backup/download/{filename}`
- Stream tar.gz file with `Results.File()` and `enableRangeProcessing: true` for resume support on 190+ MB files
- **Security:** Filename validation (no `..` or path separators, must match `backup_YYYY-MM-DD_HH-mm-ss.tar.gz` pattern), path containment check (`Path.GetFullPath(...).StartsWith(backupDir)`), `[Authorize]` attribute
- Blazor component replaces JS interop with `NavigationManager.NavigateTo("/api/backup/download/{filename}", forceLoad: true)`

### #427 — MLContext Elimination + ITransformer Disposal + PredictionEngine Thread Safety

- Remove redundant `new MLContext()` in `SaveModelAsync` (line 170) — reuse the training context under existing `_retrainingSemaphore`
- Keep local `new MLContext()` in `LoadModelAsync` (line 237) — runs once at startup, avoids race with shared field since LoadModelAsync does not acquire the semaphore
- Dispose old `ITransformer` on model swap — `ITransformer` does not extend `IDisposable`, so use conditional cast: `(oldContainer?.Model as IDisposable)?.Dispose()`
- **Fix pre-existing concurrency bug:** Replace single `PredictionEngine<SpamTextFeatures, SpamPrediction>` with `PredictionEnginePool<T,T>` from `Microsoft.Extensions.ML` — `PredictionEngine` is documented as not thread-safe, but `Predict()` is called concurrently from message processing threads. The pool provides thread-safe prediction with model hot-swap support via `ModelReloadToken` (signal after each retrain for lazy engine refresh)

### #428 — LoggerMessage Source Generator

- Add `[LoggerMessage]` partial method definitions for hot-path log calls in `MessageProcessingService` and other detection pipeline services
- Generated methods check `IsEnabled()` before evaluating arguments — compatible with dynamic UI log level toggle
- `.ToLogDebug()` extension methods remain but only called when level is enabled
- **Honest estimate:** Primary benefit is correctness (guaranteed IsEnabled guard), not throughput — homelab message rate is modest

### #429 — IHttpClientFactory via Typed Client Wrapper

- Register named client: `services.AddHttpClient("Internal")` (no BaseAddress — set at call time)
- New scoped `InternalApiClient` class that injects `IHttpClientFactory` + `IHttpContextAccessor`
- `CreateClient()` method: gets client from factory, sets `BaseAddress` dynamically from current `HttpContext` (scheme + host), falls back to `localhost:5161` when `HttpContext` is null (background jobs)
- Preserves existing dynamic BaseAddress behavior (works behind reverse proxy in prod, localhost in dev) while gaining proper socket pooling via `IHttpClientFactory`
- Move any HTTP call logic out of Blazor components into services that inject `InternalApiClient`
- Remove the old `AddScoped<HttpClient>` lambda

### #430 — IMemoryCache for RateLimitService + Cleanup Timer for Token Services

**Scoped down from original — atomic token consumption is a security property:**

- **RateLimitService only:** Replace `ConcurrentDictionary<string, List<DateTimeOffset>>` with `IMemoryCache` + sliding expiration. Retain existing `Lock` for atomic check-count-then-increment sequence. `Interlocked` counter for `EntryCount` gauge with eviction callback (check `EvictionReason` to avoid double-decrement on explicit `Remove()`)
- **IntermediateAuthService:** Keep `ConcurrentDictionary` — `TryRemove` atomicity is required for one-time token consumption security. Replace fire-and-forget cleanup `Task.Run` with `IHostedService` background timer draining expired entries every 60 seconds
- **PendingRecoveryCodesService:** Same as IntermediateAuthService — keep `ConcurrentDictionary`, add `IHostedService` cleanup timer

### #431 — EF Core 10 LeftJoin Where Applicable + AsNoTracking Audit

- Replace `GroupJoin/SelectMany/DefaultIfEmpty` patterns with `LeftJoin` in repositories that actually have them:
  - MLTrainingDataRepository (convertible patterns only — correlated subqueries stay as `GroupJoin/SelectMany/DefaultIfEmpty`)
  - DetectionResultsRepository (`WithActorJoins` — 4 chained joins, mechanical conversion to method syntax)
  - MessageHistoryRepository
- **Not in scope** (no left-join patterns found): StopWordsRepository, InviteRepository, AnalyticsRepository, TelegramUserRepository
- **Query syntax caveat:** `LeftJoin` is method-syntax only. Correlated subquery patterns (e.g., `from mt in context.MessageTranslations.Where(mt => mt.MessageId == m.MessageId)`) cannot be expressed with `LeftJoin`'s independent key selectors — these stay as `GroupJoin`/`SelectMany`/`DefaultIfEmpty` in method syntax
- `AsNoTracking()` audit across all repositories — fix any missing instances on read-only queries

### #432 — VirusTotal Stream-Based JSON + Disposal

- Replace 5 `ReadAsStringAsync()` calls with `JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())`
- Wrap all `HttpResponseMessage` in `using` declarations
- Ensure error paths dispose responses

### #433 — Server-Side Take() on Implicit Ham Query

- Add `.Take(maxImplicitHam * 3)` before `.ToListAsync()` in MLTrainingDataRepository (~line 190)
- 3x over-fetch accounts for SimHash deduplication removal (~30-50%)
- **Note:** This is ordered by `text.Length descending` — the cap means deduplication only runs within the top-3x longest messages, a minor behavior change from global deduplication. Acceptable tradeoff for homelab scale.

### Tier 3: Merge Classifier Retraining Jobs

- Merge `TextClassifierRetrainingJob` and `BayesClassifierRetrainingJob` into a single `ClassifierRetrainingJob`
- Job loads training data once (both classifiers currently load identical datasets: same 627 spam + 1346 ham samples) → trains SDCA → trains Bayes → done
- `TrainingHandler` fires one `TriggerNowAsync` instead of two — eliminates concurrent retrain concern structurally
- Scheduled cron fires one job that handles both — Bayes now gets periodic retraining (currently only retrained on spam events)
- Halves training data pipeline memory since dataset only lives in memory once

## Wave 2 — Depends on Wave 1

### #424 — FrozenDictionary + Pre-Computed Aggregates for Bayes

**Depends on:** #428 (LoggerMessage covering hot paths)

- Convert `_spamWordCounts` / `_hamWordCounts` to `FrozenDictionary<string, int>` via `.ToFrozenDictionary()` at training time
- Pre-compute and cache as fields:
  - `_vocabularySize` (replaces `.Keys.Union().Count()`)
  - `_spamWordTotal` / `_hamWordTotal` (replaces `.Values.Sum()`)
  - `_laplaceDenominatorSpam` / `_laplaceDenominatorHam`
- `significantWords` list allocation stays (it's the return value)
- **Honest estimate:** ~20-30% allocation reduction per classification — tokenizer allocations dominate until #435 lands
- **Verify:** `IBayesClassifierService.GetMetadata()` consumed by `MemoryMetrics` — ensure `SpamVocabularySize` / `HamVocabularySize` fields still compile after data structure change

### #426 — RecyclableMemoryStream for Image Processing

**Depends on:** #425 (pool singleton registered)

- Inject `RecyclableMemoryStreamManager` into ProfileScanService and TelegramImageService
- Replace `new MemoryStream()` with `manager.GetStream()` for photo downloads
- Eliminate `.ToArray()` — pass stream directly to `Image.Load(stream)`. Dispose `RecyclableMemoryStream` immediately after `Image.Load()` returns (ImageSharp reads full contents, no further stream ownership needed)
- `ResizeForVisionAsync` signature: `byte[]` → `Stream` input

## Wave 3 — Breaking Changes

### #434 — Chunked AEAD Streaming Encryption

**Depends on:** #425 (RecyclableMemoryStream pipeline)

This commit includes the full encryption interface refactor from `byte[]` to `Stream` (deferred from Wave 1 to avoid two-phase interface churn).

**Interface change:**
- `EncryptBackup(byte[] jsonBytes, string passphrase)` → `EncryptBackup(Stream plaintext, Stream cipherOutput, string passphrase)`
- `DecryptBackup(byte[] encryptedBytes, string passphrase)` → `DecryptBackup(Stream cipherInput, Stream plainOutput, string passphrase)`
- `IsEncrypted(byte[] backupBytes)` → add `IsEncrypted(Stream input)` overload that reads header bytes only

**Protocol:**
- 1 MB plaintext chunks, each encrypted independently with AES-GCM
- **CRITICAL: Base nonce MUST be generated fresh via `RandomNumberGenerator.GetBytes` per encryption call — never cached or reused. AES-GCM nonce reuse with the same key breaks confidentiality.**
- Per-chunk nonce: XOR a 64-bit big-endian chunk counter into the last 8 bytes of the 12-byte base nonce
- Final chunk sentinel (length = 0) for truncation detection
- PBKDF2 key derivation happens once per backup (from passphrase + salt in header), NOT per chunk
- File header: `TGAEC2\0` magic (distinct from legacy `TGAENC\0`) + version byte + salt + base nonce

**Dual-read:**
- `IsEncrypted` checks first 7 bytes: `TGAENC\0` → legacy, `TGAEC2\0` → chunked
- Route to `DecryptChunked` or `DecryptLegacy` based on magic header
- `EncryptBackup` always writes new chunked format
- Tracking issue opened to remove `DecryptLegacy` later

**Memory impact:** Peak drops from ~300 MB (Wave 1 state) to ~2 MB (one read + one write chunk buffer)

### #435 — Span-Based Tokenizer with FrozenDictionary.AlternateLookup

**Depends on:** #424 (FrozenDictionary adopted)

- Replace `Regex.Matches()` with `Regex.EnumerateMatches()` — `ValueMatch` structs, no `Match` allocations
- Use `ReadOnlySpan<char>` slices from original message instead of new `string` per word
- `FrozenDictionary.GetAlternateLookup<ReadOnlySpan<char>>()` for zero-allocation dictionary lookups
- `ClassifyMessage` must remain synchronous (spans can't cross async boundaries) — confirmed synchronous from exploration
- Words added to `significantWords` list must be materialized to `string` via `.ToString()` before escaping the method (spans can't be stored on heap)
- **Allocation reduction opportunities to investigate during implementation:**
  - `ToLowerInvariant()` — may be avoidable if FrozenDictionary uses `StringComparer.OrdinalIgnoreCase` and AlternateLookup supports case-insensitive comparison
  - `RemoveEmojis()` — two `Regex.Replace` calls allocating new strings; may be unnecessary if emoji tokens are absent from vocabulary (zero probability weight in Bayes)
  - `Distinct()` — deduplication set may be avoidable if duplicate words are handled mathematically (count once, weight once)
- **Honest estimate:** Near-zero but not zero — some preprocessing allocations will remain; goal is to minimize with particular attention to eliminating steps the classifier can handle implicitly

## Commit Plan

| # | Issue | Commit Message |
|---|-------|---------------|
| 1 | #425 | `perf: RecyclableMemoryStream for backup pipeline + streaming download` |
| 2 | #427 | `perf: eliminate redundant MLContext + PredictionEnginePool for thread safety` |
| 3 | #428 | `perf: LoggerMessage source generator for hot-path logging` |
| 4 | #429 | `fix: replace scoped HttpClient with IHttpClientFactory via InternalApiClient` |
| 5 | #430 | `perf: IMemoryCache for RateLimitService + cleanup timer for token services` |
| 6 | #431 | `refactor: EF Core 10 LeftJoin where applicable + AsNoTracking audit` |
| 7 | #432 | `perf: VirusTotal stream-based JSON + response disposal` |
| 8 | #433 | `perf: server-side Take() on implicit ham training query` |
| 9 | T3 | `perf: merge classifier retraining into single job` |
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

| Metric | Current | Target | When Achievable |
|--------|---------|--------|-----------------|
| RSS range | 810-1272 MiB | 800-1000 MiB | After Wave 3 (#434) |
| Native gap steady state | 380-508 MiB | 250-350 MiB | After Wave 1 (#427) |
| Native gap peak | 808 MiB | <500 MiB | After Wave 3 (#434) |
| LOH fragmentation | 38% | <15% | After Wave 1 (#425) — ML.NET internals may keep it above 0% |
| Backup peak memory | ~500 MB | ~300 MB | After Wave 1 (#425, streaming serialization + early null) |
| Backup peak memory | ~300 MB | ~2 MB | After Wave 3 (#434, chunked AEAD) |

## PR Structure

- Single PR to `develop`: `perf: memory tuning phase 2`
- PR body: `Closes #424, Closes #425, Closes #426, Closes #427, Closes #428, Closes #429, Closes #430, Closes #431, Closes #432, Closes #433, Closes #434, Closes #435`
- One new tracking issue: remove legacy decryption path (`DecryptLegacy`) from #434
