---
phase: 06-data-layer-fixes
plan: "02"
subsystem: testing
tags: [integration-tests, idbcontextfactory, upsert, concurrency, postgres, nunit, testcontainers]

dependency_graph:
  requires:
    - phase: 06-01
      provides: IDbContextFactory-migrated repositories and atomic ON CONFLICT UpsertAsync
  provides:
    - integration-test-coverage-for-data-layer-fixes
  affects: [ci, future-repo-changes]

tech_stack:
  added: []
  patterns:
    - "One IServiceProvider per test fixture (SetUp builds, TearDown disposes) with unique PostgreSQL DB per test"
    - "Separate async scope per test method (CreateAsyncScope) so each test starts from clean DI scope"
    - "Concurrent repo instances from IDbContextFactory directly (not DI) for concurrency test — avoids shared state"
    - "Named cancellationToken: CancellationToken.None at all call sites per project conventions"

key_files:
  created:
    - TelegramGroupsAdmin.IntegrationTests/Repositories/DbContextFactoryMigrationTests.cs
    - TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserUpsertTests.cs
  modified: []

key-decisions:
  - "Concurrent upsert test instantiates TelegramUserRepository directly with shared IDbContextFactory rather than using separate DI scopes — avoids scope teardown race and more closely models real background service concurrency where factory is shared"
  - "ExecuteSqlAsync used for is_trusted/bot_dm_enabled preset in trust-preservation test — verifies column-level exclusion from ON CONFLICT UPDATE SET clause without going through repo layer"

patterns-established:
  - "Admin-controlled field preservation test pattern: set field via raw SQL, call UpsertAsync, assert field unchanged"
  - "Concurrent race test pattern: Task.WhenAll with N direct repo instances, then COUNT(*) assertion"

requirements-completed: [DATA-01, DATA-02]

duration: "~15m"
completed: "2026-03-17"
---

# Phase 06 Plan 02: Integration Tests for Data Layer Fixes Summary

11 NUnit integration tests against real PostgreSQL proving IDbContextFactory per-method context (DATA-01) and atomic ON CONFLICT DO UPDATE upsert (DATA-02) work correctly, including concurrent safety.

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-17T01:37:00Z
- **Completed:** 2026-03-17T01:42:00Z
- **Tasks:** 2
- **Files modified:** 2 (both created)

## Accomplishments

- 7 smoke tests prove each IDbContextFactory-migrated repository performs a round-trip without ObjectDisposedException
- 4 upsert tests prove atomic INSERT ... ON CONFLICT semantics: new user creation, field updates, admin-field preservation, and concurrent race safety
- All 562 integration tests pass — 11 new tests, 0 regressions

## Task Commits

1. **Task 1: IDbContextFactory smoke tests for 7 migrated repositories** - `ad655973` (test)
2. **Task 2: Concurrent upsert integration tests for TelegramUserRepository** - `970aff8a` (test)

## Files Created

- `TelegramGroupsAdmin.IntegrationTests/Repositories/DbContextFactoryMigrationTests.cs` — 7 smoke tests, one per migrated repo (BlocklistSubscriptions, DomainFilters, CachedBlockedDomains, TagDefinitions, PendingNotifications, AdminNotes, UserTags)
- `TelegramGroupsAdmin.IntegrationTests/Repositories/TelegramUserUpsertTests.cs` — 4 upsert tests (new user, existing user update, trust/DM preservation, 10-concurrent race producing exactly 1 row)

## Decisions Made

- Concurrent upsert test instantiates `TelegramUserRepository` directly with the shared `IDbContextFactory` rather than creating 10 separate DI scopes — avoids scope teardown race and more closely mirrors real-world concurrency (background services share a factory, not isolated scopes per invocation).
- `ExecuteSqlAsync` raw SQL used to preset `is_trusted = true` and `bot_dm_enabled = true` in the trust-preservation test — verifies that the ON CONFLICT UPDATE SET clause physically excludes these columns, without going through the repository layer that would mask the behavior.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed nullable reference CS8604 on _serviceProvider in concurrent test**
- **Found during:** Task 2 (TelegramUserUpsertTests.cs)
- **Issue:** `_serviceProvider.GetRequiredService<ILoggerFactory>()` was missing the `!` null-forgiving operator that all other dereferences use; `TreatWarningsAsErrors` made this a build error
- **Fix:** Added `!` null-forgiving operator to match the rest of the file
- **Files modified:** TelegramUserUpsertTests.cs
- **Verification:** Build succeeded, all 4 tests passed
- **Committed in:** 970aff8a (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — build error)
**Impact on plan:** Minor one-character fix, no scope change.

## Issues Encountered

None beyond the nullable fix above.

## Next Phase Readiness

- Both DATA-01 and DATA-02 requirements are now fully verified by integration tests against real PostgreSQL
- Phase 06 complete — 2/2 plans done
- Ready for Phase 07 (backend fixes) or production release of these fixes via PR

---
*Phase: 06-data-layer-fixes*
*Completed: 2026-03-17*
