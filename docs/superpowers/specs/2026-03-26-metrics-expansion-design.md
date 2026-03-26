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
- Blazor/web UI metrics (page load times, auth events) — deferred to a future iteration

## Architecture: Domain-Scoped Metrics Classes

Each class is a **singleton** registered in DI, owns a `Meter` instance, and exposes recording methods that enforce consistent tag construction. Services inject only the metrics class they need.

```
┌─────────────────────────────────────────────────────────┐
│ TelegramGroupsAdmin.Core                                │
│   ApiMetrics         (OpenAI, VirusTotal, SendGrid, TG) │
│   CacheMetrics       (hit/miss/removal counters)        │
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

### Prometheus Export Behavior (Verified Against Prod)

- **Dots become underscores**: `tga.cache.chat.count` → `tga_cache_chat_count`
- **`_total` suffix**: The Prometheus exporter detects existing `_total` suffixes and does NOT double-append. `spam_detections_total` exports as `spam_detections_total`, not `spam_detections_total_total`. Safe to include `_total` in counter names.
- **Unit suffix**: The exporter appends the unit as a suffix. A histogram named `job_duration_ms` with `unit: "ms"` exports as `job_duration_ms_milliseconds_bucket` — redundant. **Fix: drop `_ms` from histogram names and keep the `unit: "ms"` parameter.** E.g., `tga.detection.algorithm.duration` with `unit: "ms"` → `tga_detection_algorithm_duration_milliseconds_bucket`.

### Renames (Existing Flat Metrics)

| Current Name | New Name | Notes |
|---|---|---|
| `spam_detections_total` | `tga.detection.spam_total` | |
| `spam_detection_duration_ms` | `tga.detection.algorithm.duration` | Drop `_ms`, keep `unit: "ms"` |
| `file_scan_results_total` | `tga.detection.file_scan_total` | |
| `file_scan_duration_ms` | `tga.detection.file_scan.duration` | Drop `_ms`, keep `unit: "ms"` |
| `job_executions_total` | `tga.jobs.executions_total` | |
| `job_duration_ms` | `tga.jobs.duration` | Drop `_ms`, keep `unit: "ms"` |

Existing `tga.cache.*`, `tga.ml.*`, `tga.sessions.*`, `tga.queue.*` gauges are already correctly named — no changes needed.

**Grafana impact:** All 6 renamed metrics require dashboard query updates on the prod machine. The histogram renames also change the exported suffix from `_ms_milliseconds` to `_milliseconds`.

### Tag Value Migration

The existing `result` tag on `spam_detections_total` currently uses values: `"spam"`, `"clean"`, `"abstained"`, `"low_confidence"`. The new `tga.detection.spam_total` will use `result` values: `spam`, `clean`, `abstained`. The `low_confidence` value is collapsed into `clean` since the per-algorithm level already provides confidence granularity. The `version` tag (`"v2"`) is intentionally dropped — only one engine version exists at a time, and the tag adds no diagnostic value.

## Metrics Classes — Full Instrument Catalog

### 1. `ApiMetrics` (TelegramGroupsAdmin.Core)

Meter: `TelegramGroupsAdmin.Api`

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.api.openai.calls_total` | Counter | — | `feature`, `model`, `status` | Call count per feature |
| `tga.api.openai.latency` | Histogram | ms | `feature`, `model` | Latency distribution |
| `tga.api.openai.tokens_total` | Counter | — | `feature`, `model`, `type` (prompt\|completion) | Token consumption |
| `tga.api.virustotal.calls_total` | Counter | — | `operation` (hash_lookup\|file_upload), `status` | Calls by operation |
| `tga.api.virustotal.latency` | Histogram | ms | `operation` | Latency per operation |
| `tga.api.virustotal.quota_exhausted_total` | Counter | — | — | Daily quota exhaustion events |
| `tga.api.sendgrid.sends_total` | Counter | — | `template`, `status` | Email sends |
| `tga.api.telegram.calls_total` | Counter | — | `operation`, `status` | Bot API calls by operation |
| `tga.api.telegram.errors_total` | Counter | — | `error_type` | Error breakdown |

**Recording methods:**
- `RecordOpenAiCall(string feature, string model, int promptTokens, int completionTokens, double durationMs, bool success)`
- `RecordVirusTotalCall(string operation, double durationMs, bool success)`
- `RecordVirusTotalQuotaExhausted()`
- `RecordSendGridSend(string template, bool success)` — use `"raw"` for non-templated sends
- `RecordTelegramApiCall(string operation, bool success)`
- `RecordTelegramApiError(string errorType)`

**Instrumentation points:**
- `SemanticKernelChatService` (Core) — already extracts `ChatTokenUsage`, add metrics after each completion
- `VirusTotalScannerService` (ContentDetection) — wrap hash lookup and file upload calls
- `SendGridEmailService` (main app, `Services/Email/`) — wrap send calls; use `"raw"` template tag for non-templated sends
- `BotMessageService`, `BotUserService`, `BotChatService` (Telegram) — wrap Telegram Bot API calls

**Cardinality note:** The `operation` tag for Telegram API calls has ~15-20 values (send_message, delete_message, ban_chat_member, get_chat_member, restrict_chat_member, get_chat, get_chat_administrators, create_chat_invite_link, get_user_profile_photos, get_file, edit_message_text, edit_message_reply_markup, approve_chat_join_request, decline_chat_join_request, etc.). All bounded.

### 2. `DetectionMetrics` (TelegramGroupsAdmin.ContentDetection)

Meter: `TelegramGroupsAdmin.Detection`

Replaces existing flat counters in `TelemetryConstants`.

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.detection.spam_total` | Counter | — | `algorithm`, `result` (spam\|clean\|abstained) | Per-algorithm detection counts |
| `tga.detection.algorithm.duration` | Histogram | ms | `algorithm` | Per-algorithm execution time |
| `tga.detection.file_scan_total` | Counter | — | `tier` (clamav\|virustotal), `result` (malicious\|clean) | File scan results |
| `tga.detection.file_scan.duration` | Histogram | ms | `tier` | File scan latency |
| `tga.detection.veto_total` | Counter | — | `algorithm` | OpenAI veto count per algorithm |

**Recording methods:**
- `RecordSpamDetection(string algorithm, string result, double durationMs)` — accepts explicit result string to preserve abstained/clean distinction
- `RecordFileScan(string tier, bool isMalicious, double durationMs)`
- `RecordVeto(string algorithm)`

**Instrumentation points:**
- `ContentDetectionEngineV2` — replace direct `TelemetryConstants` calls
- `ClamAVScannerService` — replace direct `TelemetryConstants` calls
- `VirusTotalScannerService` — add Tier 2 recording (currently missing)

### 3. `PipelineMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Pipeline`

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.pipeline.messages_processed_total` | Counter | — | `source` (new_message\|edit), `result` (processed\|skipped\|error) | Messages through pipeline |
| `tga.pipeline.processing.duration` | Histogram | ms | `source` | End-to-end pipeline latency |
| `tga.pipeline.moderation_actions_total` | Counter | — | `action` (ban\|warn\|report\|delete\|malware_alert), `trigger` (auto\|admin) | All moderation actions |
| `tga.pipeline.commands_handled_total` | Counter | — | `command` | Bot command usage |
| `tga.pipeline.profile_scans_total` | Counter | — | `outcome` (clean\|held_for_review\|banned), `source` (welcome\|rescan\|manual) | Scan outcomes |
| `tga.pipeline.profile_scan.duration` | Histogram | ms | `source` | Scan latency |
| `tga.pipeline.profile_scan.timeouts_total` | Counter | — | — | 45s timeout hits |
| `tga.pipeline.profile_scan.skipped_total` | Counter | — | `reason` (dedup\|no_session\|excluded) | Skipped scans |

**Note:** `reports_created_total` was removed from PipelineMetrics to avoid overlap with `ReportMetrics.tga.reports.created_total`, which has richer tagging (`type` + `source`).

**Recording methods:**
- `RecordMessageProcessed(string source, string result, double durationMs)`
- `RecordModerationAction(string action, string trigger)`
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

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.chats.managed_count` | ObservableGauge | — | — | Active managed chats count |
| `tga.chats.health_check_total` | Counter | — | `result` (healthy\|degraded\|unreachable) | Health check outcomes |
| `tga.chats.marked_inactive_total` | Counter | — | — | Chats marked inactive (3 failures) |
| `tga.chats.messages_total` | Counter | — | `type` (text\|photo\|video\|document\|animation\|sticker\|other) | Messages by content type |
| `tga.chats.user_joins_total` | Counter | — | — | Raw user join events |
| `tga.chats.user_leaves_total` | Counter | — | — | Raw user departure events |

**Observable gauge implementation:** `tga.chats.managed_count` reads from `IChatCache.Count` (in-memory singleton), NOT from a database query. `ObservableGauge` callbacks must be synchronous (`Func<T>`); async DB calls are not permitted. The `ChatCache` already maintains the count of active managed chats in memory.

**Semantic distinction from WelcomeMetrics:** `tga.chats.user_joins_total` / `user_leaves_total` count raw join/leave events. `WelcomeMetrics` counts welcome *flow outcomes* (admitted, banned, timed_out, etc.). A single join event becomes one `tga.chats.user_joins_total` increment AND one `tga.welcome.joins_total` increment with a `result` tag. Both are recorded in `WelcomeService.HandleChatMemberUpdateAsync`.

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
- Observable gauge callback reads `IChatCache.Count`

### 5. `WelcomeMetrics` (TelegramGroupsAdmin.Telegram)

Meter: `TelegramGroupsAdmin.Welcome`

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.welcome.joins_total` | Counter | — | `result` (admitted\|banned\|timed_out\|denied_rules\|pre_banned\|skipped_admin) | Welcome flow outcomes |
| `tga.welcome.security_checks_total` | Counter | — | `check` (username_blacklist\|cas\|impersonation\|profile_scan\|photo_match), `result` (pass\|fail\|skipped) | Per-check pass/fail |
| `tga.welcome.duration` | Histogram | ms | `result` | End-to-end welcome flow time |
| `tga.welcome.bot_joins_total` | Counter | — | `result` (allowed\|banned) | Bot join attempts |
| `tga.welcome.timeouts_total` | Counter | — | — | Welcome timeout expirations |
| `tga.welcome.leaves_total` | Counter | — | — | Users left before completing |

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

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.reports.created_total` | Counter | — | `type` (content\|profile_scan\|impersonation\|exam_failure), `source` (auto\|user) | Reports created |
| `tga.reports.resolved_total` | Counter | — | `type`, `action` (spam\|ban\|warn\|dismiss\|kick\|allow\|confirm\|trust\|approve\|reject) | Resolution actions |
| `tga.reports.resolution.duration` | Histogram | ms | `type` | Time from creation to resolution |
| `tga.reports.pending_count` | ObservableGauge | — | — | Current total pending reports |

**Observable gauge implementation:** `tga.reports.pending_count` uses a cached counter maintained by `ReportMetrics` itself. Incremented on `RecordReportCreated()`, decremented on `RecordReportResolved()`. This avoids async DB calls in the synchronous `ObservableGauge` callback. The count is eventually consistent — it resets on app restart but self-corrects as reports are created/resolved. A per-`type` tag is not used on the gauge to keep the caching simple; the `created_total` and `resolved_total` counters provide per-type breakdown.

**Recording methods:**
- `RecordReportCreated(string type, string source)`
- `RecordReportResolved(string type, string action, double durationMs)`

**Instrumentation points:**
- `ReportService.CreateReportAsync` — record creation
- `ReportActionsService` — each `Handle*Async` method records resolution
- Observable gauge reads internal `_pendingCount` field (Interlocked increment/decrement)

### 7. `JobMetrics` (TelegramGroupsAdmin.BackgroundJobs)

Meter: `TelegramGroupsAdmin.Jobs`

Replaces existing flat counters in `TelemetryConstants`.

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.jobs.executions_total` | Counter | — | `job_name`, `status` (success\|failure) | Job execution counts |
| `tga.jobs.duration` | Histogram | ms | `job_name` | Job execution time |
| `tga.jobs.rows_affected_total` | Counter | — | `job_name` | Rows deleted/processed (cleanup jobs) |

**Recording methods:**
- `RecordJobExecution(string jobName, bool success, double durationMs)`
- `RecordRowsAffected(string jobName, long count)`

**Instrumentation points:**
- All 17 `*Job.cs` files — replace direct `TelemetryConstants.JobExecutions` / `JobDuration` calls
- `DataCleanupJob`, `DatabaseMaintenanceJob`, `DeleteMessageJob`, `DeleteUserMessagesJob` — add rows affected

### 8. `CacheMetrics` (TelegramGroupsAdmin.Core)

Meter: `TelegramGroupsAdmin.Cache`

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `tga.cache.hits_total` | Counter | — | `cache_name` | Cache hits |
| `tga.cache.misses_total` | Counter | — | `cache_name` | Cache misses |
| `tga.cache.removals_total` | Counter | — | `cache_name` | Explicit cache removals |

**Note:** Renamed from `evictions_total` to `removals_total`. The caches use `ConcurrentDictionary` with no automatic eviction policy — items are only removed explicitly via `RemoveChat()` / `RemoveHealth()`. This counter tracks those explicit removals.

**Recording methods:**
- `RecordHit(string cacheName)`
- `RecordMiss(string cacheName)`
- `RecordRemoval(string cacheName)`

**Instrumentation points:**
- `ChatCache` — wrap `TryGetValue` / `AddOrUpdate` / `RemoveChat`
- `ChatHealthCache` — same pattern
- `SemanticKernelChatService` kernel cache — wrap cache lookups

### 9. `MemoryMetrics` (TelegramGroupsAdmin — main app)

Renamed from `MemoryInstrumentation`. No instrument changes — keeps all 13 existing observable gauges. Owns its own `Meter` instance (no longer reads from `TelemetryConstants.Memory`).

Meter: `TelegramGroupsAdmin.Memory`

## Migration Plan for Existing Metrics

### TelemetryConstants Changes

1. **Remove** counter/histogram fields (`SpamDetections`, `FileScanResults`, `JobExecutions`, `SpamDetectionDuration`, `FileScanDuration`, `JobDuration`)
2. **Remove** the `Metrics` meter (replaced by per-domain meters)
3. **Remove** the `Memory` meter (moved to `MemoryMetrics` class)
4. **Keep** all four `ActivitySource` fields (tracing is unchanged)

After migration, `TelemetryConstants` contains only `ActivitySource` fields. If no other code references it, it can be removed entirely with the sources moved to a `TelemetryActivitySources` class or kept as-is.

### Call Site Updates

All files currently calling `TelemetryConstants.SpamDetections.Add(...)` etc. switch to injecting the appropriate metrics class and calling its wrapper method. The 17 background jobs switch from `TelemetryConstants.JobExecutions` / `JobDuration` to `JobMetrics.RecordJobExecution(...)`.

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
- `job_name` tag: 17 values (fixed set of Quartz jobs)
- `operation` tag (Telegram API): ~15-20 values (fixed set of Bot API operations)
- `operation` tag (VirusTotal): 2 values (hash_lookup, file_upload)

No unbounded cardinality (no user IDs, chat IDs, or message IDs as tags).
