# Memory Tuning Phase 2 — Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Address 12 code review findings (correctness bugs, performance regressions, security issues, code hygiene) from the memory tuning phase 2 multi-agent review.

**Architecture:** Single commit touching 13 files across 7 tasks grouped by file proximity. No interface changes except removing `EntryCount` from `IRateLimitService`. All fixes are localized — no new features or architectural changes.

**Tech Stack:** .NET 10, AES-GCM, XxHash128, RecyclableMemoryStream, IMemoryCache, EF Core 10

**Spec:** `docs/superpowers/specs/2026-03-28-memory-tuning-phase2-review-fixes-design.md`

**Branch:** `feat/ef-core-10-leftjoin-asnotracking-audit` (existing)

**Critical rules:**
- Single commit for all fixes: `fix: address code review findings for memory tuning phase 2`
- `dotnet build` after all changes
- `dotnet run --migrate-only` for DI validation
- Full test suite run (in background — takes ~20 min)

---

## Task 1: BackupEncryptionService — Buffer Reuse, BCL Methods, Primary Constructor

**Fixes:** #1 (per-chunk allocation), #6 (custom ReadExactly/ReadFully), #11 (primary constructor)

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs`

- [ ] **Step 1: Convert to primary constructor**

Replace the class declaration, field, and constructor (lines 13-20):

```csharp
// OLD:
public class BackupEncryptionService : IBackupEncryptionService
{
    private readonly ILogger<BackupEncryptionService> _logger;

    public BackupEncryptionService(ILogger<BackupEncryptionService> logger)
    {
        _logger = logger;
    }
```

```csharp
// NEW:
public class BackupEncryptionService(ILogger<BackupEncryptionService> logger) : IBackupEncryptionService
{
```

Then replace all `_logger` references with `logger` throughout the file.

- [ ] **Step 2: Pre-allocate ciphertext buffer in encrypt loop**

In `EncryptBackup(Stream, Stream, string)` (~line 199), add a ciphertext buffer alongside the existing allocations:

```csharp
var chunkBuffer = new byte[EncryptionConstants.ChunkSize];
var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize]; // NEW: reusable ciphertext buffer
var chunkNonce = new byte[EncryptionConstants.NonceSizeBytes];
var tag = new byte[EncryptionConstants.TagSizeBytes];
var lengthBuffer = new byte[4];
```

Replace the per-iteration allocation inside the encrypt loop (lines 222-228):

```csharp
// OLD:
var plaintextSlice = chunkBuffer.AsSpan(0, bytesRead);
var ciphertext = new byte[bytesRead];
aesGcm.Encrypt(chunkNonce, plaintextSlice, ciphertext, tag);

// Write ciphertext + tag
cipherOutput.Write(ciphertext);
cipherOutput.Write(tag);
```

```csharp
// NEW:
var plaintextSlice = chunkBuffer.AsSpan(0, bytesRead);
var ciphertextSlice = ciphertextBuffer.AsSpan(0, bytesRead);
aesGcm.Encrypt(chunkNonce, plaintextSlice, ciphertextSlice, tag);

// Write ciphertext + tag
cipherOutput.Write(ciphertextSlice);
cipherOutput.Write(tag);
```

- [ ] **Step 3: Pre-allocate buffers in decrypt loop**

In `DecryptChunked` (~line 298), add two reusable buffers alongside existing allocations:

```csharp
var chunkNonce = new byte[EncryptionConstants.NonceSizeBytes];
var tag = new byte[EncryptionConstants.TagSizeBytes];
var lengthBuffer = new byte[4];
var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize]; // NEW: reusable ciphertext buffer
var decryptedBuffer = new byte[EncryptionConstants.ChunkSize];  // NEW: reusable decrypted buffer
```

Replace the per-iteration allocations inside the decrypt loop (lines 325-342):

```csharp
// OLD:
var ciphertext = new byte[chunkLength];
ReadExactly(cipherInput, ciphertext, 0, chunkLength);
ReadExactly(cipherInput, tag, 0, EncryptionConstants.TagSizeBytes);

var decryptedChunk = new byte[chunkLength];
try
{
    aesGcm.Decrypt(chunkNonce, ciphertext, tag, decryptedChunk);
}
catch (CryptographicException ex)
{
    logger.LogWarning(ex, "Chunk {ChunkIndex} decryption failed - likely incorrect passphrase or corrupted data", chunkCounter);
    throw new CryptographicException("Failed to decrypt backup. Incorrect passphrase or corrupted file.", ex);
}

plainOutput.Write(decryptedChunk);
```

```csharp
// NEW:
var ciphertextSlice = ciphertextBuffer.AsSpan(0, chunkLength);
cipherInput.ReadExactly(ciphertextBuffer, 0, chunkLength);
cipherInput.ReadExactly(tag, 0, EncryptionConstants.TagSizeBytes);

var decryptedSlice = decryptedBuffer.AsSpan(0, chunkLength);
try
{
    aesGcm.Decrypt(chunkNonce, ciphertextSlice, tag, decryptedSlice);
}
catch (CryptographicException ex)
{
    logger.LogWarning(ex, "Chunk {ChunkIndex} decryption failed - likely incorrect passphrase or corrupted data", chunkCounter);
    throw new CryptographicException("Failed to decrypt backup. Incorrect passphrase or corrupted file.", ex);
}

plainOutput.Write(decryptedSlice);
```

- [ ] **Step 4: Replace all ReadExactly/ReadFully call sites with BCL methods**

Replace `ReadFully` call in encrypt loop (~line 211):

```csharp
// OLD:
var bytesRead = ReadFully(plaintext, chunkBuffer, 0, EncryptionConstants.ChunkSize);

// NEW:
var bytesRead = plaintext.ReadAtLeast(chunkBuffer.AsSpan(0, EncryptionConstants.ChunkSize), EncryptionConstants.ChunkSize, throwOnEndOfStream: false);
```

Replace `ReadExactly` calls in `DecryptBackup` header reading (~lines 263, 283, 290, 293):

```csharp
// OLD:
ReadExactly(cipherInput, magicBuffer, 0, magicBuffer.Length);
// ... later:
ReadExactly(cipherInput, versionByte, 0, 1);
ReadExactly(cipherInput, salt, 0, salt.Length);
ReadExactly(cipherInput, baseNonce, 0, baseNonce.Length);

// NEW:
cipherInput.ReadExactly(magicBuffer, 0, magicBuffer.Length);
// ... later:
cipherInput.ReadExactly(versionByte, 0, 1);
cipherInput.ReadExactly(salt, 0, salt.Length);
cipherInput.ReadExactly(baseNonce, 0, baseNonce.Length);
```

Replace `ReadExactly` call in `IsEncrypted(Stream)` if present.

- [ ] **Step 5: Delete ReadExactly and ReadFully private methods**

Delete the two private helpers (lines ~405-433):

```csharp
// DELETE entirely:
private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count) { ... }
private static int ReadFully(Stream stream, byte[] buffer, int offset, int count) { ... }
```

---

## Task 2: BayesClassifier — Hash Collision Fix + Static Regex

**Fixes:** #2 (hash-collision deduplication), #14 (static readonly regex)

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs`

- [ ] **Step 1: Add required usings at top of file**

Add these using directives if not already present:

```csharp
using System.IO.Hashing;
using System.Runtime.InteropServices;
```

- [ ] **Step 2: Change _wordBoundaryRegex to static readonly**

Line 29:

```csharp
// OLD:
private readonly Regex _wordBoundaryRegex = TextTokenizer.GetWordBoundaryRegex();

// NEW:
private static readonly Regex _wordBoundaryRegex = TextTokenizer.GetWordBoundaryRegex();
```

- [ ] **Step 3: Replace HashSet<int> with HashSet<UInt128> and XxHash128**

In `ClassifyMessage` (~line 90):

```csharp
// OLD:
var seenWords = new HashSet<int>(); // track seen words by hash to avoid duplicates

// NEW:
var seenWords = new HashSet<UInt128>(); // track seen words by 128-bit hash (collision-safe)
```

Replace the deduplication block (~lines 121-124):

```csharp
// OLD:
// Deduplicate using case-insensitive hash (matching the FrozenDictionary comparer)
var hash = string.GetHashCode(wordSpan, StringComparison.OrdinalIgnoreCase);
if (!seenWords.Add(hash))
    continue;

// NEW:
// Deduplicate using 128-bit hash — zero-allocation via stackalloc + ToLowerInvariant
Span<char> lowerBuf = stackalloc char[wordSpan.Length];
wordSpan.ToLowerInvariant(lowerBuf);
var hash = XxHash128.HashToUInt128(MemoryMarshal.AsBytes(lowerBuf));
if (!seenWords.Add(hash))
    continue;
```

---

## Task 3: InternalApiClient — SSRF Fix via AppOptions.BaseUrl

**Fix:** #3

**Files:**
- Modify: `TelegramGroupsAdmin/Services/InternalApiClient.cs`

Note: `ServiceCollectionExtensions.cs:58-59` does NOT need changes — the DI registration is `AddScoped<InternalApiClient>()` with no factory lambda, so the container resolves the new `IOptions<AppOptions>` constructor parameter automatically.

- [ ] **Step 1: Rewrite InternalApiClient**

Replace the entire file content:

```csharp
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;

namespace TelegramGroupsAdmin.Services;

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

- [ ] **Step 2: Verify no other code depends on IHttpContextAccessor in InternalApiClient**

Search for usages of `InternalApiClient` to confirm no caller depends on the per-request Host header behavior. The `CreateClient()` return type and signature are unchanged — only the internal BaseAddress source changes.

Run: `grep -r "InternalApiClient" --include="*.cs" -l` to verify all consumers just call `CreateClient()`.

---

## Task 4: RateLimitService — Remove Drifting EntryCount Gauge

**Fix:** #4

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Auth/RateLimitService.cs`
- Modify: `TelegramGroupsAdmin/Services/Auth/IRateLimitService.cs`
- Modify: `TelegramGroupsAdmin/Services/MemoryMetrics.cs`

- [ ] **Step 1: Remove EntryCount from IRateLimitService**

In `IRateLimitService.cs`, delete lines 18-21:

```csharp
// DELETE:
/// <summary>
/// Number of active rate limit tracking entries (for memory instrumentation).
/// </summary>
int EntryCount { get; }
```

- [ ] **Step 2: Remove _entryCount field, property, and eviction callback from RateLimitService**

In `RateLimitService.cs`:

Delete field and property (lines 16-18):
```csharp
// DELETE:
private int _entryCount;

public int EntryCount => _entryCount;
```

In `RecordAttemptAsync`, simplify the cache set block (lines 99-130). Replace:

```csharp
// OLD:
using (_lock.EnterScope())
{
    var isNew = !_cache.TryGetValue(key, out List<DateTimeOffset>? attempts);
    if (isNew || attempts is null)
    {
        attempts = [];
    }

    attempts.Add(now);

    var options = new MemoryCacheEntryOptions
    {
        SlidingExpiration = limit.Window
    };

    if (isNew)
    {
        // Register eviction callback to decrement counter only when entry is truly removed
        options.RegisterPostEvictionCallback((_, _, reason, _) =>
        {
            // Replaced means the entry was overwritten via Set — the new entry will manage the count
            if (reason != EvictionReason.Replaced)
            {
                Interlocked.Decrement(ref _entryCount);
            }
        });

        Interlocked.Increment(ref _entryCount);
    }

    _cache.Set(key, attempts, options);
}
```

```csharp
// NEW:
using (_lock.EnterScope())
{
    var attempts = _cache.Get<List<DateTimeOffset>>(key) ?? [];

    attempts.Add(now);

    var options = new MemoryCacheEntryOptions
    {
        SlidingExpiration = limit.Window
    };

    _cache.Set(key, attempts, options);
}
```

- [ ] **Step 3: Remove rate limit gauge from MemoryMetrics**

In `MemoryMetrics.cs`, remove `IRateLimitService rateLimitService` from the constructor:

```csharp
// OLD:
public MemoryMetrics(
    IChatCache chatCache,
    IChatHealthCache chatHealthCache,
    ITelegramSessionManager sessionManager,
    IMediaRefetchQueueService mediaRefetchQueue,
    IDocumentationService documentationService,
    IRateLimitService rateLimitService,
    IIntermediateAuthService intermediateAuthService,

// NEW:
public MemoryMetrics(
    IChatCache chatCache,
    IChatHealthCache chatHealthCache,
    ITelegramSessionManager sessionManager,
    IMediaRefetchQueueService mediaRefetchQueue,
    IDocumentationService documentationService,
    IIntermediateAuthService intermediateAuthService,
```

Delete the rate limit gauge (lines 64-68):
```csharp
// DELETE:
meter.CreateObservableGauge(
    "tga.cache.rate_limit.count",
    () => rateLimitService.EntryCount,
    description: "Number of active rate limit tracking entries");
```

Move the `// --- Auth state ---` comment to above the `auth_tokens` gauge (line 70) so the section label is preserved.

Keep the `using TelegramGroupsAdmin.Services.Auth;` import — still needed by `IIntermediateAuthService` and `IPendingRecoveryCodesService`.

---

## Task 5: BackupService — Stream Deserialization + Early jsonStream Dispose

**Fixes:** #8 (ToArray on restore), #10 (jsonStream held during tar write)

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`

- [ ] **Step 1: Replace ToArray() with DeserializeAsync in RestoreInternalAsync**

At line 461, replace:

```csharp
// OLD:
databaseData = JsonSerializer.Deserialize<Dictionary<string, List<object>>>(decryptedMs.ToArray(), jsonOptions);

// NEW:
databaseData = await JsonSerializer.DeserializeAsync<Dictionary<string, List<object>>>(decryptedMs, jsonOptions, cancellationToken);
```

Note: `decryptedMs.Position` is already set to 0 by the `DecryptBackup` call writing from position 0, and the `Position = 0` may already be on line 459. Verify that position is 0 before deserialization.

- [ ] **Step 2: Dispose jsonStream early in CreateBackupInternalAsync**

After the encryption logging (~line 192), add an explicit dispose:

```csharp
_logger.LogInformation("Encrypted database: {OriginalSize} bytes → {EncryptedSize} bytes",
    jsonStream.Length, encryptedStream.Length);
jsonStream.Dispose(); // return ~250 MB pool buffer before tar-write phase
```

The `using` declaration on `jsonStream` (line 165) becomes a harmless no-op — double-dispose on `RecyclableMemoryStream` is safe.

---

## Task 6: Test DI Fix + Code Hygiene (TokenizerOptions, Auth Const, ClassifierRetrainingJob)

**Fixes:** #5 (test DI), #11 (ClassifierRetrainingJob primary constructor), #12 (TokenizerOptions record), #13 (backup auth policy const)

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Jobs/ClassifierRetrainingJob.cs`
- Modify: `TelegramGroupsAdmin.Core/Utilities/TextTokenizer.cs`
- Modify: `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs`
- Modify: `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Fix BackupServiceTests DI**

In `BackupServiceTests.cs`, add before line 100 (before the `BackupService` registration):

```csharp
services.AddSingleton(new Microsoft.IO.RecyclableMemoryStreamManager());
```

Add `using Microsoft.IO;` at top of file if not already present.

- [ ] **Step 2: Convert ClassifierRetrainingJob to primary constructor**

Replace lines 16-33:

```csharp
// OLD:
public class ClassifierRetrainingJob : IJob
{
    private readonly IMLTextClassifierService _mlClassifier;
    private readonly IBayesClassifierService _bayesClassifier;
    private readonly ILogger<ClassifierRetrainingJob> _logger;
    private readonly JobMetrics _jobMetrics;

    public ClassifierRetrainingJob(
        IMLTextClassifierService mlClassifier,
        IBayesClassifierService bayesClassifier,
        ILogger<ClassifierRetrainingJob> logger,
        JobMetrics jobMetrics)
    {
        _mlClassifier = mlClassifier;
        _bayesClassifier = bayesClassifier;
        _logger = logger;
        _jobMetrics = jobMetrics;
    }
```

```csharp
// NEW:
public class ClassifierRetrainingJob(
    IMLTextClassifierService mlClassifier,
    IBayesClassifierService bayesClassifier,
    ILogger<ClassifierRetrainingJob> logger,
    JobMetrics jobMetrics) : IJob
{
```

Then replace all `_mlClassifier` → `mlClassifier`, `_bayesClassifier` → `bayesClassifier`, `_logger` → `logger`, `_jobMetrics` → `jobMetrics` throughout the file.

- [ ] **Step 3: Convert TokenizerOptions to record with init properties**

In `TextTokenizer.cs`, replace lines 227-248:

```csharp
// OLD:
public class TokenizerOptions
{
    public static readonly TokenizerOptions Default = new();
    public bool RemoveEmojis { get; set; } = true;
    public bool RemoveStopWords { get; set; } = true;
    public bool RemoveNumbers { get; set; } = true;
    public int MinWordLength { get; set; } = 2;
    public bool ConvertToLowerCase { get; set; } = true;
}

// NEW:
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

- [ ] **Step 4: Add PolicyGlobalAdminOrOwner constant**

In `AuthenticationConstants.cs`, add at the end of the class (before closing brace):

```csharp
/// <summary>
/// Authorization policy name for global admin or owner access.
/// </summary>
public const string PolicyGlobalAdminOrOwner = "GlobalAdminOrOwner";
```

- [ ] **Step 5: Use policy constant in BackupEndpoints**

In `BackupEndpoints.cs`, add using:

```csharp
using TelegramGroupsAdmin.Constants;
```

Replace line 65:

```csharp
// OLD:
}).RequireAuthorization();

// NEW:
}).RequireAuthorization(AuthenticationConstants.PolicyGlobalAdminOrOwner);
```

- [ ] **Step 6: Use policy constant in ServiceCollectionExtensions**

In `ServiceCollectionExtensions.cs` line 88, replace:

```csharp
// OLD:
.AddPolicy("GlobalAdminOrOwner", policy =>

// NEW:
.AddPolicy(AuthenticationConstants.PolicyGlobalAdminOrOwner, policy =>
```

Verify `using TelegramGroupsAdmin.Constants;` is already present (it is — `AuthenticationConstants.CookieExpiration` is used on line 79).

---

## Task 7: Build, Test, and Commit

- [ ] **Step 1: Build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings (or only pre-existing warnings)

- [ ] **Step 2: DI validation**

Run: `dotnet run --project TelegramGroupsAdmin --migrate-only`
Expected: Clean startup and exit (validates DI container resolution)

- [ ] **Step 3: Run full test suite (background)**

Run in background with output to file:
```bash
dotnet test --no-build --verbosity normal > /tmp/test-results-review-fixes.txt 2>&1
```

Expected: All tests pass, including the previously-failing 27 BackupServiceTests.

- [ ] **Step 4: Commit all changes**

```bash
git add \
  TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs \
  TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs \
  TelegramGroupsAdmin/Services/InternalApiClient.cs \
  TelegramGroupsAdmin/Services/Auth/RateLimitService.cs \
  TelegramGroupsAdmin/Services/Auth/IRateLimitService.cs \
  TelegramGroupsAdmin/Services/MemoryMetrics.cs \
  TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs \
  TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs \
  TelegramGroupsAdmin.BackgroundJobs/Jobs/ClassifierRetrainingJob.cs \
  TelegramGroupsAdmin.Core/Utilities/TextTokenizer.cs \
  TelegramGroupsAdmin/Constants/AuthenticationConstants.cs \
  TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs \
  TelegramGroupsAdmin/ServiceCollectionExtensions.cs

git commit -m "fix: address code review findings for memory tuning phase 2

- Pre-allocate reusable ciphertext/decrypted buffers in AEAD encrypt/decrypt loops
- Replace custom ReadExactly/ReadFully with BCL Stream.ReadExactly/ReadAtLeast
- Fix hash-collision deduplication in BayesClassifier (XxHash128 + stackalloc)
- Eliminate SSRF via Host header: InternalApiClient uses AppOptions.BaseUrl
- Remove drifting entryCount gauge from RateLimitService
- Fix BackupServiceTests DI: register RecyclableMemoryStreamManager
- Deserialize directly from stream in RestoreInternalAsync (eliminate ToArray)
- Dispose jsonStream before tar-write phase (return pool buffer earlier)
- Convert BackupEncryptionService + ClassifierRetrainingJob to primary constructors
- Convert TokenizerOptions to record with init properties
- Restrict backup download to GlobalAdminOrOwner policy (use const)
- Make BayesClassifier._wordBoundaryRegex static readonly

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```
