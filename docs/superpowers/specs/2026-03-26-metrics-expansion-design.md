# Metrics Expansion Design

## Summary

Expand `/metrics` endpoint instrumentation from ~22 instruments to ~58, standardize naming on `tga.*` dotted prefix, and organize metrics into domain-scoped singleton classes with wrapper methods that enforce consistent tagging.

## Goals

- **Full observability from Grafana** — independent of the Blazor analytics pages
- **Alerting capability** — real-time rate-based alerts on external API failures, pipeline stalls, quota exhaustion
- **Per-feature attribution** — every external API call tagged with *why* it happened
- **Consistent naming** — all metrics under `tga.<domain>.<subject>.<metric_type>`

## Non-Goals

- Grafana dashboard creation (handled separately on prod machine)
- Alert rule definitions (configured in Prometheus/Grafana, not in app code)
- Distributed tracing expansion (ActivitySources are already well-covered)

## Architecture: Domain-Scoped Metrics Classes

Each class is a **singleton** registered in DI, owns a `Meter` instance, and exposes recording methods that enforce consistent tag construction. Services inject only the metrics class they need.

```
┌─────────────────────────────────────────────────────────┐
│ TelegramGroupsAdmin.Core                                │
│   ApiMetrics         (OpenAI, VirusTotal, SendGrid, TG) │
│   CacheMetrics       (hit/miss/eviction counters)       │
├─────────────────────────────────────────────────────────┤
│ TelegramGroupsAdmin.ContentDetection                    │
│   DetectionMetrics   (spam, file scan, veto)            │
├─────────────────────────────────────────────────────────┤
│ TelegramGroupsAdmin.Telegram                            │
│   PipelineMetrics    (messages, moderation, profile)    │
│   ChatMetrics        (managed chats, health, joins)     │
│   WelcomeMetrics     (welcome flow, security checks)    │
│   ReportMetrics      (created, resolved, pending)       │
├─────────────────────────────────────────────────────────┤
│ TelegramGroupsAdmin.BackgroundJobs                      │
│   JobMetrics         (executions, duration, rows)       │
├─────────────────────────────────────────────────────────┤
│ TelegramGroupsAdmin (main app)                          │
│   MemoryMetrics      (renamed MemoryInstrumentation)    │
└─────────────────────────────────────────────────────────┘
```

### Why Wrapper Methods

Recording methods like `RecordOpenAiCall(feature, model, tokens, duration, success)` enforce that callers always provide the required tags. This prevents inconsistent tagging (e.g., forgetting the `feature` tag on one call site) which would create confusing Grafana queries.

## Naming Convention

All metrics use: `tga.<domain>.<subject>.<metric_type>`

### Renames (Existing Flat Metrics)

| Current Name | New Name |
|---|---|
| `spam_detections_total` | `tga.detection.spam_total` |
| `spam_detection_duration_ms` | `tga.detection.algorithm.duration_ms` |
| `file_scan_results_total` | `tga.detection.file_scan_total` |
| `file_scan_duration_ms` | `tga.detection.file_scan.duration_ms` |
| `job_executions_total` | `tga.jobs.executions_total` |
| `job_duration_ms` | `tga.jobs.duration_ms` |

Existing `tga.cache.*`, `tga.ml.*`, `tga.sessions.*`, `tga.queue.*` gauges are already correctly named — no changes needed.

**Grafana impact:** All 6 renamed metrics require dashboard query updates on the prod machine.

## Metrics Classes — Full Instrument Catalog

### 1. `ApiMetrics` (TelegramGroupsAdmin.Core)

Meter: `TelegramGroupsAdmin.Api`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.api.openai.calls_total` | Counter | `feature`, `model`, `status` | Call count per feature |
| `tga.api.openai.latency_ms` | Histogram | `feature`, `model` | Latency distribution |
| `tga.api.openai.tokens_total` | Counter | `feature`, `model`, `type` (prompt\|completion) | Token consumption |
| `tga.api.virustotal.calls_total` | Counter | `operation` (hash_lookup\|file_upload), `status` | Calls by operation |
| `tga.api.virustotal.latency_ms` | Histogram | `operation` | Latency per operation |
| `tga.api.virustotal.quota_exhausted_total` | Counter | — | Daily quota exhaustion events |
| `tga.api.sendgrid.sends_total` | Counter | `template`, `status` | Email sends |
| `tga.api.telegram.calls_total` | Counter | `operation`, `status` | Bot API calls by operation |
| `tga.api.telegram.errors_total` | Counter | `error_type` | Error breakdown |

**Recording methods:**
- `RecordOpenAiCall(string feature, string model, int promptTokens, int completionTokens, double durationMs, bool success)`
- `RecordVirusTotalCall(string operation, double durationMs, bool success)`
- `RecordVirusTotalQuotaExhausted()`
- `RecordSendGridSend(string template, bool success)`
- `RecordTelegramApiCall(string operation, bool success)`
- `RecordTelegramApiError(string errorType)`

**Instrumentation points:**
- `SemanticKernelChatService` — already extracts `ChatTokenUsage`, add metrics after each completion
- `VirusTotalScannerService` — wrap hash lookup and file upload calls
- `SendGridEmailService` (or equivalent) — wrap send calls
- `BotMessageService`, `BotUserService`, `BotChatService` — wrap Telegram Bot API calls

### 2. `DetectionMetrics` (TelegramGroupsAdmin.ContentDetection)

Meter: `TelegramGroupsAdmin.Detection`

Replaces existing flat counters in `TelemetryConstants`.

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.detection.spam_total` | Counter | `algorithm`, `result` (spam\|ham) | Per-algorithm detection counts |
| `tga.detection.algorithm.duration_ms` | Histogram | `algorithm` | Per-algorithm execution time |
| `tga.detection.file_scan_total` | Counter | `tier` (clamav\|virustotal), `result` (malicious\|clean) | File scan results |
| `tga.detection.file_scan.duration_ms` | Histogram | `tier` | File scan latency |
| `tga.detection.veto_total` | Counter | `algorithm` | OpenAI veto count per algorithm |

**Recording methods:**
- `RecordSpamDetection(string algorithm, bool isSpam, double durationMs)`
- `RecordFileScan(string tier, bool isMalicious, double durationMs)`
- `RecordVeto(string algorithm)`

**Instrumentation points:**
- `ContentDetectionEngineV2` — replace direct `TelemetryConstants` calls
- `ClamAVScannerService` — replace direct `TelemetryConstants` calls
- `VirusTotalScannerService` — add Tier 2 recording (currently missing)

### 3. `PipelineMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Pipeline`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.pipeline.messages_processed_total` | Counter | `source` (new_message\|edit), `result` (processed\|skipped\|error) | Messages through pipeline |
| `tga.pipeline.processing.duration_ms` | Histogram | `source` | End-to-end pipeline latency |
| `tga.pipeline.moderation_actions_total` | Counter | `action` (ban\|warn\|report\|delete\|malware_alert), `trigger` (auto\|admin) | All moderation actions |
| `tga.pipeline.reports_created_total` | Counter | `reason` (borderline\|admin_review) | Reports queued for review |
| `tga.pipeline.commands_handled_total` | Counter | `command` | Bot command usage |
| `tga.pipeline.profile_scans_total` | Counter | `outcome` (clean\|held_for_review\|banned), `source` (welcome\|rescan\|manual) | Scan outcomes |
| `tga.pipeline.profile_scan.duration_ms` | Histogram | `source` | Scan latency |
| `tga.pipeline.profile_scan.timeouts_total` | Counter | — | 45s timeout hits |
| `tga.pipeline.profile_scan.skipped_total` | Counter | `reason` (dedup\|no_session\|excluded) | Skipped scans |

**Recording methods:**
- `RecordMessageProcessed(string source, string result, double durationMs)`
- `RecordModerationAction(string action, string trigger)`
- `RecordReportCreated(string reason)`
- `RecordCommandHandled(string command)`
- `RecordProfileScan(string outcome, string source, double durationMs)`
- `RecordProfileScanTimeout()`
- `RecordProfileScanSkipped(string reason)`

**Instrumentation points:**
- `MessageProcessingService.HandleNewMessageAsync` / `HandleEditAsync` — wrap with stopwatch
- `DetectionActionService.HandleSpamDetectionActionsAsync` — record moderation actions
- `ProfileScanService.ScanUserProfileAsync` — wrap with stopwatch, record outcome/timeout/skip
- `CommandRouter` — record command usage

### 4. `ChatMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Chats`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.chats.managed_total` | ObservableGauge | — | Active managed chats count |
| `tga.chats.health_check_total` | Counter | `result` (healthy\|degraded\|unreachable) | Health check outcomes |
| `tga.chats.marked_inactive_total` | Counter | — | Chats marked inactive (3 failures) |
| `tga.chats.messages_total` | Counter | `type` (text\|photo\|video\|document\|sticker\|other) | Messages by content type |
| `tga.chats.user_joins_total` | Counter | — | User joins across all chats |
| `tga.chats.user_leaves_total` | Counter | — | User departures |

**Recording methods:**
- `RecordHealthCheck(string result)`
- `RecordChatMarkedInactive()`
- `RecordMessage(string type)`
- `RecordUserJoin()`
- `RecordUserLeave()`

**Instrumentation points:**
- `ChatHealthRefreshOrchestrator.RefreshHealthForChatAsync` — record health check results
- `MessageProcessingService.HandleNewMessageAsync` — record message type
- `WelcomeService.HandleChatMemberUpdateAsync` — record joins/leaves
- Observable gauge callback queries `IManagedChatsRepository` for active count

### 5. `WelcomeMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Welcome`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.welcome.joins_total` | Counter | `result` (admitted\|banned\|timed_out\|denied_rules\|pre_banned\|skipped_admin) | Welcome flow outcomes |
| `tga.welcome.security_checks_total` | Counter | `check` (username_blacklist\|cas\|impersonation\|profile_scan\|photo_match), `result` (pass\|fail\|skipped) | Per-check pass/fail |
| `tga.welcome.duration_ms` | Histogram | `result` | End-to-end welcome flow time |
| `tga.welcome.bot_joins_total` | Counter | `result` (allowed\|banned) | Bot join attempts |
| `tga.welcome.timeouts_total` | Counter | — | Welcome timeout expirations |
| `tga.welcome.leaves_total` | Counter | — | Users left before completing |

**Recording methods:**
- `RecordWelcomeOutcome(string result, double durationMs)`
- `RecordSecurityCheck(string check, string result)`
- `RecordBotJoin(string result)`
- `RecordWelcomeTimeout()`
- `RecordUserLeft()`

**Instrumentation points:**
- `WelcomeService.HandleChatMemberUpdateAsync` — wrap with stopwatch, record outcome at each exit path
- Each security check step — record pass/fail/skip
- `WelcomeTimeoutJob` — record timeout
- `WelcomeService.HandleUserLeftAsync` — record leave

### 6. `ReportMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Reports`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.reports.created_total` | Counter | `type` (content\|profile_scan\|impersonation), `source` (auto\|user) | Reports created |
| `tga.reports.resolved_total` | Counter | `type`, `action` (spam\|ban\|warn\|dismiss\|kick\|allow\|confirm\|trust) | Resolution actions |
| `tga.reports.resolution.duration_ms` | Histogram | `type` | Time from creation to resolution |
| `tga.reports.pending` | ObservableGauge | `type` | Current pending count |

**Recording methods:**
- `RecordReportCreated(string type, string source)`
- `RecordReportResolved(string type, string action, double durationMs)`

**Instrumentation points:**
- `ReportService.CreateReportAsync` — record creation
- `ReportActionsService` — each `Handle*Async` method records resolution
- Observable gauge callback queries `IReportsRepository` for pending counts

### 7. `JobMetrics` (TelegramGroupsAdmin.BackgroundJobs)

Meter: `TelegramGroupsAdmin.Jobs`

Replaces existing flat counters in `TelemetryConstants`.

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.jobs.executions_total` | Counter | `job_name`, `status` (success\|failure) | Job execution counts |
| `tga.jobs.duration_ms` | Histogram | `job_name` | Job execution time |
| `tga.jobs.rows_affected_total` | Counter | `job_name` | Rows deleted/processed (cleanup jobs) |

**Recording methods:**
- `RecordJobExecution(string jobName, bool success, double durationMs)`
- `RecordRowsAffected(string jobName, long count)`

**Instrumentation points:**
- All 16 `*Job.cs` files — replace direct `TelemetryConstants.JobExecutions` / `JobDuration` calls
- `DataCleanupJob`, `DatabaseMaintenanceJob`, `DeleteMessageJob`, `DeleteUserMessagesJob` — add rows affected

### 8. `CacheMetrics` (TelegramGroupsAdmin.Core)

Meter: `TelegramGroupsAdmin.Cache`

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `tga.cache.hits_total` | Counter | `cache_name` | Cache hits |
| `tga.cache.misses_total` | Counter | `cache_name` | Cache misses |
| `tga.cache.evictions_total` | Counter | `cache_name` | Cache evictions |

**Recording methods:**
- `RecordHit(string cacheName)`
- `RecordMiss(string cacheName)`
- `RecordEviction(string cacheName)`

**Instrumentation points:**
- `ChatCache` — wrap `TryGetValue` / `AddOrUpdate` / eviction callbacks
- `ChatHealthCache` — same pattern
- `SemanticKernelChatService` kernel cache — wrap cache lookups

### 9. `MemoryMetrics` (TelegramGroupsAdmin — main app)

Renamed from `MemoryInstrumentation`. No instrument changes — keeps all 13 existing observable gauges.

Meter: `TelegramGroupsAdmin.Memory` (unchanged)

## Migration Plan for Existing Metrics

### TelemetryConstants Changes

1. **Remove** counter/histogram fields (`SpamDetections`, `FileScanResults`, `JobExecutions`, `SpamDetectionDuration`, `FileScanDuration`, `JobDuration`)
2. **Remove** the `Metrics` meter (replaced by per-domain meters)
3. **Keep** the `Memory` meter (used by `MemoryMetrics`)
4. **Keep** all four `ActivitySource` fields (tracing is unchanged)

### Call Site Updates

All files currently calling `TelemetryConstants.SpamDetections.Add(...)` etc. switch to injecting the appropriate metrics class and calling its wrapper method. The 16 background jobs switch from `TelemetryConstants.JobExecutions` / `JobDuration` to `JobMetrics.RecordJobExecution(...)`.

## DI Registration

Each metrics class registers as a singleton in its project's `ServiceCollectionExtensions`:

- `TelegramGroupsAdmin.Core` → `services.AddSingleton<ApiMetrics>()`, `services.AddSingleton<CacheMetrics>()`
- `TelegramGroupsAdmin.ContentDetection` → `services.AddSingleton<DetectionMetrics>()`
- `TelegramGroupsAdmin.Telegram` → `services.AddSingleton<PipelineMetrics>()`, `services.AddSingleton<ChatMetrics>()`, `services.AddSingleton<WelcomeMetrics>()`, `services.AddSingleton<ReportMetrics>()`
- `TelegramGroupsAdmin.BackgroundJobs` → `services.AddSingleton<JobMetrics>()`
- `TelegramGroupsAdmin` (main app) → `services.AddSingleton<MemoryMetrics>()` (replaces `MemoryInstrumentation`)

## OpenTelemetry Configuration Update

In `Program.cs`, the existing `AddMeter("TelegramGroupsAdmin.*")` wildcard already covers all new meters since they all start with `TelegramGroupsAdmin.`. No changes needed to the OTel pipeline configuration.

## Testing Strategy

- **Unit tests not required for metrics classes** — they are thin wrappers around `System.Diagnostics.Metrics` primitives
- **Verify via `/metrics` endpoint** — after implementation, scrape `/metrics` and confirm all new metric names appear with expected tags
- **Existing tests unaffected** — metrics classes are singletons with no side effects; services that gain a new constructor parameter just need the DI registration

## Cardinality Notes

Tag cardinality is bounded:
- `feature` tag: ~6 values (spam_check, image_classification, content_analysis, profile_scan, translation, impersonation)
- `algorithm` tag: ~14 values (fixed set of content checks)
- `command` tag: ~10 values (fixed set of bot commands)
- `job_name` tag: 16 values (fixed set of Quartz jobs)
- `operation` tag: ~8 values per API (fixed set of operations)

No unbounded cardinality (no user IDs, chat IDs, or message IDs as tags).
