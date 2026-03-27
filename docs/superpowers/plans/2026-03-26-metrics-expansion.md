# Metrics Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `/metrics` instrumentation from ~19 to ~58 custom instruments using domain-scoped metrics classes with consistent `tga.*` naming.

**Architecture:** 9 singleton metrics classes distributed across their respective projects, each owning a `Meter` and exposing wrapper methods that enforce consistent tagging. Existing flat metrics in `TelemetryConstants` are migrated to the new classes, then removed.

**Tech Stack:** System.Diagnostics.Metrics, OpenTelemetry with Prometheus exporter, .NET 10

**Spec:** `docs/superpowers/specs/2026-03-26-metrics-expansion-design.md`

**Branch:** `feat/metrics-expansion` (already created, branched from `develop`)

**Important:** This project has NO unit tests for metrics classes (spec decision — they're thin wrappers around SDK primitives). Validation is done by running `dotnet build` and verifying the `/metrics` endpoint on prod after deployment. Run `dotnet run --migrate-only` to validate the app boots without errors after DI changes.

---

## File Structure

### New Files

| File | Project | Responsibility |
|---|---|---|
| `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs` | Core | OpenAI, VirusTotal, SendGrid, Telegram API metrics |
| `TelegramGroupsAdmin.Core/Metrics/CacheMetrics.cs` | Core | Cache hit/miss/removal counters |
| `TelegramGroupsAdmin.ContentDetection/Metrics/DetectionMetrics.cs` | ContentDetection | Spam detection, file scan, veto metrics |
| `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs` | Telegram | Message processing, moderation, profile scan metrics |
| `TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs` | Telegram | Managed chats, health checks, joins/leaves |
| `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs` | Telegram | Welcome flow outcomes, security checks |
| `TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs` | Telegram | Report creation, resolution, pending count |
| `TelegramGroupsAdmin.BackgroundJobs/Metrics/JobMetrics.cs` | BackgroundJobs | Job execution counts, duration, rows affected |

### Renamed Files

| From | To |
|---|---|
| `TelegramGroupsAdmin/Services/MemoryInstrumentation.cs` | `TelegramGroupsAdmin/Services/MemoryMetrics.cs` |

### Modified Files

| File | Change |
|---|---|
| `TelegramGroupsAdmin.Core/TelemetryConstants.cs` | Remove counters/histograms/meters, keep ActivitySources only |
| `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs` | Register `ApiMetrics`, `CacheMetrics` |
| `TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs` | Register `DetectionMetrics` |
| `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` | Register `PipelineMetrics`, `ChatMetrics`, `WelcomeMetrics`, `ReportMetrics` |
| `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs` | Register `JobMetrics` |
| `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` | Replace `MemoryInstrumentation` with `MemoryMetrics` |
| `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs` | Switch to `DetectionMetrics` |
| `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` | Switch to `DetectionMetrics` |
| `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs` | Add `DetectionMetrics` + `ApiMetrics` |
| `TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs` | Add `ApiMetrics` |
| `TelegramGroupsAdmin/Services/Email/SendGridEmailService.cs` | Add `ApiMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/ChatCache.cs` | Add `CacheMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs` | Add `CacheMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs` | Add `PipelineMetrics`, `ChatMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/DetectionActionService.cs` | Add `PipelineMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` | Add `PipelineMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs` | Add `PipelineMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/ChatHealthRefreshOrchestrator.cs` | Add `ChatMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` | Add `WelcomeMetrics`, `ChatMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/ReportService.cs` | Add `ReportMetrics` |
| `TelegramGroupsAdmin.Telegram/Services/ReportActions/ReportActionsService.cs` | Add `ReportMetrics` |
| `TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs` | Add `WelcomeMetrics` |
| All 17 `*Job.cs` files in BackgroundJobs/Jobs/ | Switch to `JobMetrics` |

---

## Task 1: DetectionMetrics — Create Class and Migrate Existing Metrics

This is the best starting task because it replaces existing metrics (proving the pattern works) with minimal risk.

**Files:**
- Create: `TelegramGroupsAdmin.ContentDetection/Metrics/DetectionMetrics.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs`

- [ ] **Step 1: Create `DetectionMetrics.cs`**

Create `TelegramGroupsAdmin.ContentDetection/Metrics/DetectionMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace TelegramGroupsAdmin.ContentDetection.Metrics;

/// <summary>
/// Metrics for spam detection, file scanning, and OpenAI veto tracking.
/// Replaces flat counters/histograms from TelemetryConstants.
/// </summary>
public sealed class DetectionMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Detection");

    private readonly Counter<long> _spamTotal;
    private readonly Histogram<double> _algorithmDuration;
    private readonly Counter<long> _fileScanTotal;
    private readonly Histogram<double> _fileScanDuration;
    private readonly Counter<long> _vetoTotal;

    public DetectionMetrics()
    {
        _spamTotal = _meter.CreateCounter<long>(
            "tga.detection.spam_total",
            description: "Per-algorithm spam detection counts");

        _algorithmDuration = _meter.CreateHistogram<double>(
            "tga.detection.algorithm.duration",
            unit: "ms",
            description: "Per-algorithm execution time");

        _fileScanTotal = _meter.CreateCounter<long>(
            "tga.detection.file_scan_total",
            description: "File scan results by tier and outcome");

        _fileScanDuration = _meter.CreateHistogram<double>(
            "tga.detection.file_scan.duration",
            unit: "ms",
            description: "File scan latency by tier");

        _vetoTotal = _meter.CreateCounter<long>(
            "tga.detection.veto_total",
            description: "OpenAI veto count per algorithm");
    }

    public void RecordSpamDetection(string algorithm, string result, double durationMs)
    {
        var tags = new TagList
        {
            { "algorithm", algorithm },
            { "result", result }
        };
        _spamTotal.Add(1, tags);
        _algorithmDuration.Record(durationMs, new TagList { { "algorithm", algorithm } });
    }

    public void RecordFileScan(string tier, bool isMalicious, double durationMs)
    {
        var tags = new TagList
        {
            { "tier", tier },
            { "result", isMalicious ? "malicious" : "clean" }
        };
        _fileScanTotal.Add(1, tags);
        _fileScanDuration.Record(durationMs, new TagList { { "tier", tier } });
    }

    public void RecordVeto(string algorithm)
    {
        _vetoTotal.Add(1, new TagList { { "algorithm", algorithm } });
    }
}
```

- [ ] **Step 2: Register in DI**

In `TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs`, add after the existing singleton registrations (after line ~35):

```csharp
using TelegramGroupsAdmin.ContentDetection.Metrics;
// ...
services.AddSingleton<DetectionMetrics>();
```

- [ ] **Step 3: Migrate ContentDetectionEngineV2**

In `TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs`:
- Add `DetectionMetrics` to the constructor parameters
- In `RecordDetectionMetrics()` (lines 493-514), replace `TelemetryConstants.SpamDetections.Add(...)` and `TelemetryConstants.SpamDetectionDuration.Record(...)` with calls to `_detectionMetrics.RecordSpamDetection(algorithm, result, durationMs)`
- Drop the `version` tag (intentionally removed per spec)
- Change `result` tag: collapse `"low_confidence"` into `"clean"`

- [ ] **Step 4: Migrate ClamAVScannerService**

In `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs`:
- Add `DetectionMetrics` to the constructor parameters
- In `RecordScanMetrics()` (lines 320-345), replace `TelemetryConstants.FileScanResults.Add(...)` and `TelemetryConstants.FileScanDuration.Record(...)` with calls to `_detectionMetrics.RecordFileScan("clamav", isMalicious, durationMs)`

- [ ] **Step 5: Add Tier 2 metrics to VirusTotalScannerService**

In `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs`:
- Add `DetectionMetrics` to the constructor parameters
- After hash lookup completes (around lines 80-100), add `_detectionMetrics.RecordFileScan("virustotal", isMalicious, durationMs)`
- After file upload completes (around lines 220-240), add `_detectionMetrics.RecordFileScan("virustotal", isMalicious, durationMs)`

- [ ] **Step 6: Build and verify**

```bash
dotnet build TelegramGroupsAdmin.ContentDetection
dotnet build  # Full solution
```

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.ContentDetection/Metrics/DetectionMetrics.cs \
       TelegramGroupsAdmin.ContentDetection/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.ContentDetection/Services/ContentDetectionEngineV2.cs \
       TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs \
       TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs
git commit -m "feat: add DetectionMetrics and migrate spam/file scan instrumentation

Replaces flat TelemetryConstants counters with domain-scoped
DetectionMetrics class. Adds VirusTotal Tier 2 scan metrics.
Drops version tag, collapses low_confidence into clean."
```

---

## Task 2: JobMetrics — Create Class and Migrate All 17 Jobs

**Files:**
- Create: `TelegramGroupsAdmin.BackgroundJobs/Metrics/JobMetrics.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs`
- Modify: All 17 `*Job.cs` files in `TelegramGroupsAdmin.BackgroundJobs/Jobs/`

- [ ] **Step 1: Create `JobMetrics.cs`**

Create `TelegramGroupsAdmin.BackgroundJobs/Metrics/JobMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace TelegramGroupsAdmin.BackgroundJobs.Metrics;

/// <summary>
/// Metrics for Quartz.NET background job execution.
/// Replaces flat counters from TelemetryConstants.
/// </summary>
public sealed class JobMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Jobs");

    private readonly Counter<long> _executionsTotal;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _rowsAffectedTotal;

    public JobMetrics()
    {
        _executionsTotal = _meter.CreateCounter<long>(
            "tga.jobs.executions_total",
            description: "Job execution counts by name and status");

        _duration = _meter.CreateHistogram<double>(
            "tga.jobs.duration",
            unit: "ms",
            description: "Job execution time by name");

        _rowsAffectedTotal = _meter.CreateCounter<long>(
            "tga.jobs.rows_affected_total",
            description: "Rows deleted or processed by cleanup jobs");
    }

    public void RecordJobExecution(string jobName, bool success, double durationMs)
    {
        var tags = new TagList
        {
            { "job_name", jobName },
            { "status", success ? "success" : "failure" }
        };
        _executionsTotal.Add(1, tags);
        _duration.Record(durationMs, new TagList { { "job_name", jobName } });
    }

    public void RecordRowsAffected(string jobName, long count)
    {
        if (count > 0)
            _rowsAffectedTotal.Add(count, new TagList { { "job_name", jobName } });
    }
}
```

- [ ] **Step 2: Register in DI**

In `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs`, add after existing singleton registrations (after line ~32):

```csharp
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
// ...
services.AddSingleton<JobMetrics>();
```

- [ ] **Step 3: Migrate all 17 job files**

For each job file in `TelegramGroupsAdmin.BackgroundJobs/Jobs/`:

1. Add `JobMetrics jobMetrics` to the constructor (primary constructor or injected field)
2. Replace the `finally` block pattern:

**Before:**
```csharp
finally
{
    var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
    var tags = new TagList
    {
        { "job_name", jobName },
        { "status", success ? "success" : "failure" }
    };
    TelemetryConstants.JobExecutions.Add(1, tags);
    TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
}
```

**After:**
```csharp
finally
{
    var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
    jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
}
```

3. Remove `using TelegramGroupsAdmin.Core.Telemetry;` if no other TelemetryConstants references remain

The 17 job files to update:
- `BayesClassifierRetrainingJob.cs`
- `BlocklistSyncJob.cs`
- `ChatHealthCheckJob.cs`
- `DatabaseMaintenanceJob.cs`
- `DataCleanupJob.cs`
- `DeleteMessageJob.cs`
- `DeleteUserMessagesJob.cs`
- `FetchUserPhotoJob.cs`
- `FileScanJob.cs`
- `ProfileRescanJob.cs`
- `ProfileScanJob.cs`
- `RefreshUserPhotosJob.cs`
- `RotateBackupPassphraseJob.cs`
- `ScheduledBackupJob.cs`
- `TempbanExpiryJob.cs`
- `TextClassifierRetrainingJob.cs`
- `WelcomeTimeoutJob.cs`

For cleanup jobs (`DataCleanupJob`, `DatabaseMaintenanceJob`, `DeleteMessageJob`, `DeleteUserMessagesJob`), also add `jobMetrics.RecordRowsAffected(jobName, rowCount)` where row counts are available.

- [ ] **Step 4: Build and verify**

```bash
dotnet build TelegramGroupsAdmin.BackgroundJobs
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.BackgroundJobs/
git commit -m "feat: add JobMetrics and migrate all 17 background jobs

Replaces TelemetryConstants.JobExecutions/JobDuration with
domain-scoped JobMetrics class. Adds rows_affected tracking
for cleanup jobs."
```

---

## Task 3: Clean Up TelemetryConstants and Rename MemoryInstrumentation

Now that DetectionMetrics and JobMetrics have migrated all call sites, clean up the old definitions.

**Files:**
- Modify: `TelegramGroupsAdmin.Core/TelemetryConstants.cs`
- Rename: `TelegramGroupsAdmin/Services/MemoryInstrumentation.cs` → `TelegramGroupsAdmin/Services/MemoryMetrics.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Strip TelemetryConstants down to ActivitySources only**

In `TelegramGroupsAdmin.Core/TelemetryConstants.cs`, remove:
- Lines 42-113: The `Metrics` meter, `Memory` meter, all 3 counters, all 3 histograms
- Keep lines 1-38: The 4 ActivitySource fields (`SpamDetection`, `FileScanning`, `MessageProcessing`, `BackgroundJobs`)
- Remove `using System.Diagnostics.Metrics;` if no longer needed

After cleanup, file should contain only:
```csharp
using System.Diagnostics;

namespace TelegramGroupsAdmin.Core.Telemetry;

public static class TelemetryConstants
{
    public static readonly ActivitySource SpamDetection = new("TelegramGroupsAdmin.SpamDetection");
    public static readonly ActivitySource FileScanning = new("TelegramGroupsAdmin.FileScanning");
    public static readonly ActivitySource MessageProcessing = new("TelegramGroupsAdmin.MessageProcessing");
    public static readonly ActivitySource BackgroundJobs = new("TelegramGroupsAdmin.BackgroundJobs");
}
```

- [ ] **Step 2: Rename MemoryInstrumentation to MemoryMetrics**

Rename file `TelegramGroupsAdmin/Services/MemoryInstrumentation.cs` → `TelegramGroupsAdmin/Services/MemoryMetrics.cs`

Update the class:
- Rename class from `MemoryInstrumentation` to `MemoryMetrics`
- Replace `var meter = TelemetryConstants.Memory;` with `var meter = new Meter("TelegramGroupsAdmin.Memory");`
- Add `using System.Diagnostics.Metrics;`
- Remove the `TelemetryConstants` import if no longer needed

- [ ] **Step 3: Update DI registration**

In `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`, change:
```csharp
services.AddSingleton<MemoryInstrumentation>();
```
to:
```csharp
services.AddSingleton<MemoryMetrics>();
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build
dotnet run --migrate-only  # Verify DI wiring
```

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/TelemetryConstants.cs \
       TelegramGroupsAdmin/Services/MemoryMetrics.cs \
       TelegramGroupsAdmin/ServiceCollectionExtensions.cs
git rm TelegramGroupsAdmin/Services/MemoryInstrumentation.cs
git commit -m "refactor: strip TelemetryConstants to ActivitySources, rename MemoryInstrumentation

TelemetryConstants now contains only ActivitySource fields.
MemoryInstrumentation renamed to MemoryMetrics with its own Meter."
```

---

## Task 4: ApiMetrics — Create Class and Instrument External APIs

**Files:**
- Create: `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs`
- Modify: `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs`
- Modify: `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs`
- Modify: `TelegramGroupsAdmin/Services/Email/SendGridEmailService.cs`

- [ ] **Step 1: Create `ApiMetrics.cs`**

Create `TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace TelegramGroupsAdmin.Core.Metrics;

/// <summary>
/// Metrics for external API calls: OpenAI, VirusTotal, SendGrid, Telegram Bot API.
/// Per-feature attribution via required tags on recording methods.
/// </summary>
public sealed class ApiMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Api");

    private readonly Counter<long> _openAiCallsTotal;
    private readonly Histogram<double> _openAiLatency;
    private readonly Counter<long> _openAiTokensTotal;
    private readonly Counter<long> _virusTotalCallsTotal;
    private readonly Histogram<double> _virusTotalLatency;
    private readonly Counter<long> _virusTotalQuotaExhaustedTotal;
    private readonly Counter<long> _sendGridSendsTotal;
    private readonly Counter<long> _telegramCallsTotal;
    private readonly Counter<long> _telegramErrorsTotal;

    public ApiMetrics()
    {
        _openAiCallsTotal = _meter.CreateCounter<long>(
            "tga.api.openai.calls_total",
            description: "OpenAI API call count per feature");

        _openAiLatency = _meter.CreateHistogram<double>(
            "tga.api.openai.latency",
            unit: "ms",
            description: "OpenAI API latency distribution");

        _openAiTokensTotal = _meter.CreateCounter<long>(
            "tga.api.openai.tokens_total",
            description: "OpenAI token consumption per feature");

        _virusTotalCallsTotal = _meter.CreateCounter<long>(
            "tga.api.virustotal.calls_total",
            description: "VirusTotal API calls by operation");

        _virusTotalLatency = _meter.CreateHistogram<double>(
            "tga.api.virustotal.latency",
            unit: "ms",
            description: "VirusTotal API latency per operation");

        _virusTotalQuotaExhaustedTotal = _meter.CreateCounter<long>(
            "tga.api.virustotal.quota_exhausted_total",
            description: "VirusTotal daily quota exhaustion events");

        _sendGridSendsTotal = _meter.CreateCounter<long>(
            "tga.api.sendgrid.sends_total",
            description: "SendGrid email sends by template and status");

        _telegramCallsTotal = _meter.CreateCounter<long>(
            "tga.api.telegram.calls_total",
            description: "Telegram Bot API calls by operation");

        _telegramErrorsTotal = _meter.CreateCounter<long>(
            "tga.api.telegram.errors_total",
            description: "Telegram Bot API error breakdown");
    }

    public void RecordOpenAiCall(string feature, string model, int promptTokens, int completionTokens, double durationMs, bool success)
    {
        _openAiCallsTotal.Add(1, new TagList
        {
            { "feature", feature },
            { "model", model },
            { "status", success ? "success" : "failure" }
        });

        _openAiLatency.Record(durationMs, new TagList
        {
            { "feature", feature },
            { "model", model }
        });

        if (promptTokens > 0)
            _openAiTokensTotal.Add(promptTokens, new TagList
            {
                { "feature", feature },
                { "model", model },
                { "type", "prompt" }
            });

        if (completionTokens > 0)
            _openAiTokensTotal.Add(completionTokens, new TagList
            {
                { "feature", feature },
                { "model", model },
                { "type", "completion" }
            });
    }

    public void RecordVirusTotalCall(string operation, double durationMs, bool success)
    {
        _virusTotalCallsTotal.Add(1, new TagList
        {
            { "operation", operation },
            { "status", success ? "success" : "failure" }
        });
        _virusTotalLatency.Record(durationMs, new TagList { { "operation", operation } });
    }

    public void RecordVirusTotalQuotaExhausted()
    {
        _virusTotalQuotaExhaustedTotal.Add(1);
    }

    public void RecordSendGridSend(string template, bool success)
    {
        _sendGridSendsTotal.Add(1, new TagList
        {
            { "template", template },
            { "status", success ? "success" : "failure" }
        });
    }

    public void RecordTelegramApiCall(string operation, bool success)
    {
        _telegramCallsTotal.Add(1, new TagList
        {
            { "operation", operation },
            { "status", success ? "success" : "failure" }
        });
    }

    public void RecordTelegramApiError(string errorType)
    {
        _telegramErrorsTotal.Add(1, new TagList { { "error_type", errorType } });
    }
}
```

- [ ] **Step 2: Register in DI**

In `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`, add:

```csharp
using TelegramGroupsAdmin.Core.Metrics;
// ...
services.AddSingleton<ApiMetrics>();
```

- [ ] **Step 3: Instrument SemanticKernelChatService**

In `TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs`:
- Add `ApiMetrics` to the constructor parameters
- In `GetCompletionAsync()` (line ~72), after `CreateResult()`, add a `Stopwatch` around the completion call and record:
  ```csharp
  apiMetrics.RecordOpenAiCall(feature, modelId, promptTokens, completionTokens, durationMs, success: true);
  ```
- The `feature` parameter needs to be passed through from callers — add a `string feature` parameter to the method or use a constant based on the call context
- Same for `GetVisionCompletionAsync()` methods (lines ~122, ~176)
- In catch blocks, record `success: false`

**Note:** Check how `feature` context flows from callers (e.g., `AIContentCheckV2`, `ImageContentCheckV2`). The feature string should identify the spam detection subsystem calling OpenAI. If callers don't pass this, add a `string feature` parameter to the public API.

- [ ] **Step 4: Instrument VirusTotalScannerService**

In `TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs`:
- Add `ApiMetrics` to the constructor parameters
- At quota exhaustion points (lines 62, 247), add `apiMetrics.RecordVirusTotalQuotaExhausted()`
- Wrap hash lookup calls with stopwatch, add `apiMetrics.RecordVirusTotalCall("hash_lookup", durationMs, success)`
- Wrap file upload calls with stopwatch, add `apiMetrics.RecordVirusTotalCall("file_upload", durationMs, success)`

- [ ] **Step 5: Instrument SendGridEmailService**

In `TelegramGroupsAdmin/Services/Email/SendGridEmailService.cs`:
- Add `ApiMetrics` to the constructor parameters
- After `client.SendEmailAsync()` (line ~75), add:
  ```csharp
  apiMetrics.RecordSendGridSend("raw", response.IsSuccessStatusCode);
  ```
- In `SendTemplatedEmailAsync()` if it exists, use the template name instead of `"raw"`

- [ ] **Step 6: Build and verify**

```bash
dotnet build
dotnet run --migrate-only
```

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Core/Metrics/ApiMetrics.cs \
       TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs \
       TelegramGroupsAdmin.ContentDetection/Services/VirusTotalScannerService.cs \
       TelegramGroupsAdmin/Services/Email/SendGridEmailService.cs
git commit -m "feat: add ApiMetrics for OpenAI, VirusTotal, and SendGrid instrumentation

Per-feature attribution for all external API calls including
token consumption, latency histograms, and quota exhaustion."
```

---

## Task 5: CacheMetrics — Create Class and Instrument Caches

**Files:**
- Create: `TelegramGroupsAdmin.Core/Metrics/CacheMetrics.cs`
- Modify: `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ChatCache.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs`
- Modify: `TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs`

- [ ] **Step 1: Create `CacheMetrics.cs`**

Create `TelegramGroupsAdmin.Core/Metrics/CacheMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace TelegramGroupsAdmin.Core.Metrics;

/// <summary>
/// Metrics for in-memory cache hit/miss/removal tracking.
/// </summary>
public sealed class CacheMetrics
{
    private readonly Meter _meter = new("TelegramGroupsAdmin.Cache");

    private readonly Counter<long> _hitsTotal;
    private readonly Counter<long> _missesTotal;
    private readonly Counter<long> _removalsTotal;

    public CacheMetrics()
    {
        _hitsTotal = _meter.CreateCounter<long>(
            "tga.cache.hits_total",
            description: "Cache hits by cache name");

        _missesTotal = _meter.CreateCounter<long>(
            "tga.cache.misses_total",
            description: "Cache misses by cache name");

        _removalsTotal = _meter.CreateCounter<long>(
            "tga.cache.removals_total",
            description: "Explicit cache removals by cache name");
    }

    public void RecordHit(string cacheName)
    {
        _hitsTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }

    public void RecordMiss(string cacheName)
    {
        _missesTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }

    public void RecordRemoval(string cacheName)
    {
        _removalsTotal.Add(1, new TagList { { "cache_name", cacheName } });
    }
}
```

- [ ] **Step 2: Register in DI**

In `TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs`, add `services.AddSingleton<CacheMetrics>();`

- [ ] **Step 3: Instrument ChatCache**

In `TelegramGroupsAdmin.Telegram/Services/ChatCache.cs`:
- Add `CacheMetrics` to the constructor parameters
- In `GetChat()` (line ~15): record hit if found, miss if not
- In `RemoveChat()` (line ~23): record removal

- [ ] **Step 4: Instrument ChatHealthCache**

In `TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs`:
- Add `CacheMetrics` to the constructor parameters
- In `GetCachedHealth()` (line ~20): record hit/miss
- In `RemoveHealth()` (line ~60): record removal

- [ ] **Step 5: Instrument SemanticKernelChatService kernel cache**

In `TelegramGroupsAdmin.Core/Services/AI/SemanticKernelChatService.cs` (already modified in Task 4 for `ApiMetrics`):
- Add `CacheMetrics` to the constructor parameters (alongside `ApiMetrics` from Task 4)
- Wrap Semantic Kernel cache lookups with `cacheMetrics.RecordHit("kernel")` / `cacheMetrics.RecordMiss("kernel")`

- [ ] **Step 6: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Core/Metrics/CacheMetrics.cs \
       TelegramGroupsAdmin.Core/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Telegram/Services/ChatCache.cs \
       TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs
git commit -m "feat: add CacheMetrics for hit/miss/removal tracking on ChatCache and ChatHealthCache"
```

---

## Task 6: PipelineMetrics — Message Processing, Moderation, Profile Scans

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/DetectionActionService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs`

- [ ] **Step 1: Create `PipelineMetrics.cs`**

Create `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs` with:
- 6 counters: `messages_processed_total`, `moderation_actions_total`, `commands_handled_total`, `profile_scans_total`, `profile_scan.timeouts_total`, `profile_scan.skipped_total`
- 2 histograms: `processing.duration`, `profile_scan.duration`
- Recording methods: `RecordMessageProcessed()`, `RecordModerationAction()`, `RecordCommandHandled()`, `RecordProfileScan()`, `RecordProfileScanTimeout()`, `RecordProfileScanSkipped()`

Follow the same pattern as `DetectionMetrics` — sealed class, private meter `TelegramGroupsAdmin.Pipeline`, TagList in recording methods.

- [ ] **Step 2: Register in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, add `services.AddSingleton<PipelineMetrics>();` in the singleton registration block (after line ~204).

- [ ] **Step 3: Instrument MessageProcessingService**

In `MessageProcessingService.cs`:
- Add `PipelineMetrics` to the constructor
- In `HandleNewMessageAsync()` (line ~57): add `Stopwatch.GetTimestamp()` at start, call `pipelineMetrics.RecordMessageProcessed("new_message", result, durationMs)` at each exit path
- In `HandleEditAsync()` if it exists: same pattern with `"edit"` source

- [ ] **Step 4: Instrument DetectionActionService**

In `DetectionActionService.cs`:
- Add `PipelineMetrics` to the constructor
- After `MarkAsSpamAndBanAsync()` calls (lines ~98, ~127): `pipelineMetrics.RecordModerationAction("ban", "auto")`
- After `CreateReportAsync()` (line ~146): `pipelineMetrics.RecordModerationAction("report", "auto")`
- For hard block path: `pipelineMetrics.RecordModerationAction("ban", "auto")` with hard_block context

- [ ] **Step 5: Instrument ProfileScanService**

In `ProfileScanService.cs`:
- Add `PipelineMetrics` to the constructor
- In `ScanUserProfileAsync()` (line ~43): add stopwatch at start
- At dedup return (line ~62-72): `pipelineMetrics.RecordProfileScanSkipped("dedup")`
- At no-session return (line ~81): `pipelineMetrics.RecordProfileScanSkipped("no_session")`
- At timeout (line ~99-113): `pipelineMetrics.RecordProfileScanTimeout()`
- At excluded return (line ~159): `pipelineMetrics.RecordProfileScanSkipped("excluded")`
- At successful completion: `pipelineMetrics.RecordProfileScan(outcome, source, durationMs)`

Determine `source` from context: `"welcome"` when `triggeringChat` is provided, `"rescan"` for ProfileRescanJob, `"manual"` for UI-triggered scans.

- [ ] **Step 6: Instrument CommandRouter**

In `CommandRouter.cs`:
- Add `PipelineMetrics` to the constructor
- After successful command execution (line ~130): `pipelineMetrics.RecordCommandHandled(commandName)`

- [ ] **Step 7: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs \
       TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs \
       TelegramGroupsAdmin.Telegram/Services/BackgroundServices/DetectionActionService.cs \
       TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs \
       TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs
git commit -m "feat: add PipelineMetrics for message processing, moderation, and profile scans"
```

---

## Task 7: ChatMetrics — Managed Chats, Health Checks, Joins/Leaves

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ChatHealthRefreshOrchestrator.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs`

- [ ] **Step 1: Create `ChatMetrics.cs`**

Create `TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs` with:
- 1 ObservableGauge: `tga.chats.managed_count` — callback reads `IChatCache.Count`
- 5 counters: `health_check_total`, `marked_inactive_total`, `messages_total`, `user_joins_total`, `user_leaves_total`

**Note:** `RecordUserJoin()` and `RecordUserLeave()` call sites are added in Task 8 when WelcomeService is instrumented.
- Constructor takes `IChatCache` for the gauge callback
- Recording methods: `RecordHealthCheck()`, `RecordChatMarkedInactive()`, `RecordMessage()`, `RecordUserJoin()`, `RecordUserLeave()`

**Important:** The `IChatCache` dependency must be injected via constructor for the ObservableGauge callback.

- [ ] **Step 2: Register in DI**

Add `services.AddSingleton<ChatMetrics>();` — ensure it's registered AFTER `IChatCache`.

- [ ] **Step 3: Instrument ChatHealthRefreshOrchestrator**

In `ChatHealthRefreshOrchestrator.cs`:
- Add `ChatMetrics` to the constructor
- After health check result determination (lines ~46-77): `chatMetrics.RecordHealthCheck(result)` where result is `"healthy"`, `"degraded"`, or `"unreachable"`
- At `MarkInactiveAsync()` call (line ~59): `chatMetrics.RecordChatMarkedInactive()`

- [ ] **Step 4: Instrument MessageProcessingService for message types**

In `MessageProcessingService.cs` (already modified in Task 6):
- Add `ChatMetrics` to the constructor (if not already added)
- Near the start of `HandleNewMessageAsync()`, determine message type from the Telegram `Message` object and call `chatMetrics.RecordMessage(type)` where type is `"text"`, `"photo"`, `"video"`, `"document"`, `"animation"`, `"sticker"`, or `"other"`

- [ ] **Step 5: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs \
       TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Telegram/Services/ChatHealthRefreshOrchestrator.cs \
       TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs
git commit -m "feat: add ChatMetrics for managed chats, health checks, and message type tracking"
```

---

## Task 8: WelcomeMetrics — Welcome Flow Outcomes and Security Checks

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs`

- [ ] **Step 1: Create `WelcomeMetrics.cs`**

Create `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs` with:
- 5 counters: `joins_total`, `security_checks_total`, `bot_joins_total`, `timeouts_total`, `leaves_total`
- 1 histogram: `duration` (unit: ms)
- Recording methods per spec

- [ ] **Step 2: Register in DI**

Add `services.AddSingleton<WelcomeMetrics>();`

- [ ] **Step 3: Instrument WelcomeService**

In `WelcomeService.cs`:
- Add `WelcomeMetrics` and `ChatMetrics` to the constructor
- Add `Stopwatch.GetTimestamp()` at the start of `HandleChatMemberUpdateAsync()`
- At each exit path (see exploration map — ~18 exit paths), record the appropriate outcome:
  - Bot banned → `welcomeMetrics.RecordBotJoin("banned")`
  - Bot allowed → `welcomeMetrics.RecordBotJoin("allowed")`
  - Admin skip → `welcomeMetrics.RecordWelcomeOutcome("skipped_admin", durationMs)`
  - Pre-banned → `welcomeMetrics.RecordWelcomeOutcome("pre_banned", durationMs)`
  - Username blacklist → `welcomeMetrics.RecordSecurityCheck("username_blacklist", "fail")` + `RecordWelcomeOutcome("banned", durationMs)`
  - CAS banned → `welcomeMetrics.RecordSecurityCheck("cas", "fail")` + `RecordWelcomeOutcome("banned", durationMs)`
  - Impersonation → `welcomeMetrics.RecordSecurityCheck("impersonation", "fail")` + `RecordWelcomeOutcome("banned", durationMs)`
  - Profile scan auto-ban → `welcomeMetrics.RecordSecurityCheck("profile_scan", "fail")` + `RecordWelcomeOutcome("banned", durationMs)`
  - Photo match detection → `welcomeMetrics.RecordSecurityCheck("photo_match", "fail")` + appropriate outcome
  - Normal admission → `welcomeMetrics.RecordWelcomeOutcome("admitted", durationMs)`
  - Denied rules → `welcomeMetrics.RecordWelcomeOutcome("denied_rules", durationMs)`
- For each security check that passes: `welcomeMetrics.RecordSecurityCheck(checkName, "pass")`
- For skipped checks: `welcomeMetrics.RecordSecurityCheck(checkName, "skipped")`
- At `HandleUserLeftAsync()`: `welcomeMetrics.RecordUserLeft()` + `chatMetrics.RecordUserLeave()`
- At new user join: `chatMetrics.RecordUserJoin()`

**This is the most complex instrumentation point.** Take care to record at every exit path. The stopwatch should be started early and duration recorded at the outcome point.

- [ ] **Step 4: Instrument WelcomeTimeoutJob**

In `WelcomeTimeoutJob.cs`:
- Add `WelcomeMetrics` to the constructor
- When a timeout kicks a user (around line ~100): `welcomeMetrics.RecordWelcomeTimeout()` + `welcomeMetrics.RecordWelcomeOutcome("timed_out", durationMs)`

- [ ] **Step 5: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs \
       TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs \
       TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs
git commit -m "feat: add WelcomeMetrics for welcome flow outcomes and security check tracking"
```

---

## Task 9: ReportMetrics — Report Creation, Resolution, Pending Count

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/ReportActions/ReportActionsService.cs`

- [ ] **Step 1: Create `ReportMetrics.cs`**

Create `TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs` with:
- 2 counters: `created_total`, `resolved_total`
- 1 histogram: `resolution.duration` (unit: ms)
- 1 ObservableGauge: `pending_count` — reads from internal `_pendingCount` field
- Internal `_pendingCount` (`long`) managed via `Interlocked.Increment` / `Interlocked.Decrement`

```csharp
private long _pendingCount;

public ReportMetrics()
{
    // ... other instruments ...

    _meter.CreateObservableGauge(
        "tga.reports.pending_count",
        () => Interlocked.Read(ref _pendingCount),
        description: "Current total pending reports");
}

public void RecordReportCreated(string type, string source)
{
    _createdTotal.Add(1, new TagList { { "type", type }, { "source", source } });
    Interlocked.Increment(ref _pendingCount);
}

public void RecordReportResolved(string type, string action, double durationMs)
{
    _resolvedTotal.Add(1, new TagList { { "type", type }, { "action", action } });
    _resolutionDuration.Record(durationMs, new TagList { { "type", type } });
    Interlocked.Decrement(ref _pendingCount);
}
```

**Eventual consistency note:** `_pendingCount` resets to 0 on app restart. Resolving pre-existing reports will decrement below zero. This is expected and accepted — the gauge self-corrects as new reports are created/resolved. Do not add a database query or floor check.

- [ ] **Step 2: Register in DI**

Add `services.AddSingleton<ReportMetrics>();`

- [ ] **Step 3: Instrument ReportService**

In `ReportService.cs`:
- Add `ReportMetrics` to the constructor
- After `InsertContentReportAsync()` (line ~38): `reportMetrics.RecordReportCreated(type, source)` where `type` maps from the report's type and `source` is `isAutomated ? "auto" : "user"`

- [ ] **Step 4: Instrument ReportActionsService**

In `ReportActionsService.cs`:
- Add `ReportMetrics` to the constructor
- In `ExecuteWithLockAsync()` (lines ~93-121) or in each `Handle*Async` wrapper method:
  - After the delegate completes, if successful: `reportMetrics.RecordReportResolved(type, action, durationMs)`
  - Map each handler method to its type and action:
    - `HandleContentSpamAsync` → `("content", "spam")`
    - `HandleContentBanAsync` → `("content", "ban")`
    - `HandleContentWarnAsync` → `("content", "warn")`
    - `HandleContentDismissAsync` → `("content", "dismiss")`
    - `HandleProfileScanBanAsync` → `("profile_scan", "ban")`
    - `HandleProfileScanKickAsync` → `("profile_scan", "kick")`
    - `HandleProfileScanAllowAsync` → `("profile_scan", "allow")`
    - `HandleImpersonationConfirmAsync` → `("impersonation", "confirm")`
    - `HandleImpersonationDismissAsync` → `("impersonation", "dismiss")`
    - `HandleImpersonationTrustAsync` → `("impersonation", "trust")`
    - `HandleExamApproveAsync` → `("exam_failure", "approve")`
    - `HandleExamDenyAsync` → `("exam_failure", "deny")`
    - `HandleExamDenyAndBanAsync` → `("exam_failure", "deny_and_ban")`

- [ ] **Step 5: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs \
       TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs \
       TelegramGroupsAdmin.Telegram/Services/ReportService.cs \
       TelegramGroupsAdmin.Telegram/Services/ReportActions/ReportActionsService.cs
git commit -m "feat: add ReportMetrics for report creation, resolution, and pending count tracking"
```

---

## Task 10: Telegram Bot API Metrics — Instrument Bot Services

This is separate from Task 4 (ApiMetrics class creation) because the Bot services are in the Telegram project and have many methods to instrument.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotMessageService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotUserService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs`

- [ ] **Step 1: Instrument BotMessageService**

- Add `ApiMetrics` to the constructor
- In each method that calls the Telegram Bot API (send, edit, delete), add:
  ```csharp
  apiMetrics.RecordTelegramApiCall("send_message", success: true);
  ```
- In catch blocks for `ApiRequestException`, add:
  ```csharp
  apiMetrics.RecordTelegramApiError(ex.ErrorCode.ToString());
  ```

- [ ] **Step 2: Instrument BotUserService**

- Add `ApiMetrics` to the constructor
- Instrument `GetChatMemberAsync`, `BanChatMemberAsync`, `UnbanChatMemberAsync`, `RestrictChatMemberAsync`, etc.

- [ ] **Step 3: Instrument BotChatService**

- Add `ApiMetrics` to the constructor
- Instrument `GetChatAsync`, `CheckHealthAsync`, `CreateChatInviteLinkAsync`, etc.

- [ ] **Step 4: Build, verify, commit**

```bash
dotnet build && dotnet run --migrate-only
git add TelegramGroupsAdmin.Telegram/Services/Bot/BotMessageService.cs \
       TelegramGroupsAdmin.Telegram/Services/Bot/BotUserService.cs \
       TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs
git commit -m "feat: add Telegram Bot API operation metrics to BotMessageService, BotUserService, BotChatService"
```

---

## Task 11: Final Verification and Cleanup

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```

Fix any compilation errors.

- [ ] **Step 2: Run existing tests**

```bash
dotnet test --filter "Category!=E2E" --no-build 2>&1 | tee /tmp/metrics-test-results.txt
```

Review output for failures. Since metrics classes are singletons with no side effects, failures would indicate a DI wiring issue (missing registration or constructor parameter mismatch).

- [ ] **Step 3: Verify app boots**

```bash
dotnet run --migrate-only
```

This validates all DI registrations resolve correctly.

- [ ] **Step 4: Verify no dangling TelemetryConstants references**

```bash
# Should return zero results for counter/histogram usage
grep -r "TelemetryConstants\.\(SpamDetections\|FileScanResults\|JobExecutions\|SpamDetectionDuration\|FileScanDuration\|JobDuration\)" --include="*.cs" .
```

If any results, update those call sites to use the appropriate metrics class.

- [ ] **Step 5: Verify no remaining MemoryInstrumentation references**

```bash
grep -r "MemoryInstrumentation" --include="*.cs" .
```

If any results, update to `MemoryMetrics`.

- [ ] **Step 6: Commit any cleanup**

```bash
git add -A
git commit -m "chore: final cleanup for metrics expansion"
```
