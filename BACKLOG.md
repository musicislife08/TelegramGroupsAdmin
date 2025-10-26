# Development Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-24
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 88/100 (Excellent)

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
- Hardcoded secrets in code (connection strings, API keys)

**Priority:** HIGH - Must complete before GitHub migration

**Effort:** 1-2 hours

**Related Work:**
- Document secret management in README (environment variables only)
- Add pre-commit hooks to block future secret commits

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

## Bugs

### BUG-1: Analytics Only Tracks False Positives, Misses False Negatives

**Status:** 🐛 OPEN (2025-10-24)
**Severity:** Medium | **Impact:** Analytics accuracy, detection performance metrics incomplete

**Issue:**
The analytics system (`AnalyticsRepository.GetFalsePositiveStatsAsync`) only counts false positives (spam → ham corrections) but ignores false negatives (ham → spam corrections). This skews accuracy metrics and hides systemic detection failures.

**Current Logic (lines 28-37):**
```csharp
// Only finds: System said SPAM → User corrected to HAM (false positive)
var falsePositives = await context.DetectionResults
    .Where(dr => dr.IsSpam && dr.DetectionSource != "manual") // Initial spam detection
    .Where(dr => context.DetectionResults.Any(correction =>
        correction.MessageId == dr.MessageId &&
        correction.DetectionSource == "manual" &&
        !correction.IsSpam &&  // <-- Only checks corrections to HAM
        correction.DetectedAt > dr.DetectedAt))
```

**Missing Logic:**
No code to find: System said HAM → User corrected to SPAM (false negative)

**Example Case (Message 22053):**
```
Detection 1287: is_spam=false, confidence=-225, source='auto'  (System: not spam)
Detection 1298: is_spam=true,  confidence=100,  source='manual' (User: actually spam)
```

This is a **false negative** - system failed to detect spam, user manually corrected it. Currently **not counted** in analytics.

**Impact:**
- Incomplete accuracy metrics (only showing half the picture)
- Cannot track if system is under-detecting spam (false negatives)
- Cannot compare false positive rate vs false negative rate
- Per-algorithm stats (lines 198-208) also miss false negatives

**Proposed Fix:**
Add parallel tracking for false negatives:
1. Create `GetFalseNegativeStatsAsync()` method (mirrors false positive logic, inverts conditions)
2. Update analytics UI to show both metrics:
   - False Positives: "Said spam, was actually ham"
   - False Negatives: "Said ham, was actually spam"
3. Add combined "Overall Accuracy" metric: `(TP + TN) / (TP + TN + FP + FN)`

**Files to Update:**
- `AnalyticsRepository.cs` - Add false negative query
- `IAnalyticsRepository.cs` - Add interface method
- `PerformanceMetrics.razor` - Add false negative display
- `FalsePositiveStats.cs` - Rename to `DetectionAccuracyStats` with both FP and FN

**Priority:** Medium (not blocking, but important for production monitoring)

---

## Completed Work

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
