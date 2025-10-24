# Development Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-24
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 88/100 (Excellent)

This document tracks technical debt, performance optimizations, refactoring work, and deferred features.

---

## Feature Backlog

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

## Completed Work

**2025-10-24**: ARCH-2 reopened (audit log Actor migration incomplete - 5 tables remaining)
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

### ARCH-2: Complete Actor Exclusive Arc Migration (REOPENED)

**Status:** 🔄 REOPENED (2025-10-24) - Partial completion, significant work remaining
**Severity:** Architecture | **Impact:** Type safety, data consistency, audit trail accuracy
**Complexity:** High - requires database migrations, data migration, repository updates, UI changes

---

## Background

The Actor exclusive arc pattern (Phase 4.19) provides type-safe attribution tracking with three mutually exclusive actor types:
- **WebUser** - Authenticated web admin (UUID)
- **TelegramUser** - Telegram user via bot commands (long ID)
- **System** - Automated actions (identifier string like "auto_trust", "bot_protection")

**Database Pattern:**
```sql
-- Exclusive arc: exactly ONE of these must be non-null
web_user_id UUID,
telegram_user_id BIGINT,
system_identifier VARCHAR(50)
```

**Code Pattern:**
```csharp
Actor.FromWebUser(userId, email)
Actor.FromTelegramUser(telegramId, username)
Actor.AutoTrust  // Predefined system actor
```

---

## Current Status

### ✅ Phase 1 Complete (2025-10-21): Telegram Moderation Tables

**Migrated Tables:**
1. ✅ `user_actions` - Moderation actions (ban/warn/trust/etc)
   - Columns: `web_user_id`, `telegram_user_id`, `system_identifier`
   - Usage: All moderation commands, spam actions, auto-trust

2. ✅ `stop_words` - Custom spam keywords
   - Columns: `web_user_id`, `telegram_user_id`, `system_identifier`
   - Usage: Add/remove stop words via UI or bot

3. ✅ `user_tags` - User classification tags
   - Columns: `actor_web_user_id`, `actor_telegram_user_id`, `actor_system_identifier`
   - Plus separate removal actor columns for audit trail

4. ✅ `admin_notes` - Per-user moderator notes
   - Columns: `actor_web_user_id`, `actor_telegram_user_id`, `actor_system_identifier`
   - **⚠️ Legacy field still exists:** `created_by` (string) - needs data migration + column drop

5. ✅ `domain_filters` - Manual domain blacklist/whitelist
   - Columns: `web_user_id`, `telegram_user_id`, `system_identifier`

6. ✅ `blocklist_subscriptions` - External URL blocklists
   - Columns: `web_user_id`, `telegram_user_id`, `system_identifier`

**Migrated Code:**
- ✅ ModerationActionService - All methods accept `Actor`
- ✅ UserActionsRepository, AdminNotesRepository, UserTagsRepository
- ✅ TelegramUserManagementService.ToggleTrustAsync(), UnbanAsync()
- ✅ Domain/blocklist repositories and UI components

---

### ❌ Phase 2 Remaining: Web Admin Audit Tables

**Tables NOT Yet Migrated:**

1. ❌ **`audit_log`** - Web admin action audit trail (HIGH PRIORITY)
   - **Current:** `actor_user_id VARCHAR` (string), `target_user_id VARCHAR` (string)
   - **Issue:** Stores "system" string instead of null, breaking UI display (shows "Unknown (system)")
   - **Impact:** 30+ call sites in services/endpoints
   - **Files:** AuditLogRepository.cs, IAuditLogRepository.cs, UserAutoTrustService.cs:108, AuthService.cs, TotpService.cs, UserManagementService.cs, EmailVerificationEndpoints.cs, etc.

2. ❌ **`invites`** - User invite tokens
   - **Current:** `created_by VARCHAR` (UUID string), `used_by VARCHAR` (UUID string)
   - **Issue:** Only tracks web users, no system/Telegram actors possible
   - **Impact:** InviteService, invite management UI
   - **Files:** InviteService.cs, InviteRepository.cs

3. ❌ **`reports`** - User-submitted spam reports
   - **Current:** Mixed approach - `reviewed_by VARCHAR` (string), `reported_by_user_id BIGINT` (Telegram only), `web_user_id VARCHAR` (web only)
   - **Issue:** Can't track if system auto-reviewed a report, split actor logic
   - **Impact:** Reports page, report review workflow
   - **Files:** ReportDto.cs, ReportActionsService.cs, Reports.razor

4. ❌ **`chat_prompts`** - Custom OpenAI prompts (DEPRECATED?)
   - **Current:** `added_by VARCHAR` (string)
   - **Note:** May be superseded by `prompt_versions` table
   - **Files:** ChatPromptRecord.cs

5. ❌ **`prompt_versions`** - AI prompt builder version history
   - **Current:** `created_by VARCHAR` (UUID string)
   - **Issue:** Only web users, can't track system-generated prompts
   - **Impact:** Prompt builder UI, version history
   - **Files:** PromptVersionDto.cs, prompt builder components

---

## Migration Plan

### Step 1: Fix Immediate Bug (5 minutes)

**File:** `UserAutoTrustService.cs:108`
```csharp
// Change:
actorUserId: "system",  // ❌ Shows "Unknown (system)" in UI

// To:
actorUserId: null,      // ✅ Shows "SYSTEM" chip in Audit.razor:120-124
```

### Step 2: Migrate `audit_log` Table (HIGH PRIORITY)

**Database Migration:**
```sql
ALTER TABLE audit_log
  ADD COLUMN actor_web_user_id VARCHAR(450),
  ADD COLUMN actor_telegram_user_id BIGINT,
  ADD COLUMN actor_system_identifier VARCHAR(50),
  ADD COLUMN target_web_user_id VARCHAR(450),
  ADD COLUMN target_telegram_user_id BIGINT,
  ADD COLUMN target_system_identifier VARCHAR(50);

-- Data migration
UPDATE audit_log
SET actor_web_user_id = actor_user_id
WHERE actor_user_id IS NOT NULL
  AND actor_user_id != 'system'
  AND actor_user_id ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$';  -- UUID pattern

UPDATE audit_log
SET actor_system_identifier = 'unknown'
WHERE actor_user_id = 'system';

UPDATE audit_log
SET target_web_user_id = target_user_id
WHERE target_user_id IS NOT NULL
  AND target_user_id ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$';

-- Verify migration
SELECT COUNT(*) FROM audit_log WHERE actor_user_id IS NOT NULL
  AND actor_web_user_id IS NULL
  AND actor_telegram_user_id IS NULL
  AND actor_system_identifier IS NULL;

-- Drop old columns (after verification)
ALTER TABLE audit_log
  DROP COLUMN actor_user_id,
  DROP COLUMN target_user_id;
```

**Code Changes:**
1. Update `AuditLogRecordDto` columns
2. Update `IAuditLogRepository.LogEventAsync()` signature to accept `Actor actorBy, Actor? targetActor`
3. Update `AuditLogRepository` implementation to use Actor exclusive arc columns
4. Update all 30+ call sites (use IDE rename refactoring)
5. Update `Audit.razor` to use Actor display helpers instead of `GetUserEmail()`

**Estimated Effort:** 3-4 hours

---

### Step 3: Migrate `invites` Table

**Changes:**
- Add Actor columns for `created_by` and `used_by`
- Data migration: UUIDs → `web_user_id`
- Update InviteService, InviteRepository
- Update invite UI components

**Estimated Effort:** 1-2 hours

---

### Step 4: Migrate `reports` Table

**Changes:**
- Consolidate `reported_by_user_id`, `web_user_id` → Actor pattern
- Add Actor columns for `reviewed_by`
- Data migration for existing reports
- Update ReportActionsService, Reports.razor

**Estimated Effort:** 2-3 hours

---

### Step 5: Migrate `prompt_versions` Table

**Changes:**
- Add Actor columns for `created_by`
- Data migration: UUIDs → `web_user_id`
- Update prompt builder components

**Estimated Effort:** 1 hour

---

### Step 6: Clean Up `admin_notes` Legacy Column

**Changes:**
- Drop `created_by VARCHAR` column (already has Actor columns)
- Verify all data migrated to Actor columns
- Remove any legacy code references

**Estimated Effort:** 30 minutes

---

### Step 7: Fix Duplicate Enum Value Bug

**File:** `AuditEventType.cs:75, 97`
```csharp
UserAutoWhitelisted = 26,    // Line 75
...
ConfigurationChanged = 26,   // Line 97 - DUPLICATE! Should be 28
```

**Fix:** Change `ConfigurationChanged` to next available number (28)

**Estimated Effort:** 5 minutes

---

## Testing Plan

1. **Unit Tests:** Repository methods with all Actor types
2. **Integration Tests:** Audit log creation, filtering by actor type
3. **UI Tests:**
   - Audit.razor displays all actor types correctly
   - Filter dropdowns include system actors
   - User detail shows correct attribution
4. **Migration Tests:**
   - Test data migration on copy of production DB
   - Verify no data loss
   - Check foreign key integrity

---

## Benefits of Completion

1. **Type Safety:** Eliminate string-based actor tracking, catch errors at compile time
2. **Consistency:** Single Actor pattern across entire codebase
3. **Audit Trail:** Accurate "who did what" tracking for system/Telegram/web actions
4. **UI Clarity:** Proper display of system actors vs unknown users
5. **Future-Proof:** Easy to add new actor types or attributes

---

## Blockers / Risks

- **Breaking Changes:** Database schema changes require downtime
- **Data Migration Complexity:** Need to handle edge cases (malformed UUIDs, Telegram IDs in string fields)
- **Call Site Volume:** 30+ places use audit logging, requires careful refactoring
- **Testing Burden:** Must verify audit trail accuracy across all features

---

## Definition of Done

- [ ] All database tables storing "who" use Actor exclusive arc pattern
- [ ] All repository methods accept `Actor` parameters
- [ ] All UI components display Actor correctly (web user email, Telegram username, system name)
- [ ] Data migrations tested and verified
- [ ] Legacy string-based columns dropped
- [ ] No "Unknown (system)" or similar display bugs
- [ ] 0 errors, 0 warnings build standard maintained
- [ ] Documentation updated (CLAUDE.md, this file)

---

**Total Estimated Effort:** 8-12 hours (across multiple sessions)
**Priority:** Medium-High (audit trail accuracy important for production use)
**Assigned:** To be completed on dev machine

---

## Notes

- **Pre-production status:** Breaking changes are acceptable
- **Readability-first:** Modern features used only when clarity improves
- **No feature changes:** Pure refactoring, preserve all functionality
- **Build quality:** Must maintain 0 errors, 0 warnings standard

**Last Updated:** 2025-10-24
**Next Review:** After ARCH-2 completion or when implementing medium priority optimizations opportunistically
