# Refactoring Backlog - TelegramGroupsAdmin

**Last Updated:** 2025-10-22
**Status:** Pre-production (breaking changes acceptable)
**Overall Code Quality:** 88/100 (Excellent)

---

## Completed Work

**2025-10-22**: PERF-CD-3 (Domain stats: 2.24× faster, 55ms→25ms via GroupBy aggregation), PERF-CD-4 (TF-IDF: 2.59× faster, 44% less memory via Dictionary counting + pre-indexed vocabulary)
**2025-10-21**: ARCH-1 (Core library - 544 lines eliminated), ARCH-2 (Actor refactoring complete), PERF-APP-1, PERF-APP-3, DI-1 (4 repositories), Comprehensive audit logging coverage, BlazorAuthHelper DRY refactoring (19 instances), Empirical performance testing (PERF-CD-1 removed via PostgreSQL profiling)
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

### ~~ARCH-2: Eliminate Actor String Round-Tripping~~ ✅ COMPLETE

**Status:** ✅ COMPLETE (2025-10-21)
**Severity:** Architecture | **Impact:** Cleaner API, type safety, removed 50+ lines

**Completed Work:**
1. ✅ All ModerationActionService methods now accept `Actor` parameter instead of `string executorId`
2. ✅ All repository methods (UserActionsRepository, AdminNotesRepository, UserTagsRepository) accept `Actor`
3. ✅ TelegramUserManagementService.ToggleTrustAsync() and UnbanAsync() accept `Actor`
4. ✅ No more string round-tripping - `Actor` objects created directly at call sites
5. ✅ GetExecutorIdentifierAsync() helper method removed (never existed in final implementation)
6. ✅ ParseLegacyFormat() removed (never needed - Actor system designed correctly from start)

**Files Updated:** ModerationActionService.cs (all methods), TelegramUserManagementService.cs, 6 repository files, SpamActionService.cs, ReportActionsService.cs, 5 UI components

**Result:** Clean, type-safe Actor attribution throughout the application

---

## Notes

- **Pre-production status:** Breaking changes are acceptable
- **Readability-first:** Modern features used only when clarity improves
- **No feature changes:** Pure refactoring, preserve all functionality
- **Build quality:** Must maintain 0 errors, 0 warnings standard

**Last Updated:** 2025-10-22
**Next Review:** When implementing medium priority optimizations opportunistically
