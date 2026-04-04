# Memory Tuning Phase 2 — Review Fixes Design Spec

**Date:** 2026-03-28
**Branch:** `feat/ef-core-10-leftjoin-asnotracking-audit`
**Parent Spec:** `docs/superpowers/specs/2026-03-28-memory-tuning-phase2-design.md`
**Review Results:** `tmp/memory-tuning-phase2-review-results.md`
**PR Target:** `develop`

## Context

Multi-agent code review of the 13-commit memory tuning phase 2 implementation identified 14 issues across performance, correctness, security, and code hygiene. This spec covers the 12 issues selected for fixing. Two were dropped as disproportionate to their risk/value.

## Commit

Single commit: `fix: address code review findings for memory tuning phase 2`

## Scope

12 fixes across 6 critical/high and 6 medium issues. Two medium issues dropped.

### Dropped Issues

| # | Issue | Reason |
|---|-------|--------|
| 7 | `LoadModelAsync` unsynchronized with `_retrainingSemaphore` | Effectively zero risk — startup model load completes well before Quartz initializes and fires retrain jobs |
| 9 | `ClassifierRetrainingJob` loads training data twice | Interface churn to both classifier services disproportionate to gain — ~2,000 rows on a multi-hour cron cycle |

## Fixes

### 1. Pre-allocate ciphertext buffers in BackupEncryptionService

**File:** `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs`
**Category:** Performance (Critical)

**Problem:** `new byte[bytesRead]` inside encrypt loop (line 224) and `new byte[chunkLength]` + `new byte[chunkLength]` inside decrypt loop (lines 326, 331) allocate 1 MB LOH objects per chunk. A 200 MB backup produces ~200 LOH allocations per direction, defeating the "2 MB peak" claim.

**Fix:** Pre-allocate buffers before each loop:

- **Encrypt:** `var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize]` before the loop. Use `ciphertextBuffer.AsSpan(0, bytesRead)` as the ciphertext output span per iteration. Write `ciphertextBuffer.AsSpan(0, bytesRead)` to `cipherOutput`.
- **Decrypt:** `var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize]` and `var decryptedBuffer = new byte[EncryptionConstants.ChunkSize]` before the loop. Read ciphertext into `ciphertextBuffer`, decrypt into `decryptedBuffer`, write `decryptedBuffer.AsSpan(0, chunkLength)` to `plainOutput`.

Total fixed allocation: 2 MB (encrypt) or 3 MB (decrypt) instead of hundreds of transient LOH objects.

### 2. Fix hash-collision deduplication in BayesClassifier

**File:** `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs`
**Category:** Correctness (Critical)

**Problem:** `HashSet<int>` using `string.GetHashCode(wordSpan, OrdinalIgnoreCase)` treats two different words with the same 32-bit hash as duplicates, silently corrupting Bayes probability calculations.

**Fix:** Replace with `HashSet<UInt128>` using `XxHash128` for 128-bit hashing. Use `stackalloc` + `ToLowerInvariant` for case-insensitive, zero-heap-allocation hashing:

```csharp
var seenWords = new HashSet<UInt128>();

// Inside loop:
Span<char> lower = stackalloc char[wordSpan.Length];
wordSpan.ToLowerInvariant(lower);
var hash = XxHash128.HashToUInt128(MemoryMarshal.AsBytes(lower[..wordSpan.Length]));
if (!seenWords.Add(hash))
    continue;
```

Requires `using System.IO.Hashing;` and `using System.Runtime.InteropServices;` — both in-box on .NET 10.

### 3. Eliminate SSRF via Host header in InternalApiClient

**File:** `TelegramGroupsAdmin/Services/InternalApiClient.cs`
**Category:** Security (High)

**Problem:** `HttpClient.BaseAddress` derived from attacker-controllable `Host` request header. An attacker can redirect internal API calls to arbitrary network addresses.

**Fix:** Replace `IHttpContextAccessor` dependency with `IOptions<AppOptions>`. Derive base URL from `AppOptions.BaseUrl` (already exists, defaults to `http://localhost:5161`, configured via environment variable in production):

```csharp
public class InternalApiClient(IHttpClientFactory factory, IOptions<AppOptions> appOptions)
{
    public HttpClient CreateClient()
    {
        var client = factory.CreateClient("Internal");
        client.BaseAddress = new Uri(appOptions.Value.BaseUrl);
        return client;
    }
}
```

Remove `IHttpContextAccessor` registration for `InternalApiClient` from DI setup in `ServiceCollectionExtensions.cs`.

### 4. Remove drifting entryCount gauge from RateLimitService

**File:** `TelegramGroupsAdmin/Services/Auth/RateLimitService.cs`
**File:** `TelegramGroupsAdmin/Services/MemoryMetrics.cs`
**File:** `TelegramGroupsAdmin/Services/Auth/IRateLimitService.cs`
**Category:** Concurrency (Critical)

**Problem:** `_entryCount` with eviction callbacks can drift negative due to race between `IMemoryCache` eviction thread and the `Lock`-scoped `TryGetValue` → `Set` pattern. The counter is a monitoring gauge for per-account rate limit cache entries — not security-critical.

**Fix:** Delete the counter entirely:
- Remove `_entryCount` field, `EntryCount` property from `RateLimitService`
- Remove `EntryCount` from `IRateLimitService` interface
- Remove `RegisterPostEvictionCallback` and `Interlocked.Increment/Decrement` calls
- Remove `tga.cache.rate_limit.count` gauge from `MemoryMetrics`

The `EntryCount` properties on `IntermediateAuthService` and `PendingRecoveryCodesService` are unaffected — those read directly from `ConcurrentDictionary.Count` with no race.

### 5. Fix integration test DI gap (27 BackupServiceTests failures)

**File:** `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs`
**Category:** Test (Critical)

**Problem:** Test DI container missing `RecyclableMemoryStreamManager` registration after commit `008b2793` added it as a `BackupService` dependency.

**Fix:** Add before the `BackupService` registration (~line 99):
```csharp
services.AddSingleton(new RecyclableMemoryStreamManager());
```

Default pool options are sufficient for tests.

### 6. Replace custom ReadExactly/ReadFully with BCL methods

**File:** `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs`
**Category:** Dead code (High)

**Problem:** Private helpers `ReadExactly` (lines 405-416) and `ReadFully` (lines 422-433) reimplement `Stream.ReadExactly` and `Stream.ReadAtLeast` available since .NET 7.

**Fix:** Replace all call sites with BCL equivalents:
- `ReadExactly(stream, buf, offset, count)` → `stream.ReadExactly(buf, offset, count)`
- `ReadFully(stream, buf, 0, count)` → `stream.ReadAtLeast(buf.AsSpan(0, count), count, throwOnEndOfStream: false)`

Delete both private helper methods.

### 8. Deserialize directly from stream in RestoreInternalAsync

**File:** `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`
**Category:** Performance (Medium)

**Problem:** Line 461 calls `decryptedMs.ToArray()` to deserialize, allocating a contiguous ~250 MB byte[] copy of the already-positioned `RecyclableMemoryStream`.

**Fix:**
```csharp
decryptedMs.Position = 0;
databaseData = await JsonSerializer.DeserializeAsync<Dictionary<string, List<object>>>(
    decryptedMs, jsonOptions, cancellationToken);
```

### 10. Dispose jsonStream before tar write phase

**File:** `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`
**Category:** Performance (Medium)

**Problem:** `jsonStream` (~250 MB pool buffer) stays alive during entire tar-write phase after encryption completes at line 192. Pool buffer not returned until `using` block exits.

**Fix:** Add explicit dispose after encryption logging (after line 192):
```csharp
jsonStream.Dispose(); // return ~250 MB pool buffer before tar-write phase
```

The `using` declaration becomes a harmless no-op (double-dispose on `RecyclableMemoryStream` is safe).

### 11. Convert to primary constructors

**Files:**
- `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs`
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/ClassifierRetrainingJob.cs`

**Category:** Consistency (Medium)

**Fix:** Convert conventional constructor + field patterns to primary constructor syntax. No validation logic present — textbook primary constructor candidates. Aligns with rest of codebase.

### 12. Convert TokenizerOptions to record with init properties

**File:** `TelegramGroupsAdmin.Core/Utilities/TextTokenizer.cs`
**Category:** Correctness (Medium)

**Problem:** Mutable class with public setters. `TokenizerOptions.Default` is `static readonly` but nothing prevents `Default.RemoveEmojis = false` corrupting all callers.

**Fix:**
```csharp
public record TokenizerOptions
{
    public static readonly TokenizerOptions Default = new();
    public bool RemoveEmojis { get; init; } = true;
    public bool RemoveStopWords { get; init; } = true;
    public bool RemoveNumbers { get; init; } = true;
    public int MinWordLength { get; init; } = 2;
    public bool ConvertToLowerCase { get; init; } = true;
}
```

### 13. Restrict backup download to GlobalAdminOrOwner

**Files:**
- `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs`
- `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs`
- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`

**Category:** Security (Medium)

**Problem:** Backup download endpoint uses default authorization policy (any authenticated user). Backup contains full database.

**Fix:** Add policy name constant to `AuthenticationConstants`:
```csharp
public const string PolicyGlobalAdminOrOwner = "GlobalAdminOrOwner";
```

Reference the constant in both the policy registration (`ServiceCollectionExtensions.cs:88`) and the endpoint (`BackupEndpoints.cs:65`):
```csharp
.RequireAuthorization(AuthenticationConstants.PolicyGlobalAdminOrOwner)
```

### 14. Make _wordBoundaryRegex static readonly

**File:** `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs`
**Category:** Clarity (Low)

**Problem:** Per-instance field holds reference to a static `GeneratedRegex` instance. Redundant pointer per `BayesClassifier` instance.

**Fix:** Change `private readonly` to `private static readonly` on line 29.

## Files Modified

| File | Issues |
|------|--------|
| `BackupEncryptionService.cs` | 1, 6, 11 |
| `BayesClassifier.cs` | 2, 14 |
| `InternalApiClient.cs` | 3 |
| `RateLimitService.cs` | 4 |
| `IRateLimitService.cs` | 4 |
| `MemoryMetrics.cs` | 4 |
| `BackupServiceTests.cs` | 5 |
| `BackupService.cs` | 8, 10 |
| `ClassifierRetrainingJob.cs` | 11 |
| `TextTokenizer.cs` | 12 |
| `AuthenticationConstants.cs` | 13 |
| `BackupEndpoints.cs` | 13 |
| `ServiceCollectionExtensions.cs` | 3, 13 |

## Build & Test Strategy

- `dotnet build` after all changes
- `dotnet run --migrate-only` for DI validation
- Full test suite run
