# Development Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-11-05
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 89/100 (Excellent - homelab-optimized)

This document tracks technical debt, performance optimizations, refactoring work, and deferred features.

---

## Feature Backlog

### DOCS-3: Configuration Guide

**Priority:** MEDIUM
**Impact:** Optimal spam detection tuning

**Needs:** Explain 9 algorithms, when to enable/disable, example configs (Strict/Moderate/Permissive), threshold tuning, file scanning config, auto-trust settings

---

### DOCS-5: Contextual Help System

**Priority:** MEDIUM
**Impact:** Reduces support burden

**Needs:** Help icons next to settings, tooltips, expandable sections, embedded examples

---

### DOCS-6: First-Time Setup Guidance

**Priority:** LOW
**Impact:** Reduces misconfiguration

**Needs:** Welcome modal, default configs, optional wizard, dashboard widget

---

### UX-1: Detection Explanation Enhancement

**Priority:** MEDIUM
**Impact:** Trust and transparency

**Enhancement:** Show contributing factors for each detection with threshold logic (e.g., "Banned: CAS 90%, Stop Words 85%, TF-IDF 73%")

---

### UX-2: Message Trends Analytics

**Priority:** MEDIUM
**Impact:** Complete analytics offering

**Needs:** Daily volume charts, per-chat breakdown, active users/day, peak hours heatmap, week-over-week growth, spam vs ham ratio over time

---

### ANALYTICS-1: Algorithm Reliability Dashboard

**Priority:** MEDIUM
**Impact:** Algorithm tuning confidence

**Shows:** Performance metrics per algorithm, false positive rates, average confidence, trends, recommendations (e.g., "Stop Words: 11.5% FP rate")

---

### ANALYTICS-2: Algorithm Recommendations

**Priority:** LOW
**Impact:** Automated optimization

**Auto-Suggest:** "CAS: 0 spam in 30 days - disable?", "OpenAI Vision: 98% accuracy - increase weight?", "Stop Words FP rate increased - review?"

---

### ANALYTICS-3: Export Features

**Priority:** LOW
**Impact:** External analysis, compliance

**Export to CSV/Excel:** Analytics data, user lists, audit logs, message history

---

### ML-1: Algorithm Weight Optimization

**Priority:** LOW
**Impact:** Fewer false positives

**Enhancement:** Learn optimal weights from correction history, auto-tune weekly

---

### ML-2: Community-Specific Pattern Mining

**Priority:** LOW
**Impact:** Personalized spam detection

**Feature:** Analyze banned messages, suggest stop words via TF-IDF, one-click add to config

---

### ML-3: Anomaly Detection for Raids

**Priority:** LOW
**Impact:** Early warning for coordinated attacks

**Detect:** Burst joins (15 in 5min), similar messages from multiple users, coordinated posting patterns

---

### ML-4: Bidirectional Threshold Optimization (Phase 2)

**Priority:** MEDIUM
**Impact:** Balanced spam detection, reduce both false positives AND false negatives

**Current State:** ML threshold optimizer (Phase 1) only considers false positives (OpenAI vetoes). Ignores false negatives (admin marks missed spam).

**Enhancement:** Incorporate admin manual overrides to calculate:
- **False Positives**: Auto spam → OpenAI veto OR admin marks ham
- **False Negatives**: Auto ham → Admin marks spam
- Calculate Precision, Recall, F1 score per threshold
- Optimize for F1-score (balanced) or configurable FP/FN cost ratio

**Open Questions:**
1. Optimization strategy: F1-Score (balanced), Cost-based (weighted FP/FN), or Target-Recall (safety-first)?
2. Minimum data requirements for each algorithm?
3. Show which messages would change at recommended threshold?
4. Allow admins to configure FP cost vs FN cost?

**Implementation:** Extend `ThresholdRecommendationService` to parse admin manual overrides from `detection_results` where `detection_source='manual'`, calculate confusion matrix per threshold, find optimal balance point.

---

### ML-5: Algorithm Performance Tracking

**Priority:** MEDIUM
**Impact:** Identify slow checks, trigger performance-based recommendations

**Phase 1: Data Collection**
- Ensure all `IContentCheck` implementations populate `ProcessingTimeMs` in `ContentCheckResponse`
- Use `Stopwatch.GetTimestamp()` for high-precision, low-overhead timing (not `Stopwatch.StartNew()`)
- Data already stored in `check_results_json.Checks[].ProcessingTimeMs` (no new table needed)

**Phase 2: Analytics UI**
- Add "Algorithm Performance Breakdown" section to `/analytics` → Performance Metrics
- Parse `check_results_json` from `detection_results` table
- Extract `ProcessingTimeMs` for each algorithm (by `CheckName` enum)
- Calculate per-algorithm metrics:
  - Average execution time
  - P95 execution time
  - Total time contribution (avg × frequency)
  - Slowest checks (identify outliers)
- Display in table with color-coded thresholds:
  - Green: <100ms average
  - Yellow: 100-500ms average
  - Red: >500ms average (⚠️ warning)

**Phase 3: Performance Recommendations**
- Track when total check time exceeds threshold (e.g., >200ms average)
- Identify contributing algorithms (e.g., "StopWords: 245ms, 73% of total time")
- Trigger recommendations:
  - "StopWords check is slow → Run ML-6 recommendations to reduce word count"
  - "OpenAI Vision P95: 3.4s → Consider disabling for low-risk chats"

**Example UI:**
```
Algorithm Performance (Last 30 Days)
┌────────────────────────────────────────────────┐
│ StopWords      Avg: 45ms   P95: 120ms    ✓    │
│ CAS            Avg: 12ms   P95: 35ms     ✓    │
│ Similarity     Avg: 87ms   P95: 215ms    ✓    │
│ OpenAI         Avg: 1.2s   P95: 3.4s     ⚠️   │
│ ImageSpam      Avg: 890ms  P95: 2.1s     ⚠️   │
└────────────────────────────────────────────────┘
```

**Technical Notes:**
- Reuse existing `AnalyticsRepository` with new query method
- Parse JSON using `CheckResultsSerializer.Deserialize()` (already handles both formats)
- Group by `CheckName` enum for aggregation
- Filter to successful checks only (exclude errors)

---

### ML-6: Stop Words ML Recommendations

**Priority:** MEDIUM
**Impact:** Automated stop word maintenance, reduce false positives, improve performance

**Algorithm:**

**Words to ADD (Spam-Only Candidates):**
1. Extract all words from spam training samples (`detection_results` WHERE `is_spam=true` AND `used_for_training=true`)
2. Count frequency across spam corpus
3. Count same words across ALL legitimate messages (`messages` WHERE `is_spam=false`)
4. Calculate spam-to-legit ratio: `spamFreq / (legitFreq + 1)`
5. Filter candidates:
   - Spam frequency ≥ 5% (appears in at least 5% of spam messages)
   - Legit frequency < 1% (rare in legitimate messages)
   - Not already in `stop_words` table
   - Minimum spam sample size: 50 messages (fail-fast if insufficient)
6. Rank by ratio (highest = best candidates)

**Words to REMOVE (Low Precision / Dead Weight):**
1. For each existing stop word, parse `check_results_json` to find when StopWords check triggered
2. Cross-reference against message outcomes:
   - **Correct**: Stop word triggered → message was spam (`is_spam=true`)
   - **False Positive**: Stop word triggered → message was ham (`is_spam=false` OR vetoed by OpenAI)
3. Calculate precision: `correctTriggers / (correctTriggers + falsePositives)`
4. Recommend removal if:
   - Precision < 70% (causes too many false positives)
   - Never triggered in last 30 days (dead weight)
   - Total triggers < 5 (insufficient data)

**Performance-Based Cleanup (Triggered by ML-5):**
- When `StopWords` check average time exceeds threshold (e.g., >200ms)
- Calculate inefficiency score: `(falsePositiveRate × executionCost) / (precision + 0.01)`
- Rank all stop words by inefficiency (worst performers first)
- Recommend removing bottom N words to bring execution time back to target (<150ms)
- Estimate time savings per word removed

**UI Design (Single Dialog):**
```
┌────────────────────────────────────────────────┐
│ Stop Word Recommendations                      │
│                                                │
│ [Refresh Analysis]  Last updated: 2 hours ago │
│                                                │
│ ⚠️ Performance Warning: StopWords check        │
│    averaging 245ms (threshold: 200ms).         │
│    Consider removing low-precision words.      │
│                                                │
│ ┌─ Suggested Additions (12) ────────────────┐ │
│ │ crypto (89% spam / 2% legit, ratio: 44x)  │ │
│ │ investment (76% spam / 5% legit, 15x)     │ │
│ │ earn (71% spam / 8% legit, 9x)            │ │
│ │ ... [+9 more]                             │ │
│ └──────────────── [Add All 12 Words] ───────┘ │
│                                                │
│ ┌─ Suggested Removals (3) ───────────────────┐│
│ │ free (12% precision, 34 false positives)  │ │
│ │ join (45% precision, 18 false positives)  │ │
│ │ welcome (never triggered, 0 spam caught)  │ │
│ └──────────────── [Remove All 3 Words] ─────┘ │
│                                                │
│ ⚠️ Performance Batch: Removing 8 least        │
│    effective words will reduce time by ~50ms  │
│ ┌─ Performance Cleanup (8) ──────────────────┐│
│ │ and (2% precision, 89 false positives)    │ │
│ │ the (5% precision, 67 false positives)    │ │
│ │ ... [+6 more]                             │ │
│ └────────────── [Remove Performance Batch] ─┘ │
│                                                │
│                              [Close]           │
└────────────────────────────────────────────────┘
```

**Confirmation Dialog (All Actions):**
```
Confirm Stop Word Changes

Add 12 words:
  crypto, investment, earn, ... (+9)

Remove 3 words:
  free, join, welcome

✓ Changes will apply to new messages
✓ Estimated performance: -15ms/check

[Confirm] [Cancel]
```

**Data Volume Validation:**
```csharp
// Before analysis, fail-fast if insufficient data
var spamSampleCount = await _db.DetectionResults
    .Where(d => d.IsSpam && d.UsedForTraining)
    .CountAsync();

var legitMessageCount = await _db.Messages
    .Where(m => !m.IsSpam)
    .CountAsync();

if (spamSampleCount < 50 || legitMessageCount < 100)
{
    return Error("Insufficient data: Need ≥50 spam samples and ≥100 legitimate messages");
}
```

**Implementation:**
- New service: `StopWordRecommendationService` (follows `ThresholdRecommendationService` pattern)
- Reuse `ITokenizerService` for word extraction
- Store recommendations in `threshold_recommendations` table (reuse existing schema)
- Settings → Stop Words → [View Recommendations] button opens dialog
- Single [Confirm] for all changes (bulk add + bulk remove)

**Edge Cases:**
- Common words ("the", "and") have high legit frequency → ratio low → won't be recommended ✓
- Rare spam-only words ("c0in", "crypt0") have high ratio → good candidates ✓
- Overly broad stop words ("free") show low precision → recommend removal ✓
- Performance batch only appears when StopWords check is slow (>200ms avg)

**Testing:**
- Unit tests for word extraction, frequency calculation, ratio scoring
- Integration tests with known spam/ham corpus
- Performance tests to validate timing estimates

---

### FEATURE-5.3: Migrate API Keys to Database with UI Management

**Priority:** HIGH - Better architecture, removes env var dependency
**Impact:** All API keys managed via encrypted database storage with full UI controls

**Architecture Changes:**

**Encrypted Secrets (`configs.api_keys` JSONB):**
- OpenAI API Key
- VirusTotal API Key
- SendGrid API Key
- Telegram Bot Token

**Plain Config (separate JSONB columns):**
- `sendgrid_config`: {FromEmail, FromName, Enabled}
- `telegram_config`: Already exists, just add UI
- `openai_config`: {MaxTokens, AvailableModels[]} - global settings
- Per-chat: Model selection in spam_detection_config (dropdown from cached models)

**OpenAI Model Management:**
- Store cached model list in `openai_config.AvailableModels`
- "Refresh Model List" button calls OpenAI `/v1/models` API
- Filter to chat models only (gpt-*, exclude embeddings/whisper)
- Pre-seed with current models: gpt-4o, gpt-4o-mini, gpt-3.5-turbo, gpt-4-turbo
- Per-chat model selection from cached list (no code changes for new models)

**Settings UI Reorganization:**
1. **System → Integrations** (infrastructure secrets + config):
   - SendGrid: API Key (encrypted), FromEmail, FromName, Enabled toggle
   - Telegram Bot: Token (encrypted)

2. **Protection → External Services** (spam/security):
   - OpenAI: API Key (encrypted), MaxTokens, Refresh Models button
   - VirusTotal: API Key (encrypted)

**All fields editable with show/hide toggles for secrets**

**Migration Strategy:** Option B - DB-only, no env var migration
- Remove `ApiKeyMigrationService`
- Remove env var fallback from `ApiKeyDelegatingHandler`
- Features disabled until configured via UI
- Fresh installs: configure via UI only

**Result:** Clean separation of secrets/config, no env var complexity, UI-driven configuration

---

### REFACTOR-7: Remove Unused File Scanning Services

**Priority:** MEDIUM - Code cleanup
**Impact:** Reduces maintenance burden, simplifies file scanning architecture

**Context:** Currently support 4 cloud file scanning services (VirusTotal, MetaDefender, HybridAnalysis, Intezer). VirusTotal is industry standard and sufficient for our needs.

**Tasks:**
1. Delete scanner service classes: MetaDefenderScannerService, HybridAnalysisScannerService, IntezerScannerService
2. Delete config classes: MetaDefenderConfig, HybridAnalysisConfig, IntezerConfig
3. Update Tier2QueueCoordinator - remove 3 services from constructor/dictionary
4. Update ServiceCollectionExtensions - remove DI registrations
5. Update ApiKeyDelegatingHandler - remove key loading
6. Update ApiKeyMigrationService - remove migration logic
7. Update ApiKeysConfig - remove properties
8. Update Tier2Config - update CloudQueuePriority defaults to only ["VirusTotal"]
9. Consider FileScanQuotaRecord - remove columns (may need migration)
10. Update docs (CLAUDE.md, FILE_SCANNING.md)

**Result:** Single VirusTotal integration for file scanning, cleaner codebase

---

### DEPLOY-1: Docker Compose Simplicity Validation

**Priority:** HIGH - Blocking for GitHub migration
**Impact:** Deployment friction

**Audit:** .env.example completeness, CHANGEME markers, no complex args, standalone operation, extensible defaults

---

### DEPLOY-2: Troubleshooting Documentation

**Priority:** MEDIUM
**Impact:** Support burden reduction

**Add to README:** Common issues (ClamAV, bot offline, DB connection), health check commands, log debugging, permission errors

---

### DEPLOY-3: Evaluate PostgreSQL 18 Upgrade

**Priority:** LOW - Wait for ecosystem maturity (target: March-April 2026)
**Impact:** Performance improvements (async I/O, JSON SIMD), data integrity checksums

**Current Status:** PostgreSQL 18 released Sept 25, 2025 (6 weeks old). All tests pass (22/22 migrations + 20/20 backup tests validated with Testcontainers).

**Blockers:**
- Wait for PostgreSQL 18.2 or 18.3 point release (bug fixes stabilized)
- Wait for EF Core and Npgsql official support statements
- Breaking change: Data checksums enabled by default (requires matching settings for pg_upgrade)

**Preparation Steps:**
1. Monitor Npgsql release notes for PostgreSQL 18 support announcement
2. Monitor EF Core 9/10 compatibility matrix
3. Enable checksums on PostgreSQL 17 cluster before upgrade: `pg_checksums --enable -D /var/lib/postgresql/data` (requires downtime)
4. Re-run Testcontainers test suite against postgres:18 image before production migration

**Benefits:**
- Async I/O subsystem: Faster sequential scans, VACUUM, bitmap heap scans
- JSON SIMD: Minimal benefit for small JSONB configs (~1 KB), but future-proofing
- Data checksums: Corruption detection (good for homelab hardware)
- Enhanced RETURNING: Better change tracking capabilities

**Notes:** No killer features for current workload. Upgrade when mature, not urgent.

---

### SCHEMA-2: Migrate Data Protection Purpose String

**Priority:** LOW - Cosmetic issue
**Impact:** Old project name in encryption purpose string

**Migration:** Decrypt all protected fields with old purpose, re-encrypt with `TelegramGroupsAdmin.Secrets` (affects users.totp_secret, configs.api_keys, configs.passphrase_encrypted)

---

### FEATURE-4.23: Cross-Chat Ban Message Cleanup

**Priority:** MEDIUM
**Impact:** Complete spam removal after cross-chat ban

**Enhancement:** When high-confidence spam (>90% or OpenAI veto) triggers cross-chat ban, delete ALL existing messages from that user via TickerQ job (Telegram API 48hr limit)

---

### QUALITY-1: Nullable Reference Type Hardening

**Priority:** MEDIUM
**Impact:** Bug prevention, explicit intent

**Phase 1:** Enable TreatWarningsAsErrors in 4 remaining projects (main app, Configuration, Telegram, Telegram.Abstractions)
**Phase 2:** Fix nullable warnings (focus CS8600/CS8602/CS8603 first, prioritize hot paths)

---

### FEATURE-4.20: Auto-Fix Database Sequences on Backup Restore

**Status:** DEFERRED ⏸️
**Priority:** LOW - Monitor and revisit if recurs
**Impact:** Data integrity (sequence drift between restores, root cause unknown)

**Decision:** Deferred - backup/restore preserves sequences correctly. Test coverage added (SequenceIntegrityTests.cs). Will implement startup validation if problem recurs.

---

### FEATURE-5.2: Universal Notification Center

**Priority:** MEDIUM
**Impact:** In-app notifications for background jobs

**Phase 1 (In-Memory):** NotificationBell component with ProtectedLocalStorage, event-driven via Blazor Server circuit
**Phase 2 (Database):** Persistent notifications table, browser notification API via JS interop, cross-device sync

**Use Cases:** Backup rotation, file scans, job failures, system alerts

---

### FEATURE-4.9: Bot Configuration Hot-Reload

**Priority:** LOW
**Impact:** Developer experience, deployment flexibility

**Enhancement:** Implement IOptionsMonitor for runtime config updates (new chats, bot token rotation, thresholds) without restart

---

### FEATURE-5.4: OpenAI Moderation API for Content Safety (Post-Open Source)

**Priority:** VERY LOW - Post-open source, community-focused feature
**Impact:** Optional content safety layer for groups with strict moderation policies

**Context:** OpenAI Moderation API (`/v1/moderations`) is FREE and detects content policy violations (violence, hate speech, harassment, self-harm, sexual content) across 40 languages with 95% accuracy. This is NOT for spam detection - it's for content safety/community guidelines enforcement.

**Key Design Principles:**

- **Always runs on ALL messages** (trusted users, admins, everyone) - content safety is universal
- **Human-in-the-loop required** - Results go to manual review queue, never auto-ban
- **Context-aware** - AI moderation can be heavy-handed; groups have different norms (gaming communities vs. professional groups)
- **Opt-in per chat** - Disabled by default, admins explicitly enable for their community standards

**Architecture:**

1. New check: `OpenAIModerationCheck` (separate from spam detection pipeline)
2. Configuration: `ModerationConfig` with per-category thresholds (violence, hate, harassment, etc.)
3. Review queue: New "Content Safety" tab showing flagged messages with category scores
4. Per-chat toggle: Settings → Chat Moderation → "Enable Content Safety Review"

**Use Cases:**

- Family-friendly communities (detect inappropriate content)
- Professional groups (workplace harassment detection)
- Educational communities (prevent bullying)
- Gaming groups with strict codes of conduct

**Why Post-Open Source:** Community-driven feature - different groups have vastly different moderation philosophies. Better to gather feedback from real users before building.

---

### TOOLS-1: Complete File Scanning Integration in Content Tester

**Priority:** LOW
**Impact:** Testing/debugging convenience (file scanning works in production, just not in test tool)

**Current State:**
- Content Tester UI has file upload field but doesn't actually pass files to scanner
- `_selectedFile` field exists but is never sent to `ContentCheckCoordinator`
- File scanning works fine in production (real Telegram messages)
- UI shows alert: "File scanning integration is not yet complete (Phase 4.14)"

**Action:**
1. Add `DocumentData: Stream?` property to `ContentCheckRequest.cs`
2. Add `DocumentFileName: string?` property to `ContentCheckRequest.cs`
3. Update `ContentTester.razor` to save `_selectedFile` to temp path and pass to coordinator
4. Update `ContentCheckCoordinator` to handle document scanning if `DocumentData` provided
5. Remove the "not yet complete" alert from UI after implementation
6. Test with eicar.com test malware file

**Files:**
- `/TelegramGroupsAdmin.ContentDetection/Models/ContentCheckRequest.cs`
- `/TelegramGroupsAdmin/Components/Shared/ContentDetection/ContentTester.razor`
- `/TelegramGroupsAdmin.ContentDetection/ContentCheckCoordinator.cs`

**Testing:** Upload eicar.com to Content Tester, verify malware detection appears in results

---

## Bugs

(No open bugs)

---

## Performance Optimization Issues

**Deployment Context:** 10+ chats, 1000+ users, 100-1000 messages/day, Messages page primary moderation tool

### Medium Priority (0 issues)

(No medium priority performance issues)

---

## Performance Optimization Summary

**Deployment Context:** 10+ chats, 100-1000 messages/day (10-50 spam checks/day on new users), 1000+ users, Messages page primary tool

**Total Issues Remaining:** 0 (PERF-3 fully complete)

**Implementation Priority:** Implement opportunistically during related refactoring work

**Removed Items:**
- **PERF-DATA-5** (JSON source generation): Inconsistent with reflection usage in backup/restore, negligible benefit with existing caching (PERF-CFG-1), premature optimization
- **PERF-CD-1** (Stop words N+1): Empirical testing (2025-10-21) disproved original estimates by 100-300x. Actual: 0.3ms baseline → 1.5ms at 92x scale. PostgreSQL hash joins handle this perfectly. Original analysis confused LEFT JOINs with N+1 queries. Solution in search of a problem.

---

### DI-2: Create Interfaces for InviteRepository and VerificationTokenRepository

**Status:** BACKLOG 📋
**Severity:** Best Practice | **Impact:** Consistency, testability
**Discovered:** 2025-10-28 via DI audit

**Current State:**
- InviteRepository and VerificationTokenRepository registered as concrete types
- Inconsistent with rest of codebase (109 other services use interface-based DI)
- Both are actively used repositories in TelegramGroupsAdmin/Repositories/

**Work Required:**
- Create IInviteRepository interface (3 methods)
- Create IVerificationTokenRepository interface (4 methods)
- Update DI registrations in ServiceCollectionExtensions.cs (2 lines)
- Update consuming code to inject interfaces

**Priority:** LOW - Consistency improvement, not blocking

---

### CODE-5: Fire-and-Forget Error Handling

**Priority:** MEDIUM
**Impact:** Silent failures in 4 fire-and-forget tasks

**Action:** Wrap all `_ = Task.Run(...)` in try-catch with logging (MessageProcessingService x2, SpamActionService, IntermediateAuthService)

---

### CODE-6: Extract Magic Numbers to Configuration

**Priority:** LOW
**Action:** Extract hardcoded retention periods, timeouts, thresholds to configuration classes (MessageHistoryOptions, etc.)

---

### CODE-7: Modernize to C# 12 Collection Expressions

**Priority:** LOW
**Action:** Replace 67× `new List<T>()` with `[]` syntax (requires explicit types, no `var`)

---

### QUALITY-2: Implement HybridCache

**Priority:** MEDIUM
**Impact:** Performance improvement for homelab

**Action:** Wire up HybridCache (v9.0.0) for spam detection config, admin status, blocklist domains, tag/note data


---

### PERF-2: Add Performance Telemetry

**Priority:** MEDIUM
**Impact:** Validate production metrics, identify regressions

**Action:** Add OpenTelemetry for spam detection duration, analytics queries, page load, background jobs


---

### REFACTOR-3: Extract MessageHistoryRepository Services

**Priority:** CRITICAL
**Impact:** Complex query testing in isolation

**Extract:**
- `MessageQueryService` - Pagination, filtering
- `MessageTranslationService` - Translation CRUD
- `MessageEditService` - Edit history

---

### REFACTOR-4: Extract WelcomeService Components

**Priority:** CRITICAL
**Impact:** Pure function testing, message building without Telegram API

**Extract:**
- `WelcomeMessageBuilder` - Message formatting (pure functions)
- `WelcomeVerificationService` - Verification flows
- `WelcomeTimeoutService` - Timeout handling

---

### REFACTOR-5: Extract ModerationActionService Handlers

**Priority:** HIGH
**Impact:** Moderation logic testability

**Extract:**
- `CrossChatBanService` - Cross-chat coordination
- `TrustManagementService` - Trust/untrust logic
- `WarningService` - Warning thresholds

---

### REFACTOR-7: Extract AuthService Components

**Priority:** MEDIUM
**Impact:** Auth testing in isolation

**Extract:**
- `TotpService` - TOTP generation/validation
- `RecoveryCodeService` - Recovery code management
- `EmailVerificationService` - Email verification

---

### REFACTOR-8: Extract ChatManagementService Components

**Priority:** MEDIUM
**Extract:**
- `AdminCacheService` - Admin cache management
- `ChatHealthCheckService` - Health check logic

---

### REFACTOR-9: Extract TelegramUserRepository Services

**Priority:** MEDIUM
**Extract:**
- `UserPhotoService` - Photo download/hash
- `UserSyncService` - User synchronization

---

### REFACTOR-10: Extract ContentDetectionEngine Services

**Priority:** MEDIUM
**Extract:**
- `CheckAggregationService` - Result aggregation
- `ConfigurationLoader` - Config loading/caching

---

### REFACTOR-11: Split AnalyticsRepository by Type

**Priority:** MEDIUM
**Extract:**
- `SpamAnalyticsQueries`
- `WelcomeAnalyticsQueries`
- `UserAnalyticsQueries`

---

### REFACTOR-12: Extract SpamActionService Components

**Priority:** MEDIUM
**Extract:**
- `TrainingQualityService` - Training sample quality
- `AutoBanService` - Automatic ban logic
- `ReportGenerationService` - Report creation

---

### REFACTOR-14: Extract DetectionResultsRepository Components

**Priority:** MEDIUM
**Extract:**
- `FalsePositiveTracker` - FP/FN tracking
- `AccuracyCalculator` - Accuracy calculation

---

### REFACTOR-15: Extract AppDbContext EntityTypeConfigurations

**Priority:** LOW
**Note:** Only if DbContext grows > 800 lines

---

### REFACTOR-17: Make Backup Tests Dynamic for Table Changes

**Priority:** LOW
**Impact:** Reduce test brittleness when adding new tables

**Current Issue:** BackupServiceTests hardcode `GoldenDataset.TotalTableCount = 32`, which breaks every time a new table is added. Discovered during ML-5 (image_training_samples) - required manual update.

**Proposed Solution:**
- Option A: Query AppDbContext DbSets at runtime, count exportable tables dynamically (exclude: __EFMigrationsHistory, file_scan_*, pending_notifications, ticker.*)
- Option B: Use reflection to count properties in GoldenDataset class (each table has a nested class)
- Option C: Query actual backup metadata and validate table names instead of count

**Affected Tests:**
- `ExportAsync_ShouldIncludeAllExpectedTables` - Asserts table count = 32
- `GetMetadataAsync_FromEncryptedBackup_ShouldReturnMetadata` - Asserts table count = 32

**Recommendation:** Option A - dynamically count DbSets minus excluded tables. More maintainable, catches regressions if tables accidentally excluded.

---

## Refactoring Summary

**Key Principle:** Extract with testing in mind - pure functions, dependency injection, single responsibility.

**Total:** 15 files across 3 priority tiers

**Priority Order:**
1. REFACTOR-1 to REFACTOR-4 (Critical: >1000 lines)
2. REFACTOR-5 to REFACTOR-6 (High: 900-1000 lines)
3. REFACTOR-7 to REFACTOR-14 (Medium: 500-700 lines)
4. REFACTOR-15 (Low: Optional)

**Success Criteria (All):**
- ✅ All files under 500 lines (ideally under 300)
- ✅ Each extracted class has single responsibility
- ✅ Pure functions extracted (no side effects = easy testing)
- ✅ Dependencies injected (mockable for tests)
- ✅ Complex logic isolated from I/O operations
- ✅ Method complexity < 10 (cyclomatic complexity)
- ✅ Unit tests added as we extract (NOT after)

**Related:** Once refactored, enables QUALITY-1 (Nullable reference hardening) and comprehensive unit test suite

---

### REFACTOR-16: BackupMetadata.CreatedAt Should Use DateTimeOffset

**Priority:** LOW
**Impact:** Type safety, consistency with codebase

**Change:** `BackupMetadata.CreatedAt` from `long` → `DateTimeOffset` (breaking change, requires migration logic for existing backups)

---

### REFACTOR-18: Extract Interfaces for Integration Testing

**Priority:** MEDIUM
**Impact:** Testability, dependency injection design

**Motivation:** Integration test attempt (2025-10-31) revealed MessageProcessingService is too coupled to infrastructure for proper testing. Need interface extraction to enable mocking/stubbing during tests.

**Required Changes:**

**REFACTOR-18-1: Extract Job Scheduling Interface**
- Extract `IJobScheduler` interface from direct TickerQ calls
- Methods: `ScheduleAsync<TPayload>(functionName, payload, delaySeconds, retries)`
- Implementation: `TickerQJobScheduler` (wraps TickerQUtilities.ScheduleJobAsync)
- Test implementation: `InMemoryJobScheduler` (tracks scheduled jobs without executing)
- **Impact:** Enables testing services that schedule jobs (file scanning, welcome timeouts, etc.)

**REFACTOR-18-2: Extract Bot Admin Cache Interface**
- Extract `IBotAdminCache` interface from ChatManagementService
- Methods: `GetAdminsAsync(chatId)`, `RefreshAdminsAsync(chatId)`, `IsChatHealthy(chatId)`
- Implementation: `TelegramBotAdminCache` (wraps existing ChatManagementService logic)
- Test implementation: `InMemoryBotAdminCache` (simple dictionary-based)
- **Impact:** Eliminates NullReferenceException when MessageProcessingService tries to refresh admins

**REFACTOR-18-3: Wrap ITelegramBotClient for Better Testing**
- Create `ITelegramBotService` interface with commonly-mocked methods
- Methods: `GetChatAdministratorsAsync`, `SendTextMessageAsync`, `DeleteMessageAsync`, etc.
- Implementation: `TelegramBotService` (thin wrapper over ITelegramBotClient)
- **Impact:** Easier to mock Telegram API calls without complex NSubstitute configurations

**Testing Prerequisite:** These refactors unblock integration tests for:
- MessageProcessingService (complete vertical slices: message → handlers → DB → events)
- SpamActionService (auto-ban, training QC, borderline reports)
- Background services orchestration

**Blocked By:** None
**Blocks:** Integration test suite for REFACTOR-1 through REFACTOR-4

---

### ARCH-3: Consolidate Audit System to Core

**Priority:** MEDIUM
**Impact:** Code organization, enum duplication fix

**Action:** Move AuditEventType, AuditLogRecord, IAuditService from Telegram → Core. Keep user_actions in Telegram (Telegram-specific). Sync duplicated enums.


---
