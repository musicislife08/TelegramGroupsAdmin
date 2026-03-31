# Memory Tuning Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce RSS from 810-1272 MiB to 800-1000 MiB by eliminating allocation hotspots across backup, ML, image processing, auth, and query pipelines.

**Architecture:** 13 commits in dependency order across 3 waves. Wave 1 (9 independent fixes), Wave 2 (2 items depending on Wave 1), Wave 3 (2 breaking changes depending on Waves 1+2). Single PR to develop.

**Tech Stack:** .NET 10, RecyclableMemoryStream, Microsoft.Extensions.ML (PredictionEnginePool), FrozenDictionary, LoggerMessage source generators, EF Core 10 LeftJoin, AES-GCM chunked AEAD.

**Spec:** `docs/superpowers/specs/2026-03-28-memory-tuning-phase2-design.md`

**Branch:** `perf/memory-tuning-phase2` (already created from develop)

**Critical rules:**
- Subagents do file edits ONLY — never run `dotnet build`, `dotnet test`, or `dotnet run`
- Parent agent owns ALL build/test/startup validation
- No git worktrees
- One commit per task after build + startup validation passes

---

## Task 1: #425 — RecyclableMemoryStream for Backup Pipeline + Streaming Download

**Files:**
- Modify: `Directory.Packages.props` — add RecyclableMemoryStream package
- Modify: `TelegramGroupsAdmin.BackgroundJobs/TelegramGroupsAdmin.BackgroundJobs.csproj` — add PackageReference
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs:114-172` — register RecyclableMemoryStreamManager singleton
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs:161,181` — streaming serialization + early null
- Create: `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs` — minimal API download endpoint
- Modify: `TelegramGroupsAdmin/Program.cs:80+` — map backup download endpoint
- Modify: `TelegramGroupsAdmin/Components/Shared/BackupBrowser.razor:414-434` — replace JS interop with NavigateTo

- [ ] **Step 1: Add NuGet package**

Add to `Directory.Packages.props` in the Utilities section:
```xml
<PackageVersion Include="Microsoft.IO.RecyclableMemoryStream" Version="3.0.1" />
```

Run: `dotnet add TelegramGroupsAdmin.BackgroundJobs package Microsoft.IO.RecyclableMemoryStream`

- [ ] **Step 2: Register RecyclableMemoryStreamManager singleton**

In `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`, inside `AddApplicationServices()` (~line 114), add:
```csharp
services.AddSingleton(new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options
{
    BlockSize = 128 * 1024,           // 128 KB blocks
    LargeBufferMultiple = 1024 * 1024, // 1 MB large buffers
    MaximumBufferSize = 512 * 1024 * 1024, // 512 MB max stream
    MaximumSmallPoolFreeBytes = 16 * 1024 * 1024, // 16 MB small pool cap
    MaximumLargePoolFreeBytes = 256 * 1024 * 1024  // 256 MB large pool cap
}));
```

Add `using Microsoft.IO;` at top.

- [ ] **Step 3: Modify BackupService for streaming serialization**

In `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs`:

Inject `RecyclableMemoryStreamManager` via constructor.

Replace the serialization block (~line 161):
```csharp
// OLD: var databaseJson = JsonSerializer.SerializeToUtf8Bytes(backup.Data, jsonOptions);
// NEW:
using var jsonStream = _streamManager.GetStream("BackupService.Serialize");
await JsonSerializer.SerializeAsync(jsonStream, backup.Data, jsonOptions, cancellationToken: cancellationToken);
jsonStream.Position = 0;
var databaseJson = jsonStream.ToArray(); // pool-backed, avoids LOH fragmentation
backup.Data = null; // release object graph early
```

The rest of the pipeline (EncryptBackup call at ~line 181) stays `byte[]` — no interface change in Wave 1.

- [ ] **Step 4: Create backup download minimal API endpoint**

Create `TelegramGroupsAdmin/Endpoints/BackupEndpoints.cs`:
```csharp
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using TelegramGroupsAdmin.BackgroundJobs.Constants;

namespace TelegramGroupsAdmin.Endpoints;

public static partial class BackupEndpoints
{
    [GeneratedRegex(@"^backup_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.tar\.gz$")]
    private static partial Regex BackupFilenamePattern();

    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet("/api/backup/download/{filename}", [Authorize] async (
            string filename,
            IBackgroundJobConfigService jobConfigService,
            IAuditService auditService,
            HttpContext httpContext) =>
        {
            if (!BackupFilenamePattern().IsMatch(filename))
                return Results.BadRequest("Invalid backup filename format");

            // Resolve backup directory from DB-stored config (same as ScheduledBackupJob)
            var config = await jobConfigService.GetBackupConfigAsync();
            var backupDir = config?.ScheduledBackup?.BackupDirectory
                ?? BackupRetentionConstants.DefaultBackupDirectory;
            var fullPath = Path.GetFullPath(Path.Combine(backupDir, filename));

            if (!fullPath.StartsWith(Path.GetFullPath(backupDir), StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Invalid path");

            if (!File.Exists(fullPath))
                return Results.NotFound();

            // Audit log the download (security-sensitive action)
            await auditService.LogEventAsync(
                AuditEventType.DataExported,
                actor: httpContext.User.ToActor(),
                target: null,
                details: $"Downloaded backup: {filename}");

            return Results.File(
                fullPath,
                contentType: "application/gzip",
                fileDownloadName: filename,
                enableRangeProcessing: true);
        });
    }
}
```

**Note:** The backup directory comes from `IBackgroundJobConfigService` (DB-stored config), NOT from `IConfiguration`/appsettings. Falls back to `BackupRetentionConstants.DefaultBackupDirectory` ("/data/backups"). The audit log is preserved from the original `BackupBrowser.razor` download method.

- [ ] **Step 5: Map endpoint in Program.cs**

In `TelegramGroupsAdmin/Program.cs`, after the app is built, add:
```csharp
app.MapBackupEndpoints();
```

Add `using TelegramGroupsAdmin.Endpoints;` at top.

- [ ] **Step 6: Update BackupBrowser.razor download method**

In `TelegramGroupsAdmin/Components/Shared/BackupBrowser.razor`, replace the `DownloadBackup` method (~lines 414-434):

Replace the `File.ReadAllBytesAsync` + JS interop block. Remove the `AuditService.LogEventAsync` call (moved to the endpoint). Replace with:
```csharp
NavigationManager.NavigateTo($"/api/backup/download/{backup.FileName}", forceLoad: true);
```

Inject `NavigationManager` if not already injected. Remove the `IJSRuntime` JS interop download call. The audit log is now handled by the minimal API endpoint itself.

- [ ] **Step 7: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup logs → SIGTERM
Expected: Clean startup, no DI errors.

- [ ] **Step 8: Commit**

```
git add -A && git commit -m "perf: RecyclableMemoryStream for backup pipeline + streaming download

Closes #425"
```

---

## Task 2: #427 — MLContext Elimination + PredictionEnginePool

**Files:**
- Modify: `Directory.Packages.props` — add Microsoft.Extensions.ML package
- Modify: `TelegramGroupsAdmin.ContentDetection/TelegramGroupsAdmin.ContentDetection.csproj` — add PackageReference
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/MLTextClassifierService.cs:103,143,145,170,237` — shared MLContext, PredictionEnginePool, ITransformer disposal

- [ ] **Step 1: Add NuGet package**

Add to `Directory.Packages.props` in the AI/ML section (matching existing `Microsoft.ML` at version 5.0.0):
```xml
<PackageVersion Include="Microsoft.Extensions.ML" Version="5.0.0" />
```

Run: `dotnet add TelegramGroupsAdmin.ContentDetection package Microsoft.Extensions.ML`

- [ ] **Step 2: Refactor MLTextClassifierService**

In `TelegramGroupsAdmin.ContentDetection/ML/MLTextClassifierService.cs`:

**a) Add shared MLContext field:**
```csharp
private readonly MLContext _mlContext = new(seed: MLConstants.MlNetSeed);
```

**b) In `TrainModelAsync` (~line 103):** Replace `var mlContext = new MLContext(seed: MLConstants.MlNetSeed);` with `var mlContext = _mlContext;` (protected by existing `_retrainingSemaphore`).

**c) In `SaveModelAsync` (~line 170):** Remove `var mlContext = new MLContext();`. Use `_mlContext` instead (called from within `TrainModelAsync` under the semaphore).

**d) In `LoadModelAsync` (~line 237):** Keep the local `var mlContext = new MLContext();` — runs once at startup, avoids race with shared field.

**e) Add ITransformer disposal on model swap (~line 145):**
Before `Interlocked.Exchange`:
```csharp
var oldContainer = _currentModel;
// ... create new container ...
Interlocked.Exchange(ref _currentModel, newContainer);
(oldContainer?.Model as IDisposable)?.Dispose();
```

**f) Replace PredictionEngine with PredictionEnginePool:**

This requires restructuring. Instead of storing a `PredictionEngine` in `ModelContainer`:
- Register `PredictionEnginePool<SpamTextFeatures, SpamPrediction>` in DI
- After model save, signal the pool to reload from the model file
- In `Predict()`, use the pool instead of the stored engine

Read the current `ModelContainer` record (lines 32-35) and `Predict` method to understand the exact current flow before implementing. The pool needs to be configured with the model file path and reload on signal.

- [ ] **Step 3: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 4: Commit**

```
git add -A && git commit -m "perf: eliminate redundant MLContext + PredictionEnginePool for thread safety

Closes #427"
```

---

## Task 3: #428 — LoggerMessage Source Generator

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.Log.cs` — LoggerMessage partial methods
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs` — make class partial, replace log calls

- [ ] **Step 1: Read MessageProcessingService.cs to catalog all log calls**

Read the file in full. Catalog every `logger.Log*` call that uses `.ToLogDebug()` or `.ToLogInfo()` — there are ~13 LogDebug calls identified at lines 94, 210, 289, 297, 364, 408, 432, 599-600, 700, 709-710, 727, 731, 759, 786-787.

Group them by message template pattern to create reusable `[LoggerMessage]` definitions. Each unique template becomes one partial method.

- [ ] **Step 2: Create LoggerMessage partial class**

Create `MessageProcessingService.Log.cs` as a partial class with `[LoggerMessage]` attributes. Example pattern:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

public partial class MessageProcessingService
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing message from {User} in {Chat}")]
    private static partial void LogProcessingMessage(ILogger logger, string user, string chat);

    // ... one method per unique template
}
```

- [ ] **Step 3: Make MessageProcessingService partial and replace log calls**

Add `partial` keyword to the class declaration. Replace each `logger.LogDebug("...", x.ToLogDebug(), y.ToLogDebug())` with the generated method call. The `.ToLogDebug()` calls move inside the generated method's `IsEnabled` guard.

- [ ] **Step 4: Apply same pattern to other hot-path services**

Check these services for hot-path logging and apply the same pattern if they have `.ToLogDebug()` calls:
- Detection pipeline services in `TelegramGroupsAdmin.ContentDetection`
- Moderation handler services in `TelegramGroupsAdmin.Telegram/Services/Moderation`

- [ ] **Step 5: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 6: Commit**

```
git add -A && git commit -m "perf: LoggerMessage source generator for hot-path logging

Closes #428"
```

---

## Task 4: #429 — IHttpClientFactory via InternalApiClient

**Files:**
- Create: `TelegramGroupsAdmin/Services/InternalApiClient.cs` — typed client wrapper
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs:58-68` — replace AddScoped HttpClient with AddHttpClient + InternalApiClient
- Modify: Any Blazor components or services that currently inject `HttpClient` directly — switch to `InternalApiClient`

- [ ] **Step 1: Create InternalApiClient**

Create `TelegramGroupsAdmin/Services/InternalApiClient.cs`:
```csharp
namespace TelegramGroupsAdmin.Services;

public class InternalApiClient(IHttpClientFactory factory, IHttpContextAccessor contextAccessor)
{
    public HttpClient CreateClient()
    {
        var client = factory.CreateClient("Internal");
        var httpContext = contextAccessor.HttpContext;
        if (httpContext is null)
        {
            client.BaseAddress = new Uri("http://localhost:5161");
            return client;
        }
        var host = httpContext.Request.Host;
        var hostString = host.Host == "0.0.0.0"
            ? $"localhost:{host.Port}"
            : host.ToString();
        client.BaseAddress = new Uri($"{httpContext.Request.Scheme}://{hostString}");
        return client;
    }
}
```

- [ ] **Step 2: Update DI registration**

In `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`, replace the `AddScoped<HttpClient>` lambda (lines 58-68) with:
```csharp
services.AddHttpClient("Internal");
services.AddScoped<InternalApiClient>();
```

- [ ] **Step 3: Find and update all HttpClient consumers**

Search for constructor injection of `HttpClient` (not `IHttpClientFactory`) in Blazor components and services. Replace with `InternalApiClient` injection and `_client.CreateClient()` at call sites.

Run: `grep -r "HttpClient" --include="*.cs" --include="*.razor" TelegramGroupsAdmin/` to find all usages.

- [ ] **Step 4: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 5: Commit**

```
git add -A && git commit -m "fix: replace scoped HttpClient with IHttpClientFactory via InternalApiClient

Closes #429"
```

---

## Task 5: #430 — IMemoryCache for RateLimitService + Cleanup Timers

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Auth/RateLimitService.cs:13-14,52` — replace ConcurrentDictionary with IMemoryCache
- Modify: `TelegramGroupsAdmin/Services/Auth/IntermediateAuthService.cs:41-51` — replace Task.Run cleanup with IHostedService
- Modify: `TelegramGroupsAdmin/Services/Auth/PendingRecoveryCodesService.cs:42-53` — replace Task.Run cleanup with IHostedService
- Create: `TelegramGroupsAdmin/Services/Auth/TokenCleanupService.cs` — IHostedService background timer
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs:114-172` — register AddMemoryCache, TokenCleanupService

- [ ] **Step 1: Add explicit AddMemoryCache**

In `ServiceCollectionExtensions.cs` inside `AddApplicationServices()`:
```csharp
services.AddMemoryCache();
```

- [ ] **Step 2: Refactor RateLimitService to IMemoryCache**

In `TelegramGroupsAdmin/Services/Auth/RateLimitService.cs`:

Replace `ConcurrentDictionary<string, List<DateTimeOffset>> _attempts` with `IMemoryCache _cache`. Keep the `Lock _lock` for atomic check-count-then-increment. Add `private int _entryCount;` with `Interlocked` operations.

Key pattern for `CheckRateLimitAsync`:
```csharp
using (_lock.EnterScope())
{
    var attempts = _cache.GetOrCreate(key, entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(config.WindowMinutes);
        entry.RegisterPostEvictionCallback((k, v, reason, state) =>
        {
            if (reason != EvictionReason.Replaced)
                Interlocked.Decrement(ref _entryCount);
        });
        Interlocked.Increment(ref _entryCount);
        return new List<DateTimeOffset>();
    })!;
    // ... existing filter + count logic on 'attempts' list
}
```

Update `EntryCount` property: `public int EntryCount => _entryCount;`

- [ ] **Step 3: Create TokenCleanupService**

Create `TelegramGroupsAdmin/Services/Auth/TokenCleanupService.cs`:
```csharp
namespace TelegramGroupsAdmin.Services.Auth;

public class TokenCleanupService(
    IIntermediateAuthService authService,
    IPendingRecoveryCodesService codesService,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                authService.CleanupExpiredEntries();
                codesService.CleanupExpiredEntries();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Token cleanup failed");
            }
        }
    }
}
```

- [ ] **Step 4: Refactor IntermediateAuthService and PendingRecoveryCodesService**

In both services:
- Keep `ConcurrentDictionary` — do NOT migrate to IMemoryCache
- Remove the `Task.Run` fire-and-forget cleanup from write methods
- Extract cleanup logic into a public `CleanupExpiredEntries()` method (called by TokenCleanupService). Note: `IntermediateAuthService` currently has a private `CleanupExpiredTokens()` — rename to `CleanupExpiredEntries()` for consistency
- **Add `CleanupExpiredEntries()` to both interfaces** (`IIntermediateAuthService` and `IPendingRecoveryCodesService`) — `TokenCleanupService` injects the interfaces, not the concrete classes
- Keep `EntryCount` backed by dictionary `.Count`

- [ ] **Step 5: Register TokenCleanupService**

In `ServiceCollectionExtensions.cs` inside `AddApplicationServices()`:
```csharp
services.AddHostedService<TokenCleanupService>();
```

- [ ] **Step 6: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 7: Commit**

```
git add -A && git commit -m "perf: IMemoryCache for RateLimitService + cleanup timer for token services

Closes #430"
```

---

## Task 6: #431 — EF Core 10 LeftJoin + AsNoTracking Audit

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/MLTrainingDataRepository.cs:34-37,57-60,120-123` — LeftJoin where applicable
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/DetectionResultsRepository.cs:54-96` — WithActorJoins LeftJoin chain
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/MessageHistoryRepository.cs:59-69` — LeftJoin conversion
- Audit: All repository files for missing `AsNoTracking()`

- [ ] **Step 1: Read all three target repositories in full**

Read each file to understand the exact LINQ patterns before modifying:
- `MLTrainingDataRepository.cs` — identify which of the 4 GroupJoin patterns have independent key selectors (convertible) vs correlated subqueries (not convertible)
- `DetectionResultsRepository.cs` — read `WithActorJoins` (~lines 54-96) to map the 4-way join chain
- `MessageHistoryRepository.cs` — read the GroupJoin at lines 59-69

- [ ] **Step 2: Convert applicable patterns to LeftJoin**

For each convertible pattern, replace:
```csharp
// OLD:
.GroupJoin(rightTable, leftKey, rightKey, (left, rights) => new { left, rights })
.SelectMany(x => x.rights.DefaultIfEmpty(), (x, right) => ...)
// NEW:
.LeftJoin(rightTable, leftKey, rightKey, (left, right) => ...)
```

Correlated subquery patterns (e.g., `from mt in context.MessageTranslations.Where(mt => mt.MessageId == m.MessageId)`) stay as `GroupJoin`/`SelectMany`/`DefaultIfEmpty` but convert from query syntax to method syntax if needed.

- [ ] **Step 3: AsNoTracking audit**

Search all repository files for read-only queries missing `AsNoTracking()`:
```
grep -rn "context\.\w\+" --include="*.cs" TelegramGroupsAdmin.ContentDetection/Repositories/ TelegramGroupsAdmin.Telegram/Repositories/ TelegramGroupsAdmin/Repositories/
```

Add `AsNoTracking()` to any read-only query that doesn't have it.

- [ ] **Step 4: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 5: Commit**

```
git add -A && git commit -m "refactor: EF Core 10 LeftJoin where applicable + AsNoTracking audit

Closes #431"
```

---

## Task 7: #432 — VirusTotal Stream-Based JSON + Response Disposal

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs:125,138,312,331,373` — stream-based parsing + using on responses

- [ ] **Step 1: Read VirusTotalScannerService.cs in full**

Catalog all 5 `ReadAsStringAsync()` calls and all `HttpResponseMessage` variables that lack `using`.

- [ ] **Step 2: Replace ReadAsStringAsync with stream-based parsing**

For each call, replace:
```csharp
// OLD:
var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
using var doc = JsonDocument.Parse(jsonContent);
// NEW:
using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
```

For error content reads (used for logging), stream-based is overkill — keep `ReadAsStringAsync` for error paths since those strings go into log messages anyway.

- [ ] **Step 3: Add using declarations to all HttpResponseMessage instances**

Wrap every `response` variable in `using`:
```csharp
using var response = await client.GetAsync(url, cancellationToken);
```

Verify error paths and early returns also dispose correctly.

- [ ] **Step 4: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 5: Commit**

```
git add -A && git commit -m "perf: VirusTotal stream-based JSON + response disposal

Closes #432"
```

---

## Task 8: #433 — Server-Side Take() on Implicit Ham Query

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/Repositories/MLTrainingDataRepository.cs:178-190` — add Take before ToListAsync

- [ ] **Step 1: Read MLTrainingDataRepository.cs lines 170-210**

Understand the implicit ham query structure, the `maxImplicitHam` parameter, and where `.ToListAsync()` is called.

- [ ] **Step 2: Add server-side Take()**

Before `.ToListAsync()` (~line 190), add:
```csharp
.Take(maxImplicitHam * 3)  // Server-side cap; 3x over-fetch for SimHash dedup (~30-50% removal)
```

Remove or update the comment at lines 175-176 about "trivial memory/CPU" since we're now capping.

- [ ] **Step 3: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 4: Commit**

```
git add -A && git commit -m "perf: server-side Take() on implicit ham training query

Closes #433"
```

---

## Task 9: Tier 3 — Merge Classifier Retraining Jobs

**Files:**
- Create: `TelegramGroupsAdmin.BackgroundJobs/Jobs/ClassifierRetrainingJob.cs` — merged job
- Delete: `TelegramGroupsAdmin.BackgroundJobs/Jobs/TextClassifierRetrainingJob.cs`
- Delete: `TelegramGroupsAdmin.BackgroundJobs/Jobs/BayesClassifierRetrainingJob.cs`
- Modify: `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs` — update/add job name constant
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/TrainingHandler.cs:110-119` — single TriggerNowAsync
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/BackgroundJobConfigService.cs:350-357` — update default config entry
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzSchedulingSyncService.cs:176-177` — update misfire special-casing
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs:95-96` — update Quartz AddJob registrations
- Modify: Test files referencing old job names (e.g., `TrainingHandlerTests.cs`)

- [ ] **Step 1: Read both existing job files**

Read `TextClassifierRetrainingJob.cs` (lines 34-73) and `BayesClassifierRetrainingJob.cs` (lines 31-69) to understand what each does: dependencies injected, services called, metrics recorded, error handling.

- [ ] **Step 2: Create merged ClassifierRetrainingJob**

Create `TelegramGroupsAdmin.BackgroundJobs/Jobs/ClassifierRetrainingJob.cs`:

Combine both jobs' logic:
1. Inject both `IMLTextClassifierService` and `IBayesClassifierService`
2. In `Execute()`: Train SDCA first → record metrics → Train Bayes → record metrics
3. Use `[DisallowConcurrentExecution]` attribute
4. Wrap each training call in try/catch so one failure doesn't block the other

- [ ] **Step 3: Update BackgroundJobNames**

In `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs`:
- Add: `public const string ClassifierRetraining = "ClassifierRetrainingJob";`
- Keep old constants temporarily if referenced by database records (schedule configs)

- [ ] **Step 4: Update TrainingHandler**

In `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/TrainingHandler.cs`:
Replace the two `TriggerNowAsync` calls (lines 110-119) with one:
```csharp
await _jobTriggerService.TriggerNowAsync(
    BackgroundJobNames.ClassifierRetraining,
    payload: new { },
    cancellationToken: cancellationToken);
```

- [ ] **Step 5: Update Quartz job registration and scheduling**

Search for references to `TextClassifierRetrainingJob` and `BayesClassifierRetrainingJob` in:
- Quartz job registration (DI/service configuration)
- `QuartzSchedulingSyncService.cs` or seed data
- Database seed configs for background job schedules

Update to reference the new `ClassifierRetrainingJob`.

- [ ] **Step 6: Delete old job files**

Delete:
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/TextClassifierRetrainingJob.cs`
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/BayesClassifierRetrainingJob.cs`

- [ ] **Step 7: Search for any remaining references to old job names**

```
grep -rn "TextClassifierRetraining\|BayesClassifierRetraining" --include="*.cs"
```

Update any remaining references.

- [ ] **Step 8: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 9: Commit**

```
git add -A && git commit -m "perf: merge classifier retraining into single job"
```

---

## Task 10: #424 — FrozenDictionary + Pre-Computed Aggregates for Bayes

**Files:**
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs:47-109` — FrozenDictionary fields, pre-computed aggregates
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifierService.cs:39-128` — build FrozenDictionary at training time

- [ ] **Step 1: Read BayesClassifier.cs and BayesClassifierService.cs in full**

Understand:
- How `_spamWordCounts` / `_hamWordCounts` are populated (training)
- How they're consumed in `ClassifyMessage` (lines 47-109)
- The `BayesModelContainer` and atomic swap pattern
- `GetMetadata()` and what `MemoryMetrics` reads from it

- [ ] **Step 2: Add pre-computed aggregate fields to BayesClassifier**

In `BayesClassifier.cs`, add fields:
```csharp
private readonly FrozenDictionary<string, int> _spamWordCounts;
private readonly FrozenDictionary<string, int> _hamWordCounts;
private readonly int _vocabularySize;
private readonly long _spamWordTotal;
private readonly long _hamWordTotal;
private readonly double _laplaceDenominatorSpam;
private readonly double _laplaceDenominatorHam;
```

Set these in the constructor or a `SetTrainingData` method called after training completes:
```csharp
_spamWordCounts = spamCounts.ToFrozenDictionary();
_hamWordCounts = hamCounts.ToFrozenDictionary();
_vocabularySize = _spamWordCounts.Keys.Union(_hamWordCounts.Keys).Count(); // computed once
_spamWordTotal = _spamWordCounts.Values.Sum();
_hamWordTotal = _hamWordCounts.Values.Sum();
_laplaceDenominatorSpam = _spamWordTotal + _vocabularySize;
_laplaceDenominatorHam = _hamWordTotal + _vocabularySize;
```

Add `using System.Collections.Frozen;`

- [ ] **Step 3: Update ClassifyMessage to use pre-computed values**

Replace the per-call computations:
```csharp
// OLD (lines 69-71):
// var spamWordTotal = _spamWordCounts.Values.Sum();
// var hamWordTotal = _hamWordCounts.Values.Sum();
// var vocabularySize = _spamWordCounts.Keys.Union(_hamWordCounts.Keys).Count();
// NEW: use _spamWordTotal, _hamWordTotal, _vocabularySize, _laplaceDenominatorSpam, _laplaceDenominatorHam directly
```

- [ ] **Step 4: Update BayesClassifierService.TrainAsync**

Ensure `TrainAsync` builds the `BayesClassifier` with `Dictionary<string, int>` during training, then converts to `FrozenDictionary` before the atomic swap.

- [ ] **Step 5: Verify MemoryMetrics compatibility**

Check that `IBayesClassifierService.GetMetadata()` still returns `SpamVocabularySize` and `HamVocabularySize` correctly. These should now come from `_spamWordCounts.Count` and `_hamWordCounts.Count` on the frozen dictionaries.

- [ ] **Step 6: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 7: Commit**

```
git add -A && git commit -m "perf: FrozenDictionary + pre-computed aggregates for Bayes

Closes #424"
```

---

## Task 11: #426 — RecyclableMemoryStream for Image Processing

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj` — add PackageReference for RecyclableMemoryStream
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs:651,669,696,711,737-753` — inject pool, replace MemoryStream + ToArray
- Modify: `TelegramGroupsAdmin.Telegram/Services/Telegram/TelegramImageService.cs:34` — inject pool, return RecyclableMemoryStream

- [ ] **Step 1: Add package reference**

Run: `dotnet add TelegramGroupsAdmin.Telegram package Microsoft.IO.RecyclableMemoryStream`

- [ ] **Step 2: Update TelegramImageService**

In `TelegramImageService.cs`:
- Inject `RecyclableMemoryStreamManager` via constructor
- At line 34, replace `new MemoryStream()` with `_streamManager.GetStream("TelegramImageService.Download")`

- [ ] **Step 3: Update ProfileScanService**

In `ProfileScanService.cs`:
- Inject `RecyclableMemoryStreamManager` via constructor
- At lines 651, 669, 696, 711: replace `ms.ToArray()` with passing the stream directly to `Image.Load(ms)` (reset stream position first: `ms.Position = 0`)
- Update `ResizeForVisionAsync` signature from `byte[]` to `Stream`:
```csharp
private static async Task<byte[]> ResizeForVisionAsync(Stream imageStream, int maxDimension = 1024)
{
    imageStream.Position = 0;
    using var image = await Image.LoadAsync(imageStream);
    // ... resize logic stays the same
    using var output = new MemoryStream(); // output is small (JPEG compressed), regular MemoryStream is fine
    await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 });
    return output.ToArray();
}
```
- Dispose the `RecyclableMemoryStream` immediately after `Image.Load()` returns

- [ ] **Step 4: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 5: Commit**

```
git add -A && git commit -m "perf: RecyclableMemoryStream for image processing

Closes #426"
```

---

## CHECKPOINT: Run Full Test Suite

After Wave 1 + Wave 2 are complete (Tasks 1-11):

```
dotnet test --logger "console;verbosity=detailed" > /tmp/test-results-wave2.txt 2>&1
```

Review results before proceeding to Wave 3.

---

## Task 12: #434 — Chunked AEAD Streaming Encryption

**Files:**
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Constants/EncryptionConstants.cs` — add chunked format constants
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/IBackupEncryptionService.cs` — add Stream overloads
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupEncryptionService.cs` — implement chunked encrypt/decrypt, dual-read routing
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/Backup/BackupService.cs` — use Stream-based encryption

- [ ] **Step 1: Add constants for chunked format**

In `EncryptionConstants.cs`, add:
```csharp
public const int ChunkSize = 1024 * 1024; // 1 MB
public static readonly byte[] ChunkedMagicHeader = "TGAEC2\0"u8.ToArray(); // 7 bytes
public static readonly byte[] LegacyMagicHeader = "TGAENC\0"u8.ToArray(); // moved from BackupEncryptionService
public const byte ChunkedFormatVersion = 0x01;
// Header layout: Magic(7) + Version(1) + Salt(32) + BaseNonce(12) = 52 bytes
public const int ChunkedHeaderSize = 7 + 1 + SaltSizeBytes + NonceSizeBytes; // 52
```

Also remove the existing `private static readonly byte[] MagicHeader` field from `BackupEncryptionService.cs` (line 16) and update all references to use `EncryptionConstants.LegacyMagicHeader` instead.

- [ ] **Step 2: Update IBackupEncryptionService interface**

Add Stream-based overloads:
```csharp
void EncryptBackup(Stream plaintext, Stream cipherOutput, string passphrase);
void DecryptBackup(Stream cipherInput, Stream plainOutput, string passphrase);
bool IsEncrypted(Stream input); // reads header bytes only, resets position
```

Keep existing `byte[]` overloads — `DecryptBackup(byte[])` becomes `DecryptLegacy` internally.

- [ ] **Step 3: Implement chunked encryption**

In `BackupEncryptionService.cs`, implement `EncryptBackup(Stream, Stream, string)`:

1. Generate fresh salt + base nonce via `RandomNumberGenerator.GetBytes`
2. Derive key once via PBKDF2
3. Write header: `TGAEC2\0` + version + salt + base nonce
4. Loop: read 1 MB chunks from plaintext stream
   - Derive per-chunk nonce: XOR 64-bit big-endian counter into last 8 bytes of base nonce
   - Write chunk length (4 bytes, big-endian)
   - Encrypt chunk with AES-GCM → write ciphertext + tag
5. Write sentinel: chunk length = 0

**CRITICAL:** Base nonce MUST be fresh `RandomNumberGenerator.GetBytes` — never cached.

- [ ] **Step 4: Implement chunked decryption**

Implement `DecryptBackup(Stream, Stream, string)`:

1. Read header: verify `TGAEC2\0` magic + version
2. Extract salt + base nonce, derive key via PBKDF2
3. Loop: read chunk length
   - If length = 0 → sentinel, done
   - Derive per-chunk nonce (same XOR scheme)
   - Read ciphertext + tag, decrypt with AES-GCM → write plaintext to output

- [ ] **Step 5: Implement dual-read format detection**

Update `IsEncrypted(Stream)`:
```csharp
public bool IsEncrypted(Stream input)
{
    var pos = input.Position;
    Span<byte> header = stackalloc byte[7];
    var read = input.ReadAtLeast(header, 7, throwOnEndOfStream: false);
    input.Position = pos;
    if (read < 7) return false;
    return header.SequenceEqual(EncryptionConstants.ChunkedMagicHeader)
        || header.SequenceEqual(EncryptionConstants.LegacyMagicHeader);
}
```

Route `DecryptBackup(Stream, Stream, string)` to chunked or legacy based on magic bytes.

- [ ] **Step 6: Update BackupService to use Stream-based encryption**

In `BackupService.cs`, replace the `byte[]` encryption flow:
```csharp
// OLD:
// var databaseJson = jsonStream.ToArray();
// var databaseContent = _encryptionService.EncryptBackup(databaseJson, passphrase);
// NEW:
jsonStream.Position = 0;
using var encryptedStream = _streamManager.GetStream("BackupService.Encrypt");
_encryptionService.EncryptBackup(jsonStream, encryptedStream, passphrase);
encryptedStream.Position = 0;
// Write encryptedStream to tar entry
```

Update the restore path similarly to use Stream-based decryption.

- [ ] **Step 7: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 8: Commit**

```
git add -A && git commit -m "perf: chunked AEAD streaming encryption for backups

Closes #434"
```

---

## Task 13: #435 — Span-Based Tokenizer with FrozenDictionary.AlternateLookup

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Utilities/TextTokenizer.cs:113-148` — EnumerateMatches, span-based extraction
- Modify: `TelegramGroupsAdmin.ContentDetection/ML/BayesClassifier.cs:47-109` — AlternateLookup for dictionary probes

- [ ] **Step 1: Read TextTokenizer.cs ExtractWords in full**

Read lines 113-148. Understand the full pipeline: emoji removal → lowercase → regex matches → filter by length/stop words/numbers → return string[].

Investigate allocation reduction opportunities:
- Can `ToLowerInvariant()` be avoided with case-insensitive FrozenDictionary?
- Can `RemoveEmojis()` be skipped if emoji tokens aren't in vocabulary?
- Can `Distinct()` be eliminated?

- [ ] **Step 2: Replace Regex.Matches with EnumerateMatches**

In `TextTokenizer.cs` `ExtractWords` method:
```csharp
// OLD:
// var matches = WordBoundaryExtractor().Matches(text);
// foreach (Match match in matches) { words.Add(match.Value); }
// NEW:
foreach (var valueMatch in WordBoundaryExtractor().EnumerateMatches(text))
{
    var word = text.AsSpan(valueMatch.Index, valueMatch.Length);
    // ... filter by length, stop words, numbers using spans
}
```

Return type may need to change depending on how callers consume the result. If `BayesClassifier.ClassifyMessage` is the primary hot-path consumer, consider a span-yielding API alongside the existing string[] API.

- [ ] **Step 3: Add AlternateLookup to BayesClassifier**

In `BayesClassifier.cs` `ClassifyMessage`:
```csharp
var spamLookup = _spamWordCounts.GetAlternateLookup<ReadOnlySpan<char>>();
var hamLookup = _hamWordCounts.GetAlternateLookup<ReadOnlySpan<char>>();

// Then in the loop:
spamLookup.TryGetValue(wordSpan, out var spamCount);
hamLookup.TryGetValue(wordSpan, out var hamCount);
```

Words added to `significantWords` must be materialized: `significantWords.Add(wordSpan.ToString());`

- [ ] **Step 4: Investigate and apply allocation reduction opportunities**

Based on Step 1 findings:
- If `StringComparer.OrdinalIgnoreCase` works with `AlternateLookup`, build FrozenDictionary with that comparer and skip `ToLowerInvariant()`
- Test whether emoji tokens exist in vocabulary — if not, skip `RemoveEmojis()`
- Consider handling duplicates mathematically in the Bayes loop instead of calling `Distinct()`

Document what was tried and what worked in the commit message.

- [ ] **Step 5: Build + startup validation**

Run: `dotnet build`
Run: `dotnet run` → wait for startup → SIGTERM

- [ ] **Step 6: Commit**

```
git add -A && git commit -m "perf: span-based tokenizer with FrozenDictionary.AlternateLookup

Closes #435"
```

---

## Final: Run Full Test Suite + PR

- [ ] **Step 1: Run full test suite**

```
dotnet test --logger "console;verbosity=detailed" > /tmp/test-results-final.txt 2>&1
```

Fix any failures.

- [ ] **Step 2: Open tracking issue for legacy decryption removal**

Create GitHub issue: "chore: remove DecryptLegacy path from BackupEncryptionService" with body explaining it's safe to remove after all existing backups have been re-encrypted in the new TGAEC2 format.

- [ ] **Step 3: Create PR**

```
gh pr create --base develop --title "perf: memory tuning phase 2" --body "$(cat <<'EOF'
## Summary

Closes #424, Closes #425, Closes #426, Closes #427, Closes #428, Closes #429, Closes #430, Closes #431, Closes #432, Closes #433, Closes #434, Closes #435

Memory tuning phase 2 addressing root causes identified in 18-hour Prometheus/Grafana investigation.

### Wave 1 — Independent Fixes
- RecyclableMemoryStream for backup pipeline + streaming download endpoint
- MLContext elimination + PredictionEnginePool for thread-safe prediction
- LoggerMessage source generators for hot-path logging
- IHttpClientFactory via InternalApiClient typed wrapper
- IMemoryCache for RateLimitService + cleanup timer for token services
- EF Core 10 LeftJoin where applicable + AsNoTracking audit
- VirusTotal stream-based JSON + response disposal
- Server-side Take() on implicit ham training query
- Merged classifier retraining into single job

### Wave 2 — Depends on Wave 1
- FrozenDictionary + pre-computed aggregates for Bayes classifier
- RecyclableMemoryStream for image processing

### Wave 3 — Breaking Changes
- Chunked AEAD streaming encryption (TGAEC2 format, dual-read with legacy)
- Span-based tokenizer with FrozenDictionary.AlternateLookup

## Verification Targets (Observable on Grafana)
| Metric | Current | Target |
|--------|---------|--------|
| RSS range | 810-1272 MiB | 800-1000 MiB |
| LOH fragmentation | 38% | <15% |
| Backup peak memory | ~500 MB | ~2 MB |

## Test plan
- [ ] Full test suite passes
- [ ] `dotnet run` + SIGTERM clean startup after each commit
- [ ] Backup export + restore round-trip with new chunked format
- [ ] Legacy backup restore still works (dual-read)
- [ ] Verify Grafana metrics after deploy

Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
