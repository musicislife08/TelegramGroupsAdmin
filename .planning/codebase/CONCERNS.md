# Codebase Concerns

**Analysis Date:** 2026-03-15

## Tech Debt

### Email Error Handling Not Custom Exception Type
- **Issue:** `SendGridEmailService` throws generic `Exception` instead of custom exception type for SendGrid API errors
- **Files:** `TelegramGroupsAdmin/Services/Email/SendGridEmailService.cs` (line 87)
- **Impact:** Callers cannot differentiate email delivery failures from other exceptions, making error recovery and logging harder
- **Fix approach:** Create `SendGridException : Exception` custom type or use `InvalidOperationException` for API errors; improve error context with response details

### Stop Words Performance Query Not Implemented
- **Issue:** `GetAverageStopWordsExecutionTimeAsync()` in `StopWordRecommendationService` returns `null` permanently - JSONB query to extract ProcessingTimeMs not implemented
- **Files:** `TelegramGroupsAdmin.ContentDetection/ML/StopWordRecommendationService.cs` (lines 512-515)
- **Impact:** Performance cleanup recommendations for StopWords check cannot be calculated; analytics incomplete but non-critical since service still provides addition/removal recommendations
- **Fix approach:** Implement JSONB query pattern documented in comments - parse `check_results_json` → filter by `CheckName='StopWords'` → `AVG(ProcessingTimeMs)` using GIN index

### Rich Formatting in Notifications Blocked
- **Issue:** Notification messages use HTML escaping with plain text only; rich formatting (bold, italic, links) not supported
- **Files:** `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs` (line 250)
- **Impact:** Admin notifications limited to plain text; harder to highlight important information
- **Fix approach:** When implementing, use HtmlSanitizer library to safely allow user-provided HTML/Markdown instead of escaping everything

### Missing E2E Test Coverage for Email Workflows
- **Issue:** Multiple Blazor component tests marked with TODO comments indicating E2E tests recommended but not implemented
- **Files:**
  - `TelegramGroupsAdmin.ComponentTests/Components/BackupRestoreTests.cs` (line 79)
  - `TelegramGroupsAdmin.ComponentTests/Components/BackupEncryptionSetupDialogTests.cs` (line 58)
  - `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs` (line 57)
  - `TelegramGroupsAdmin.ComponentTests/Components/NotificationPreferencesCardTests.cs` (line 71)
  - `TelegramGroupsAdmin.ComponentTests/Components/BotGeneralSettingsTests.cs` (line 65)
  - `TelegramGroupsAdmin.ComponentTests/Components/RestoreBackupModalTests.cs` (line 56)
  - `TelegramGroupsAdmin.ComponentTests/Components/BackupPassphraseRotationDialogTests.cs` (line 74)
- **Impact:** Component tests don't exercise interactive workflows requiring user input; Playwright E2E tests recommended for full scenario testing
- **Fix approach:** Create Playwright E2E tests for backup restore flow, backup encryption, and settings UI interactions

### Missing Email Verification Test Coverage
- **Issue:** `RegistrationTests.cs` (line 142) has TODO comment for invited user registration with email verification
- **Files:** `TelegramGroupsAdmin.E2ETests/Tests/Authentication/RegistrationTests.cs` (line 142)
- **Impact:** Email verification flow for invited users not covered by E2E tests; registration path incomplete
- **Fix approach:** Implement E2E test that creates invite → sends email → verifies token → completes registration

## Known Bugs

### UserAutoTrustService Missing Audit Log Entry
- **Issue:** Trust is granted to users automatically via `UserAutoTrustService.CheckAndApplyAutoTrustAsync()` but no audit log entry is created (only action record inserted)
- **Files:** `TelegramGroupsAdmin.Telegram/Services/UserAutoTrustService.cs` (lines 115-138)
- **Impact:** Audit log missing system-issued trust grants; security/compliance gap - admins cannot see which users were auto-trusted and when
- **Fix approach:** Call `IAuditService` in `UserAutoTrustService` after `InsertAsync()` to log the auto-trust action with same `Actor.AutoTrust` context; ensure consistency with manual trust logging
- **Severity:** High (audit trail integrity)

## Security Considerations

### Broad Exception Catching in File Scanning
- **Issue:** VideoFrameExtractionService, ClamAVScannerService, and multiple check services catch broad `Exception` types without specific error differentiation
- **Files:**
  - `TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs` (lines 209, 315, 396, 455, 510, 569)
  - `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` (lines 146, 241, 340)
  - `TelegramGroupsAdmin.ContentDetection/Checks/ThreatIntelContentCheckV2.cs` (lines 86, 154)
  - `TelegramGroupsAdmin.ContentDetection/Checks/VideoContentCheckV2.cs` (lines 173, 297, 417, 494, 615, 645)
- **Impact:** Potential security errors (invalid files, resource exhaustion, API failures) logged generically; malicious input patterns harder to detect; rate limiting on external APIs not differentiated
- **Mitigation:** Current approach logs errors with context and returns null/failure; jobs retry on failure. Improvement: Create specific exception types (`FileScanException`, `VideoProcessingException`) to classify errors
- **Fix approach:** Define domain-specific exception hierarchy; catch specific exceptions for recoverable errors vs. re-throw critical ones

### Configuration Merge Logic Uses JSON Serialization
- **Issue:** `ConfigService.MergeConfigs<T>()` uses `JsonSerializeToDocument()` → `JsonElement` dictionary → serialize back to merge configs
- **Files:** `TelegramGroupsAdmin.Core/Services/ConfigService.cs` (lines 403-431)
- **Impact:** Double serialization/deserialization overhead; property name transformations via `CamelCase` policy applied twice; potential for JSON edge cases with nested objects
- **Mitigation:** Works correctly for flat configs; no data loss observed. Improvement: Use reflection-based property merge for better type safety and performance
- **Fix approach:** Replace with `JsonNode` or reflection-based merge to avoid double serialization

## Performance Bottlenecks

### Training Sample Queries Limited by Configuration
- **Issue:** ImageContentCheckV2 and VideoContentCheckV2 limit training sample queries to config-defined count; no pagination or streaming for large datasets
- **Files:**
  - `TelegramGroupsAdmin.ContentDetection/Checks/ImageContentCheckV2.cs` (line with query limiting by config)
  - `TelegramGroupsAdmin.ContentDetection/Checks/VideoContentCheckV2.cs` (query limiting)
- **Impact:** For large training datasets (>10K images/videos), queries could materialize entire result set into memory via `.ToList()`
- **Mitigation:** Config limits set conservatively; typical homelab deployments won't exceed limits
- **Fix approach:** Implement streaming with pagination for sample queries; move limit logic to database query (TOP/LIMIT clause) instead of in-memory

### DetectionResultsRepository Batch Operations Use Multiple `.ToList()` Calls
- **Issue:** `DetectionResultsRepository` has multiple `.ToList()` calls in batch query methods (lines in DetectionResultsRepository.cs)
- **Files:** `TelegramGroupsAdmin.ContentDetection/Repositories/DetectionResultsRepository.cs`
- **Impact:** LINQ chains evaluated multiple times; materializes full result sets instead of streaming
- **Mitigation:** Batch sizes typically small (message detection results); performance acceptable for homelab scale
- **Fix approach:** Review and consolidate LINQ chains to single `.ToList()` call; use streaming alternatives for large batches

### CachedBlockedDomainsRepository Uses PostgreSQL UNNEST Bulk UPSERT
- **Issue:** Domain filter updates use raw SQL `UNNEST()` for bulk operations instead of EF Core bulk insert/update
- **Files:** `TelegramGroupsAdmin.ContentDetection/Repositories/CachedBlockedDomainsRepository.cs`
- **Impact:** Raw SQL avoids EF Core overhead but harder to maintain; schema changes require SQL updates
- **Mitigation:** Commented as "maximum performance" optimization; update frequency low (periodic blocklist sync); works well
- **Fix approach:** Document raw SQL rationale; add integration test to validate schema compatibility on migrations

## Fragile Areas

### Exception Handling with Task Cancellation Not Consistently Handled
- **Issue:** Many services catch `TaskCanceledException` separately but mix it with broad `Exception` handling
- **Files:**
  - `TelegramGroupsAdmin.ContentDetection/Checks/AIContentCheckV2.cs` (lines 138, 150)
  - `TelegramGroupsAdmin.ContentDetection/Services/UrlContentScrapingService.cs` (line 248)
- **Impact:** Cancellation tokens may not propagate properly if broad Exception catches happen first; shutdown/timeout scenarios may not clean up correctly
- **Mitigation:** Try/catch blocks structured to catch `TaskCanceledException` before `Exception`, so ordering is safe
- **Fix approach:** Extract exception handling to separate methods; document cancellation token lifecycle in service docstrings

### Moderation Service Boss/Worker Pattern Tight Coupling
- **Issue:** `BotModerationService` orchestrator knows all handler types; adding new handlers requires orchestrator changes
- **Files:** `TelegramGroupsAdmin.Telegram/Services/Moderation/` (all handler files)
- **Impact:** New domain features (e.g., `MuteHandler`) require coordination changes; hard to extend without affecting orchestrator
- **Mitigation:** Current pattern works well for ~5 handlers; documented in CLAUDE.md
- **Fix approach:** Consider service locator pattern for handlers only if count exceeds 10; current architecture acceptable for codebase size

### Database Migration Cascade Behavior Tests Pass But Schema Complex
- **Issue:** 57 migration files exist; complex relationships with exclusive arc patterns, partial unique indexes, and foreign key constraints
- **Files:** `TelegramGroupsAdmin.Data/Migrations/` (57 migration files)
- **Impact:** Schema mutations risky; migration tests validate but don't catch all edge cases (e.g., concurrent updates during migration)
- **Mitigation:** Integration tests use real PostgreSQL 18; migrations tested on every commit
- **Fix approach:** Add stress tests for concurrent writes during schema changes; document cascade behavior in schema diagrams

### Config Column Reflection-Based Empty Check
- **Issue:** `ConfigService.IsRecordEmpty()` uses reflection to check all string properties; static readonly PropertyInfo[] cached at class load
- **Files:** `TelegramGroupsAdmin.Core/Services/ConfigService.cs` (lines 381-397)
- **Impact:** If new config columns added to `ConfigRecordDto`, reflection must be re-cached; potential for stale PropertyInfo[] if new columns not picked up
- **Mitigation:** Works correctly currently; comment documents pattern
- **Fix approach:** Add unit test to verify PropertyInfo[] includes all string properties on ConfigRecordDto

## Scaling Limits

### Single Instance Constraint (Architectural, Not a Bug)
- **Issue:** Telegram Bot API enforces one active connection per bot token; multiple instances cause connection conflicts
- **Files:** All files; constraint documented in CLAUDE.md
- **Impact:** Cannot horizontally scale for higher throughput without bot service extraction
- **Mitigation:** Design acknowledged and accepted for homelab deployment (500-20,000 messages/day capacity)
- **Scaling path:** Would require extracting bot polling to separate container + message queue + distributed state (significant architecture change; not planned)

### In-Memory Caching No Distributed Cache
- **Issue:** `HybridCache` only works in single instance; no Redis/distributed cache layer
- **Files:** `TelegramGroupsAdmin.Core/Services/ConfigService.cs` (HybridCache usage throughout)
- **Impact:** Cache not shared across multiple instances if deployment scaled (constraint accepted for single-instance design)
- **Mitigation:** By design for homelab; sufficient for single-instance performance
- **Scaling path:** Add Redis layer if multi-instance required in future

### Database Query Result Sizes Not Capped
- **Issue:** Some queries (e.g., audit log pagination, message history) lack query result size limits in data layer
- **Files:** Repository implementations with `GetAll*` or unbounded SKIP/TAKE patterns
- **Impact:** Malicious actors requesting 1M rows could exhaust memory
- **Mitigation:** Pagination implemented at UI layer; API endpoints enforce page size limits
- **Fix approach:** Add query result caps at data layer; enforce `LIMIT 1000` on unbounded queries

## Dependencies at Risk

### VideoFrameExtractionService Depends on ffmpeg External Binary
- **Issue:** Video processing requires `ffmpeg` binary in container; no fallback if binary missing or incompatible version
- **Files:** `TelegramGroupsAdmin.ContentDetection/Services/VideoFrameExtractionService.cs`
- **Impact:** Video content detection fails silently if ffmpeg not installed; difficult to debug in production
- **Mitigation:** Docker image includes ffmpeg; health checks validate availability
- **Fix approach:** Add startup health check to verify ffmpeg binary exists and is runnable; throw exception on missing dependency

### ClamAV Scanner Transient Error Retries
- **Issue:** ClamAV connection errors trigger retry logic with hardcoded attempt counts (3 retries with exponential backoff)
- **Files:** `TelegramGroupsAdmin.ContentDetection/Services/ClamAVScannerService.cs` (line 146)
- **Impact:** Temporary ClamAV outages cause delays; no circuit breaker or fast-fail for sustained failures
- **Mitigation:** Logs transient errors; jobs re-queued by Quartz.NET on failure
- **Fix approach:** Add circuit breaker pattern after N consecutive failures; skip file scanning for period and resume after timeout

## Test Coverage Gaps

### AI Content Check Timeout Scenarios Not Fully Covered
- **Issue:** OpenAI API timeout handling caught but test coverage for timeout → retry logic not comprehensive
- **Files:** `TelegramGroupsAdmin.ContentDetection/Checks/AIContentCheckV2.cs` (line 138)
- **Impact:** Timeout handling may degrade under load; retry behavior unclear
- **Risk:** High (external API dependency failure)
- **Priority:** Medium - add integration test with OpenAI mock to verify retry + backoff behavior

### Notification Handler HTML Escaping Edge Cases
- **Issue:** `EscapeHtml()` uses `System.Net.WebUtility.HtmlEncode()` but doesn't validate output; potential for unexpected escaping patterns
- **Files:** `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs` (lines 253-256)
- **Impact:** Special characters may not display correctly in Telegram messages
- **Risk:** Medium - affects user-facing notifications
- **Priority:** Low - add unit test with Unicode characters + emoji to verify escape handling

### Quartz.NET Job Payload Extraction Not Fully Tested for Stale Triggers
- **Issue:** `JobPayloadHelper.TryGetPayloadAsync<T>()` designed to clean up stale trigger entries but cleanup logic not tested
- **Files:** Referenced in CLAUDE.md; implementation in BackgroundJobs project
- **Impact:** Stale trigger entries may accumulate in database
- **Risk:** Low - cleanup runs on each payload access; design is self-healing
- **Priority:** Low - add test to verify cleanup occurs after stale trigger detection

## Missing Critical Features

### No Built-in Rate Limiting for External APIs
- **Issue:** OpenAI, VirusTotal, and SendGrid API calls not rate-limited at application level; relies on external quota limits
- **Files:** Various service files (AIContentCheckV2, VirusTotalScannerService, SendGridEmailService)
- **Impact:** Burst requests could hit API quotas; no backpressure mechanism
- **Mitigation:** Logs warnings on rate limit hits; external services implement own quotas
- **Fix approach:** Implement circuit breaker + token bucket rate limiter; queue requests when approaching quota

### No Graceful Degradation for VirusTotal Service Unavailability
- **Issue:** File scanning fails if VirusTotal API unavailable; no fallback to ClamAV-only scanning
- **Files:** `TelegramGroupsAdmin.ContentDetection/Checks/FileScanningCheckV2.cs`
- **Impact:** If VirusTotal quota exhausted or service down, files marked as unscanned instead of using local scanner only
- **Mitigation:** ClamAV still scans locally; VirusTotal failures logged but not fatal
- **Fix approach:** Retry file scanning with ClamAV-only mode if VirusTotal fails; document fallback in logs

---

*Concerns audit: 2026-03-15*
