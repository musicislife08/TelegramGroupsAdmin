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
