# Development Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-26
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 89/100 (Excellent - homelab-optimized)

This document tracks technical debt, performance optimizations, refactoring work, and deferred features.

---

## Feature Backlog

### SECURITY-1: Git History Sanitization (Pre-Open Source)

**Status:** BACKLOG 📋 **CRITICAL - Blocking for open source**
**Severity:** Security | **Impact:** Repository security, credential protection

**Current State:**
- launchSettings.json appears in 10+ commits in git history
- File contains potential secrets (connection strings, API keys, environment-specific configs)
- NOW in .gitignore (good) but historical commits still contain it (bad)
- Git history is permanent - once pushed to public GitHub, secrets are exposed forever

**Security Risk:**
- API keys, database credentials, tokens in git history
- GitHub scanning bots will find exposed secrets within minutes of public push
- Cannot be undone easily once public (requires force push to all forks)
- Credential leaks = immediate security breach requiring key rotation

**Proposed Solution:**
1. **Use BFG Repo-Cleaner** (recommended) to purge launchSettings.json from all history
2. **Secret scanning audit** - grep for hardcoded credentials, API keys, tokens
3. **Force push** to Gitea (requires coordination with production machine)
4. **Re-clone** on both dev and production machines
5. **Add pre-commit hooks** to prevent future secret commits (detect-secrets or git-secrets)

**Implementation Steps:**
```bash
# Backup repo first
cp -r TelegramGroupsAdmin TelegramGroupsAdmin-backup

# Remove launchSettings.json from all history
bfg --delete-files launchSettings.json TelegramGroupsAdmin

# Clean up
cd TelegramGroupsAdmin
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# Force push
git push --force --all origin
git push --force --tags origin
```

**Verification:**
- `git log --all --full-history -- "*launchSettings.json"` returns no results
- `git log -p | grep -i "password\|apikey"` finds no hardcoded secrets
- Fresh clone has clean history

**Files to Audit:**
- TelegramGroupsAdmin/Properties/launchSettings.json (confirmed in 10+ commits)
- Any other launchSettings.json in sub-projects
- compose/compose.yml (contains Telegram bot token, OpenAI, VirusTotal, SendGrid API keys)
- TelegramGroupsAdmin/http-client.private.env.json (contains API keys for HTTP testing)
- Hardcoded secrets in code (connection strings, API keys)

**Priority:** HIGH - Must complete before GitHub migration

**Effort:** 1-2 hours

**Related Work:**
- Document secret management in README (environment variables only)
- Add pre-commit hooks to block future secret commits

---

### SECURITY-3: CSRF Protection on API Endpoints

**Status:** BACKLOG 📋
**Severity:** Security | **Impact:** Defense-in-depth for state-changing operations
**Discovered:** 2025-10-26 via security agent code review

**Current State:**
- `app.UseAntiforgery()` enabled in pipeline (WebApplicationExtensions.cs:40)
- API endpoints don't validate antiforgery tokens
- Cookie auth with `SameSite=Lax` provides partial protection

**Risk Assessment:**
- **LOW risk** for homelab deployment (Blazor Server uses SignalR/WebSocket)
- **MEDIUM risk** if API endpoints called from JavaScript/AJAX
- SameSite=Lax prevents most CSRF attacks but lacks defense-in-depth

**Proposed Solution:**
Add antiforgery validation to state-changing endpoints:
```csharp
endpoints.MapPost("/api/auth/logout", async (HttpContext context, ...) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    await antiforgery.ValidateRequestAsync(context);
    // ... rest of handler
}).RequireAuthorization();
```

**Affected Endpoints:**
- POST /api/auth/logout
- POST /api/auth/register
- POST /resend-verification
- POST /forgot-password
- POST /reset-password

**Priority:** MEDIUM - Defense-in-depth, not critical for homelab

**Effort:** 1 hour

---

### DOCS-1: README.md (Pre-Open Source)

**Status:** BACKLOG 📋 **HIGH - Blocking for open source**
**Severity:** Documentation | **Impact:** First impression, adoption rate

**Current State:**
- No README.md at repository root
- GitHub landing page empty

**What's Needed:**
- Project description and value proposition
- Screenshots of key features (Messages, Analytics, Settings)
- Quick start (5 commands from zero to running)
- Comparison to alternatives
- Community links

**Priority:** HIGH - Blocking for GitHub migration

---

### DOCS-2: Setup Guide (Pre-Open Source)

**Status:** BACKLOG 📋 **HIGH - Blocking for open source**
**Severity:** Documentation | **Impact:** User onboarding success

**What's Needed:**
- Docker Compose walkthrough (simple copy/paste)
- .env.example explanation (where to get each API key)
- First user setup process
- Health check verification
- Common deployment targets

**Priority:** HIGH - Blocking for GitHub migration

---

### DOCS-3: Configuration Guide

**Status:** BACKLOG 📋
**Severity:** Documentation | **Impact:** Optimal spam detection tuning

**What's Needed:**
- Explain all 9 algorithms in plain English
- When to enable/disable each one
- Example configurations (Strict / Moderate / Permissive)
- Threshold tuning guidance
- File scanning configuration
- Auto-trust settings

**Priority:** MEDIUM - Nice to have before launch

---

### DOCS-4: LICENSE File (Pre-Open Source)

**Status:** BACKLOG 📋 **HIGH - Blocking for open source**
**Severity:** Legal | **Impact:** Usage clarity, contribution safety

**Decision Needed:**
- MIT (permissive, max adoption)
- AGPL (copyleft, prevents commercial abuse)

**Priority:** HIGH - Blocking for GitHub migration

---

### DOCS-5: Contextual Help System

**Status:** BACKLOG 📋
**Severity:** UX | **Impact:** Reduces support burden, builds user confidence

**What's Needed:**
- Help icons (?) next to complex settings
- Tooltips explaining each option
- "Learn more" expandable sections
- Examples embedded in UI

**Priority:** MEDIUM - Nice to have before launch

---

### DOCS-6: First-Time Setup Guidance

**Status:** BACKLOG 📋
**Severity:** UX | **Impact:** Reduces misconfiguration

**What's Needed:**
- Welcome modal on first login
- Suggested default configurations
- Optional guided wizard
- "Getting Started" dashboard widget

**Priority:** LOW - Post-launch enhancement

---

### UX-1: Detection Explanation Enhancement

**Status:** BACKLOG 📋
**Severity:** UX | **Impact:** Trust and transparency

**Current State:**
- Shows confidence scores and detection methods
- Missing: WHY the decision was made

**Enhancement:**
- Show contributing factors for each detection
- Explain threshold logic
- Example: "Banned because: CAS (90%), Stop Words: 2 matches (85%), TF-IDF (73%)"
- Example: "Allowed because: User is Trusted, Bayesian Filter (88% ham)"

**Priority:** MEDIUM - Nice to have before launch

---

### UX-2: Message Trends Analytics

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** Complete analytics offering

**Current State:**
- Analytics → Message Trends tab says "Coming soon"

**Implementation:**
- Daily message volume charts
- Messages per chat breakdown
- Active users per day
- Peak hours heatmap
- Growth trends (week-over-week)
- Spam vs ham ratio over time
- Use existing messages/detection_results tables

**Priority:** MEDIUM - Nice to have before launch

---

### ANALYTICS-1: Algorithm Reliability Dashboard

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** Algorithm tuning confidence

**What It Shows:**
- Performance metrics per algorithm (checks, spam flagged, false positives)
- False positive rate by algorithm
- Average confidence per algorithm
- Trends over time
- Recommendations (e.g., "Stop Words has 11.5% false positive rate - review list")

**Data Source:** Existing detection_results + manual corrections

**Priority:** MEDIUM - Nice to have before launch

---

### ANALYTICS-2: Algorithm Recommendations

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** Automated optimization

**Auto-Suggest Configuration Changes:**
- "CAS detected 0 spam in 30 days - consider disabling"
- "OpenAI Vision has 98% accuracy - consider increasing weight"
- "Stop Words false positive rate increased - review corrections"

**Priority:** LOW - Post-launch enhancement

---

### ANALYTICS-3: Export Features

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** External analysis, compliance

**Export to CSV/Excel:**
- Analytics data (detection stats, trends)
- User lists (with tags, trust status)
- Audit logs (compliance)
- Message history (backup/migration)

**Priority:** LOW - Post-launch enhancement

---

### ML-1: Algorithm Weight Optimization

**Status:** BACKLOG 📋
**Severity:** Enhancement | **Impact:** Fewer false positives

**Current:** Confidence aggregation uses fixed weights
**Enhancement:** Learn optimal weights from manual corrections
- Train on correction history
- Auto-tune weekly
- Example: "OpenAI Vision weight increased 1.0 → 1.3 based on corrections"

**Priority:** LOW - Post-launch experimentation

---

### ML-2: Community-Specific Pattern Mining

**Status:** BACKLOG 📋
**Severity:** Enhancement | **Impact:** Personalized spam detection

**What It Does:**
- Analyze banned messages, suggest new stop words
- Extract patterns via TF-IDF
- One-click add to config
- Example: "These phrases appeared in 80% of your spam: [list]"

**Priority:** LOW - Post-launch experimentation

---

### ML-3: Anomaly Detection for Raids

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** Early warning for coordinated attacks

**Detect Unusual Patterns:**
- Burst of new joins (15 users in 5 minutes)
- Similar messages from multiple users
- Coordinated posting patterns
- Alert: "⚠️ Unusual activity detected"

**Priority:** LOW - Post-launch experimentation

---

### DEPLOY-1: Docker Compose Simplicity Validation

**Status:** BACKLOG 📋 **HIGH - Blocking for open source**
**Severity:** Deployment | **Impact:** Deployment friction

**Audit Checklist:**
- .env.example has all required variables
- Clear CHANGEME markers for secrets
- No complex args/profiles for basic setup
- Works standalone (no required custom networks/volumes)
- Power users can extend without breaking defaults

**Priority:** HIGH - Blocking for GitHub migration

---

### DEPLOY-2: Troubleshooting Documentation

**Status:** BACKLOG 📋
**Severity:** Documentation | **Impact:** Support burden reduction

**Add to README:**
- Common issues (ClamAV not starting, bot offline, database connection)
- Health check commands (curl, docker logs)
- Log debugging tips
- Permission errors

**Priority:** MEDIUM - Nice to have before launch

---

### FEATURE-4.23: Cross-Chat Ban Message Cleanup

**Status:** BACKLOG 📋
**Severity:** Feature | **Impact:** Complete spam removal, user experience

**Current Behavior:**
- Cross-chat ban system bans user from all managed chats
- Existing messages from banned user remain in all chats
- Spam messages stay visible after ban

**Enhancement:**
- When high-confidence spam triggers cross-chat ban, delete ALL existing messages from that user across all chats
- Use message history table to find all messages by telegram_user_id
- Delete messages via Telegram Bot API (if within 48hr window) or mark as deleted in DB
- Audit log all deletions

**Implementation Notes:**
- Only trigger on high-confidence spam (>90% confidence or OpenAI veto)
- Query messages table: `WHERE telegram_user_id = ? AND deleted_at IS NULL`
- Batch delete via TickerQ job (avoid blocking moderation action)
- Handle Telegram API 48-hour deletion window (older messages can't be deleted via API)

**Priority:** MEDIUM - Nice to have before launch

---

### QUALITY-1: Nullable Reference Type Hardening

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Bug prevention, developer experience

**Current State:**
- Nullable reference types enabled in ALL 7 projects (`<Nullable>enable</Nullable>`)
- TreatWarningsAsErrors enabled in 3/7 projects (ContentDetection, Core, Data)
- TreatWarningsAsErrors MISSING in 4/7 projects (main app, Configuration, Telegram, Telegram.Abstractions)
- Unknown number of potential null dereference warnings in projects without strict enforcement
- Build currently succeeds with 0 errors, 0 warnings (likely due to null-forgiving operators or selective suppression)

**Motivation:**
- Reduce potential NullReferenceException bugs
- Make intent explicit (nullable vs non-nullable)
- Show best practices for open source community
- Leverage C# compiler for null safety without massive Option<T> rewrite

**Proposed Solution:**
**Phase 1: Enable TreatWarningsAsErrors in Remaining Projects** (~15 min)
- Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to 4 remaining projects:
  - TelegramGroupsAdmin/TelegramGroupsAdmin.csproj (main app)
  - TelegramGroupsAdmin.Configuration/TelegramGroupsAdmin.Configuration.csproj
  - TelegramGroupsAdmin.Telegram/TelegramGroupsAdmin.Telegram.csproj
  - TelegramGroupsAdmin.Telegram.Abstractions/TelegramGroupsAdmin.Telegram.Abstractions.csproj
- Build and count nullability errors that surface

**Phase 2: Fix Nullable Warnings** (~1-2 hours)
- Fix all nullable warnings revealed by Phase 1
- Focus on critical warnings first (CS8600, CS8602, CS8603 - actual null dereference risk)
- Use null-forgiving operator (`!`) sparingly for legitimate false positives
- Prioritize hot paths (spam detection, message processing, bot services)

**Phase 3: Ongoing Enforcement** (future)
- Enforce for all new code
- Gradual cleanup of remaining warnings
- Code review checklist for null handling

**Practical Guidelines:**
- **DO**: Use `string?` for optional parameters
- **DO**: Check null before dereferencing: `if (user?.Email != null)`
- **DO**: Return empty collections instead of null: `Array.Empty<T>()`
- **DON'T**: Use `!` everywhere to silence warnings
- **DON'T**: Rewrite working code just to eliminate null
- **DON'T**: Make every field nullable "to be safe"

**Success Criteria:**
- TreatWarningsAsErrors enabled in ALL 7 projects
- Zero nullable warnings in entire solution
- Build succeeds with 0 errors, 0 warnings
- No new nullable warnings in future PRs
- No production NullReferenceExceptions from new code

**Priority:** MEDIUM - Quality improvement, not blocking

**Effort:** 1.5-2 hours total (15 min to enable, 1-2 hours to fix warnings)

**When to Do:**
- NOT before security sanitization (security is blocking)
- NOT before migration testing (higher priority)
- MAYBE after migration testing Phase 2
- DEFINITELY before open sourcing (shows code quality)

---

### FEATURE-4.20: Auto-Fix Database Sequences on Backup Restore

**Status:** BACKLOG 📋
**Severity:** Bug Fix | **Impact:** Data integrity, user experience

**Current Behavior:**
- When restoring from backup, database sequences are not automatically synchronized with max IDs
- Sequence values remain at their pre-restore state (e.g., sequence=2 but max ID=84)
- Subsequent inserts fail with "duplicate key value violates unique constraint" errors
- Requires manual SQL execution to reset all sequences after restore

**Proposed Enhancement:**
- Automatically detect and fix sequence mismatches during backup restore
- Add post-restore hook to sync all sequences with max IDs
- Display warning if sequences are out of sync before restore
- Include sequence reset as part of restore transaction

**Use Cases:**
- Database restore from backup
- Database migration from another instance
- Recovery from corruption

**Implementation Considerations:**
- Query all sequences and their corresponding tables dynamically
- Execute `SETVAL` for each sequence based on `MAX(id)` from table
- Handle tables with no rows (use COALESCE for null safety)
- Log which sequences were reset and their new values
- Consider adding to BackupService.RestoreAsync() method

**SQL Pattern:**
```sql
SELECT SETVAL('table_name_id_seq', COALESCE((SELECT MAX(id) FROM table_name), 1), true);
```

**Affected Tables:** 27+ sequences across all auto-incrementing ID columns

**Priority:** Medium (affects restore operations, manual workaround exists)

**Related Files:**
- `/src/TelegramGroupsAdmin/Services/Backup/BackupService.cs` (line ~390-450)

---

### FEATURE-4.9: Bot Configuration Hot-Reload

**Status:** BACKLOG 📋
**Severity:** Convenience | **Impact:** Developer experience, deployment flexibility

**Current Behavior:**
- Bot configuration changes require application restart
- Telegram bot token, chat IDs, and other settings are read once at startup
- Updates to environment variables or configuration files require full app restart

**Proposed Enhancement:**
- Implement `IOptionsMonitor<TelegramOptions>` or similar pattern for config hot-reload
- Allow runtime updates to non-critical bot settings without restart
- File watcher or admin UI trigger for configuration refresh

**Use Cases:**
- Adding new managed chats without downtime
- Updating bot token during rotation
- Adjusting rate limits or thresholds

**Implementation Considerations:**
- Some settings (like bot token) may require full reconnection
- Need to handle partial config updates gracefully
- Consider admin UI button "Reload Bot Configuration"
- TelegramBotClientFactory may need IOptionsMonitor support

**Priority:** Low (nice-to-have, not blocking MVP)

---

### SCHEMA-1: Fix Audit Log FK Cascade Rules

**Status:** BACKLOG 📋 **Priority: High**
**Severity:** Schema Bug | **Impact:** User deletion blocked, broken FK behavior
**Discovered:** 2025-10-26 via Test 6 (CascadeBehaviorTests)

**Current State:**
- `audit_log` FKs use `ON DELETE SET NULL` for actor/target user references
- Exclusive arc CHECK constraint requires exactly ONE actor field to be non-NULL
- CONFLICT: SET NULL cascade violates CHECK constraint when user is deleted
- Result: User deletion fails with `23514` CHECK constraint violation

**Production Impact:**
- ❌ Cannot delete users who have audit_log entries as actor
- ❌ User cleanup/deactivation workflows broken
- ❌ Orphaned audit entries with NULL actors provide no value (can't attribute actions)

**Root Cause:**
Migration `20251025003104_AddActorExclusiveArcToAuditLog.cs` configured:
```csharp
onDelete: ReferentialAction.SetNull  // Lines 137, 145, 153, 161
```

But also added:
```csharp
CK_audit_log_exclusive_actor: requires exactly ONE actor field non-NULL  // Line 124
```

These constraints are mathematically incompatible.

**Proposed Solution:**
Create new migration `FixAuditLogCascadeRules.cs`:
1. Drop existing FK constraints (4 total: actor_web_user_id, target_web_user_id, actor_telegram_user_id, target_telegram_user_id)
2. Re-add with `ReferentialAction.Cascade` (delete user → delete audit entries)
3. Rationale: Audit entries without actor identity are useless for investigations/attribution
4. Clean up any existing NULL actor entries (if any exist from manual testing)

**Affected FKs:**
- `FK_audit_log_users_actor_web_user_id`
- `FK_audit_log_users_target_web_user_id`
- `FK_audit_log_telegram_users_actor_telegram_user_id`
- `FK_audit_log_telegram_users_target_telegram_user_id`

**Test Coverage:**
Test 6 (CascadeBehaviorTests.UserDeletionCascade_FailsDueToCheckConstraintConflict) currently documents the bug. After migration fix, update test to validate CASCADE behavior works correctly.

**Acceptance Criteria:**
- [ ] Migration created and tested
- [ ] User deletion succeeds and cascades to audit_log
- [ ] Test 6 updated to validate CASCADE behavior (delete user → verify audit entries removed)
- [ ] No orphaned audit entries with NULL actors remain
- [ ] Exclusive arc CHECK constraint still enforced (exactly one actor field non-NULL for existing entries)

---

## Bugs

(No open bugs)

---

## Completed Work

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

**All medium-priority performance optimizations completed!**

---

## Performance Optimization Summary

**Deployment Context:** 10+ chats, 100-1000 messages/day (10-50 spam checks/day on new users), 1000+ users, Messages page primary tool

**Total Issues Remaining:** 0 (down from 52 initial findings, 38 false positives + 4 completed optimizations)

**Implementation Priority:** Implement opportunistically during related refactoring work

**Removed Items:**
- **PERF-DATA-5** (JSON source generation): Inconsistent with reflection usage in backup/restore, negligible benefit with existing caching (PERF-CFG-1), premature optimization
- **PERF-CD-1** (Stop words N+1): Empirical testing (2025-10-21) disproved original estimates by 100-300x. Actual: 0.3ms baseline → 1.5ms at 92x scale. PostgreSQL hash joins handle this perfectly. Original analysis confused LEFT JOINs with N+1 queries. Solution in search of a problem.

---

### DI-1: Interface-Only Dependency Injection Audit

**Status:** PENDING ⏳
**Severity:** Best Practice | **Impact:** Testability, maintainability

**Remaining Work:**
- Audit all services/repositories to verify they use interfaces (HttpClient/framework types OK)
- Verify all DI registrations follow `services.AddScoped<IFoo, Foo>()` pattern
- Document exceptions where concrete types are intentionally injected

**Progress:** Created interfaces for 4 repositories (IAuditLogRepository, IUserRepository, IMessageHistoryRepository, ITelegramUserRepository), updated DI registrations, verified runtime

---

### CODE-1: File Naming Consistency Audit

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** IDE navigation, developer experience, maintainability

**Current State:**
- 5 parallel explore agents audited all C# files across 7 projects (2025-10-25)
- Found **7 critical name mismatches** where file name doesn't match primary type name
- Found **major consolidation files** with 9-13 types in single file
- Found **~20 acceptable patterns** (interface + DTOs in same file)

**Critical Name Mismatches (Fix file name to match model/interface name):**

**ContentDetection Project:**
1. `ISpamCheck.cs` → should be `IContentCheck.cs` (contains `IContentCheck` interface)
2. `ISpamDetector.cs` → should be `IContentDetector.cs` (contains `IContentDetector` interface)
3. `SpamDetectionEngine.cs` → should be `ContentDetectionEngine.cs` (contains `ContentDetectionEngine` class)

**Data Project:**
4. `AdminNote.cs` → should be `AdminNoteDto.cs` (contains `AdminNoteDto` class)
5. `UserTag.cs` → should be `UserTagDto.cs` (contains `UserTagDto` class)
6. `ConfigRecord.cs` → should be `ConfigRecordDto.cs` (contains `ConfigRecordDto` class)

**Telegram Project:**
7. `ChatAdminModels.cs` → should be `ChatAdmin.cs` (contains single `ChatAdmin` class, not plural)

**Files with Multiple Top-Level Types (All Must Be Split):**

**CRITICAL - Multiple major types make file search inefficient:**
- AI agents must scan file contents instead of using file names for navigation
- Excessive context usage when searching for types
- Breaks single-responsibility principle for file organization
- **Rule: One file per major type (class/interface/record/enum)**

**Files Requiring Splits:**

**ContentDetection Project:**
- `SpamDetectionConfig.cs` - **13 config classes** → Split into individual files (StopWordsConfig.cs, SimilarityConfig.cs, CasConfig.cs, etc.)
- `SpamCheckResponse.cs` - 4 types (CheckResultType, ContentCheckResponse, ContentCheckResult, SpamAction) → Split into 4 files
- `SpamCheckRequest.cs` - 2 types (ContentCheckRequest, ContentCheckMetadata) → Split into 2 files
- `ISpamDetectionConfigRepository.cs` - 2 types (interface, ChatConfigInfo record) → Split into 2 files
- `ICloudScannerService.cs` - 5 types (interface + 4 supporting types) → Split into 5 files
- `IContentDetectionEngine.cs` - 2 types (interface, ContentDetectionResult) → Split into 2 files
- `IFileScannerService.cs` - 3 types (interface + 2 supporting types) → Split into 3 files
- `IFileScanningTestService.cs` - 3 types (interface + 2 supporting types) → Split into 3 files
- `IMessageHistoryService.cs` - 2 types (interface, HistoryMessage record) → Split into 2 files
- `IOpenAITranslationService.cs` - 2 types (interface, TranslationResult) → Split into 2 files
- `ClamAVScannerService.cs` - 2 types (service, ClamAVHealthResult) → Split into 2 files
- `Tier1VotingCoordinator.cs` - 2 types (coordinator, Tier1ScanResult) → Split into 2 files
- `Tier2QueueCoordinator.cs` - 2 types (coordinator, Tier2ScanResult) → Split into 2 files
- `TokenizerService.cs` - 4 types (interface, 2 partial classes, options) → Split into 4 files
- `UrlPreFilterService.cs` - 2 types (interface, implementation) → Split into 2 files

**Data Project:**
- `UserRecord.cs` - **9 types** (4 enums + 5 classes) → Split into 9 files (PermissionLevel.cs, UserStatus.cs, InviteStatus.cs, AuditEventType.cs, UserRecordDto.cs, RecoveryCodeRecordDto.cs, InviteRecordDto.cs, InviteWithCreatorDto.cs, AuditLogRecordDto.cs)
- `MessageRecord.cs` - 2 types (MessageRecordDto, MessageEditRecordDto) → Split into 2 files
- `UserActionRecord.cs` - 2 types (enum, class) → Split into 2 files
- `VerificationToken.cs` - 2 types (enum, class) → Split into 2 files
- `ManagedChatRecord.cs` - 3 types (2 enums, 1 class) → Split into 3 files
- `ImpersonationAlertRecordDto.cs` - 3 types (2 enums, 1 class) → Split into 3 files
- `TagColor.cs` - 2 types (enum, extensions class) → Split into 2 files

**Telegram Project:**
- `TelegramPhotoService.cs` - 2 types (UserPhotoResult record, service class) → Split into 2 files
- `ChatInviteLinkService.cs` - 2 types (interface, implementation) → Split into 2 files
- `UserMessagingService.cs` - 3 types (interface, record, class) → Split into 3 files
- `ImpersonationDetectionService.cs` - 3 types (record, interface, class) → Split into 3 files
- `IContentCheckCoordinator.cs` - 2 types (interface, record) → Split into 2 files
- `IDmDeliveryService.cs` - 2 types (record, interface) → Split into 2 files
- `INotificationChannel.cs` - 3 types (interface, 2 records) → Split into 3 files
- `MessageTrendsData.cs` - 4 types (4 related records) → Split into 4 files
- `FalsePositiveStats.cs` - 2 types (2 related classes) → Split into 2 files

**Main App (TelegramGroupsAdmin):**
- `MessageExportService.cs` - 2 types (interface, implementation) → Split into 2 files
- `RefetchRequest.cs` - 2 types (enum, record) → Split into 2 files
- `IAuthService.cs` - 4 types (interface + 3 records) → Split into 4 files
- `IInviteService.cs` - 3 types (interface + 2 records) → Split into 3 files
- `RuntimeLoggingService.cs` - 2 types (interface, implementation) → Split into 2 files
- `IEmailService.cs` - 3 types (interface, enum, record) → Split into 3 files
- `AuthEndpoints.cs` - 5 types (endpoints class + 4 request records) → Split into 5 files
- `DialogModels.cs` - **7 classes** → Split into 7 files
- `PromptBuilderModels.cs` - 3 types (2 classes, 1 enum) → Split into 3 files
- `BackupModels.cs` - 2 types (2 classes) → Split into 2 files

**Core/Configuration/Abstractions Projects:**
- `BackgroundJobConfig.cs` - 2 types (BackgroundJobConfig, BackgroundJobsConfig) → Split into 2 files
- `Actor.cs` - 2 types (ActorType enum, Actor record) → Split into 2 files
- `ConfigRepository.cs` - 2 types (interface, implementation) → Split into 2 files
- `FileScanningConfig.cs` - **9 classes** → Split into 9 files
- `JobPayloads.cs` - 4 payload records → Split into 4 files

**Total Files to Split: ~60+ files containing ~130+ types**

**Implementation Priority:**
1. **Fix 7 name mismatches** - Quick wins (rename operations)
2. **Split all multiple-type files** - Critical for AI/human navigation efficiency
3. **Files ordered by impact**: Start with worst offenders (9-13 types) then work down to 2-type files

**Success Criteria:**
- All file names match their primary public type names
- Every file contains exactly one major top-level type (class/interface/record/enum)
- Zero IDE confusion when navigating to definitions (F12 works perfectly)
- AI agents can find types by file name instead of scanning file contents
- Reduced context usage in AI-assisted development
- Consistent single-responsibility file organization across all projects

**Effort:** 8-12 hours total
- Name mismatches: 1 hour (7 renames)
- High-priority splits (9-13 types): 3-4 hours (UserRecord, SpamDetectionConfig, FileScanningConfig, DialogModels)
- Medium splits (3-5 types): 2-3 hours
- Simple splits (2 types): 2-4 hours

**Priority:** MEDIUM - Quality improvement that directly impacts AI development efficiency

**When to Do:**
- After security sanitization (blocking)
- After migration testing (higher priority)
- **BEFORE open sourcing** - Shows professional code organization
- Can be done incrementally: worst offenders first, then work down the list

---

### CODE-5: Fire-and-Forget Error Handling

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Silent failures, debugging difficulty
**Discovered:** 2025-10-26 via performance agent code review

**Current State:**
- 4 locations use `_ = Task.Run(...)` fire-and-forget pattern
- Exceptions swallowed without logging
- No timeout or cancellation support

**Affected Locations:**
1. `MessageProcessingService.cs:591` - Spam detection fire-and-forget
2. `MessageProcessingService.cs:730` - Edit spam detection fire-and-forget
3. `SpamActionService.cs:573` - Notification delivery fire-and-forget
4. `IntermediateAuthService.cs:38` - Token cleanup fire-and-forget

**Note:** OpenAI timeout is NOT an issue - HttpClient already configured with 30s timeout (ServiceCollectionExtensions.cs:252) and fail-open handling (OpenAISpamCheck.cs:171-176)

**Proposed Solution:**
Wrap all fire-and-forget tasks in try-catch with logging:
```csharp
_ = Task.Run(async () =>
{
    try
    {
        await RunSpamDetectionAsync(botClient, message, text, editVersion: 0, cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Spam detection failed for message {MessageId}", message.MessageId);
    }
}, cancellationToken);
```

**Priority:** MEDIUM - Good practice for debugging, not breaking

**Effort:** 1 hour (15 min per location)

---

### CODE-6: Extract Magic Numbers to Configuration

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Configuration flexibility
**Discovered:** 2025-10-26 via refactor agent code review

**Current State:**
- Hardcoded retention periods, timeouts, thresholds throughout codebase
- Examples:
  - `MessageHistoryRepository.cs:68` - 30-day retention hardcoded
  - Various timeout values (5s, 30s, 60s) scattered in code

**Proposed Solution:**
Extract to configuration classes:
```csharp
public class MessageHistoryOptions
{
    public int RetentionDays { get; set; } = 30;
    public int CleanupBatchSize { get; set; } = 1000;
}
```

**Affected Areas:**
- Message retention periods
- Background job polling intervals
- API timeout values
- Rate limit thresholds

**Priority:** LOW - Nice to have, not blocking

**Effort:** 1-2 hours

---

### CODE-7: Modernize to C# 12 Collection Expressions

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Code modernization
**Discovered:** 2025-10-26 via refactor agent code review

**Current State:**
- Uses `new List<string>()` throughout codebase
- C# 12 collection expressions available but not used

**Proposed Solution:**
Replace with modern syntax:
```csharp
// Before:
var items = new List<string>();

// After:
List<string> items = [];
```

**Benefits:**
- More concise
- Compiler can optimize allocation
- Consistent with .NET 9 idioms

**Priority:** LOW - Cosmetic improvement

**Effort:** 2 hours (automated refactoring with IDE)

---

### CODE-8: Remove Inconsistent ConfigureAwait Usage

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Code consistency
**Discovered:** 2025-10-26 via refactor agent code review

**Current State:**
- Only 7 uses of `.ConfigureAwait(false)` across entire codebase
- Inconsistent application suggests uncertainty about the pattern
- ASP.NET Core doesn't require ConfigureAwait(false) (no SynchronizationContext)

**Affected Files:**
- ContentCheckCoordinator.cs (4 occurrences)
- SpamActionService.cs (2 occurrences)
- MessageProcessingService.cs (1 occurrence)

**Proposed Solution:**
Remove all `.ConfigureAwait(false)` calls - unnecessary in ASP.NET Core

**Microsoft Guidance:** ASP.NET Core apps don't need ConfigureAwait(false) because there's no SynchronizationContext to capture

**Priority:** LOW - Code consistency improvement

**Effort:** 15 minutes

---

### CODE-9: Remove Reflection in Production Code

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Type safety, performance
**Discovered:** 2025-10-26 via refactor agent code review

**Current State:**
- `MessageProcessingService.cs:824-826` uses reflection to call `SerializeCheckResults`
- No compile-time safety
- Performance overhead (minimal but unnecessary)
- Fragile - breaks silently if method renamed

**Proposed Solution:**
Make method accessible through interface or use direct serialization:
```csharp
// Option 1: Add to interface
CheckResultsJson = spamDetectionEngine.SerializeCheckResults(result.SpamResult.CheckResults)

// Option 2: Direct serialization
CheckResultsJson = JsonSerializer.Serialize(result.SpamResult.CheckResults)
```

**Priority:** LOW - Nice to have, not breaking

**Effort:** 30 minutes

---

### QUALITY-2: Implement HybridCache

**Status:** BACKLOG 📋
**Severity:** Code Quality | **Impact:** Performance optimization
**Discovered:** 2025-10-26 via code review (AI agent recommended but never implemented)

**Current State:**
- HybridCache package added to project (Microsoft.Extensions.Caching.Hybrid v9.0.0)
- Package never wired up or used
- Zero caching strategy despite frequent config/admin lookups

**Proposed Implementation:**
Cache frequently-accessed data:
```csharp
// Config lookups (5min TTL)
var config = await _cache.GetOrCreateAsync($"config:{chatId}", async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
    return await _configRepo.GetConfigAsync(chatId);
});

// Admin status (30s TTL)
var isAdmin = await _cache.GetOrCreateAsync($"admin:{chatId}:{userId}", async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
    return await _chatAdminsRepo.IsAdminAsync(chatId, userId);
});
```

**Target Areas:**
- Spam detection config per chat
- Chat admin status lookups
- Blocklist domain lookups
- Tag/note data for message page

**Priority:** MEDIUM - Performance improvement for homelab

**Effort:** 2-4 hours

---

### PERF-2: Add Performance Telemetry

**Status:** BACKLOG 📋
**Severity:** Performance | **Impact:** Validate production metrics
**Discovered:** 2025-10-26 via performance agent code review

**Current State:**
- Performance claims based on observation, not telemetry
- No OpenTelemetry, no custom metrics
- Cannot validate 255ms average spam detection claim

**Proposed Solution:**
Add OpenTelemetry with custom metrics:
```csharp
using var activity = ActivitySource.StartActivity("SpamDetection");
activity?.SetTag("chat.id", chatId);
activity?.SetTag("user.id", userId);
// ... existing code ...
activity?.SetTag("detection.duration_ms", stopwatch.ElapsedMilliseconds);
activity?.SetTag("detection.result", result.Action.ToString());
```

**Metrics to Track:**
- Spam detection duration (avg, P95, P99)
- Analytics query response time
- Message page load time
- Background job execution time

**Benefits:**
- Validate performance claims with data
- Identify regressions
- Optimize based on metrics

**Priority:** MEDIUM - Useful for homelab monitoring

**Effort:** 4-6 hours

---

### REFACTOR-1: Method Extraction for Unit Testing

**Status:** BACKLOG 📋
**Severity:** Refactoring | **Impact:** Testability, maintainability
**Discovered:** 2025-10-26 via refactor agent code review
**User Priority:** HIGH (preparing for unit test migration)

**Current State:**
- `MessageProcessingService.HandleNewMessageAsync` - 550+ lines
- Single responsibility violated (handles 15+ concerns)
- Difficult to unit test individual behaviors
- High cognitive complexity

**Target:**
Reduce to ~300-350 lines by extracting 3-5 focused handlers:
- MediaDownloadHandler - Photo/video/media download logic
- FileScanningHandler - ClamAV/VirusTotal coordination
- Keep inline: Translation, impersonation, spam detection (core orchestration)

**Example Refactoring:**
```csharp
public async Task HandleNewMessageAsync(...)
{
    if (ShouldSkipMessage(message)) return;

    var messageContext = await BuildMessageContextAsync(...);
    await ExecuteCommandsIfPresentAsync(...);
    await HandleAdminMentionsAsync(...);

    // Extract to MediaDownloadHandler
    await _mediaDownloadHandler.DownloadMediaAttachmentsAsync(message, messageContext);

    // Extract to FileScanningHandler
    await _fileScanningHandler.ScheduleFileScansAsync(message, messageContext);

    await TranslateMessageIfNeededAsync(...);
    await SaveMessageToHistoryAsync(...);
    await RunContentDetectionAsync(...);
}
```

**Benefits:**
- Unit testable components
- Easier to understand and modify
- Reduced cognitive load
- Prep for comprehensive unit test suite

**Priority:** HIGH - User confirmed, preparing for unit tests

**Effort:** 4-6 hours (extract 3-5 handlers, update DI, add tests)

---
