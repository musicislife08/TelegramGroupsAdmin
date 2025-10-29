# Development Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-29
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 89/100 (Excellent - homelab-optimized)

This document tracks technical debt, performance optimizations, refactoring work, and deferred features.

---

## Feature Backlog

### SECURITY-1: Git History Sanitization (Pre-Open Source)

**Priority:** CRITICAL - Blocking for GitHub migration
**Impact:** launchSettings.json with secrets in 10+ commits

**Actions:**
1. Use BFG Repo-Cleaner to purge launchSettings.json from all history
2. Audit for other secrets (compose.yml, http-client.private.env.json)
3. Force push to Gitea, re-clone on dev/prod
4. Add pre-commit hooks (detect-secrets or git-secrets)

---

### SECURITY-3: CSRF Protection on API Endpoints

**Priority:** MEDIUM
**Impact:** Defense-in-depth for state-changing operations (LOW risk for homelab)

**Action:** Add antiforgery validation to 5 POST endpoints (logout, register, resend-verification, forgot-password, reset-password)


---

### SECURITY-4: Backup Passphrase Logging Audit

**Priority:** LOW - Preventative measure, no known leaks
**Impact:** Prevent passphrase exposure in logs/exceptions

**Action:** Audit BackupService, BackupEncryptionSetupDialog, BackupPassphraseRotationDialog, RotateBackupPassphraseJob for passphrase logging. Replace with `[REDACTED]` in all log statements.

---

### DOCS-1: README.md (Pre-Open Source)

**Priority:** HIGH - Blocking for GitHub migration
**Impact:** First impression, adoption rate

**Needs:** Project description, screenshots, quick start, comparison to alternatives, community links

---

### DOCS-2: Setup Guide (Pre-Open Source)

**Priority:** HIGH - Blocking for GitHub migration
**Impact:** User onboarding success

**Needs:** Docker Compose walkthrough, .env.example explanation, first user setup, health check verification

---

### DOCS-3: Configuration Guide

**Priority:** MEDIUM
**Impact:** Optimal spam detection tuning

**Needs:** Explain 9 algorithms, when to enable/disable, example configs (Strict/Moderate/Permissive), threshold tuning, file scanning config, auto-trust settings

---

### DOCS-4: LICENSE File (Pre-Open Source)

**Priority:** HIGH - Blocking for GitHub migration
**Impact:** Usage clarity, contribution safety

**Decision:** MIT vs AGPL

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


## Bugs

(No open bugs)

---

## Completed Work

**2025-10-29**: SECURITY-5 (Settings page authorization bypass fixed - GlobalAdminOrOwner policy + Owner-only infrastructure checks), SECURITY-6 (User management permission checks - GlobalAdmin can manage users, escalation prevention), cSpell configuration (29 domain terms, 0 spell warnings), Interface splits (3 files: WelcomeResponsesRepository, BotProtectionService, WelcomeService), REFACTOR-6 (ModelMappings 884 lines → 26 files in Mappings/ subdirectory, 69 files changed)

**2025-10-28**: CODE-8 (Removed 157× ConfigureAwait - unnecessary in ASP.NET Core), DI-1 audit (175 registrations, 66 concrete-only justified, created DI-2 for 2 inconsistent repos), REFACTOR-2 (BackupService 1,202 → 750 lines, 4 handlers + 2 services extracted, breaking changes, 20/20 tests), PERF-3 (trust context early exit), REFACTOR-13 (OpenAI extraction, 40/40 tests)

**2025-10-27**: CODE-9 (Removed reflection in MessageProcessingService - extracted CheckResultsSerializer static utility, compile-time safe, no performance overhead), CODE-1 + CODE-2 (Complete code organization overhaul - split ~60 files into 140+ individual files, fixed 7 name mismatches, renamed TotpProtectionService→DataProtectionService, one type per file achieved, 164 files changed), ANALYTICS-4 (Welcome system analytics - 4 new repository methods, WelcomeAnalytics.razor component, /analytics#welcome tab with join trends/response distribution/per-chat stats, timezone-aware queries), SCHEMA-1 (Audit log FK cascade rules fixed - migration 20251027002019, user deletion now works, 22/22 tests passing)

**2025-10-26**: SECURITY-2 (Open redirect vulnerability fixed - UrlHelpers.IsLocalUrl() validation on all auth redirects), BUG-LOGOUT (Missing /logout page - existed since Oct 6, found by user on Cloudflare tunnel exposure)

**2025-10-25**: BUG-1 (False negative tracking - analytics now shows both FP and FN rates with overall accuracy metrics)

**2025-10-24**: ARCH-2 (Actor exclusive arc migration complete - audit_log table migrated with 6 new columns, 35+ call sites updated, UI updated, 84 rows migrated successfully)
**2025-10-22**: PERF-CD-3 (Domain stats: 2.24× faster, 55ms→25ms via GroupBy aggregation), PERF-CD-4 (TF-IDF: 2.59× faster, 44% less memory via Dictionary counting + pre-indexed vocabulary)
**2025-10-21**: ARCH-1 (Core library - 544 lines eliminated), ARCH-2 (Actor refactoring partial - moderation tables only), PERF-APP-1, PERF-APP-3, DI-1 (4 repositories), Comprehensive audit logging coverage, BlazorAuthHelper DRY refactoring (19 instances), Empirical performance testing (PERF-CD-1 removed via PostgreSQL profiling)
**2025-10-19**: 8 performance optimizations (Users N+1, config caching, parallel bans, composite index, virtualization, record conversion, leak fix, allocation optimization)

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

### CODE-10: Remove Pre-Release "Legacy" Code

**Priority:** MEDIUM - No users = no legacy
**Impact:** Code clarity before release

**Remove:** 14 "legacy" code paths across WebApplicationExtensions, ScheduledBackupJob, AuthService, BackupService, Messages.razor, DetectionHistoryDialog, PermissionDialog, SimilaritySpamCheck, ContentDetectionEngine, SpamCheckRecord, ImageViewerDialog, ManagedChatType

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

### ARCH-3: Consolidate Audit System to Core

**Priority:** MEDIUM
**Impact:** Code organization, enum duplication fix

**Action:** Move AuditEventType, AuditLogRecord, IAuditService from Telegram → Core. Keep user_actions in Telegram (Telegram-specific). Sync duplicated enums.


---
