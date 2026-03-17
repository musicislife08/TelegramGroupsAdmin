---
phase: 07-backend-service-fixes
plan: 02
subsystem: backend
tags: [health-check, telegram-bot, orchestrator, concurrent-dictionary, unit-tests, nsubstitute]

# Dependency graph
requires:
  - phase: 06-data-layer-fixes
    provides: "Correct data layer (UpsertAsync, IDbContextFactory) that MarkInactiveAsync builds on"
provides:
  - "ChatHealthRefreshOrchestrator delegates reachability to BotChatService.CheckHealthAsync"
  - "In-memory per-chat consecutive failure counter via IChatHealthCache (ConcurrentDictionary)"
  - "MarkInactiveAsync called after 3 consecutive failures, counter resets after marking"
  - "Counter resets on any successful reachability check"
  - "11 unit tests covering all 3-strike rule behaviors"
affects: [health-check-job, chat-management-ui, bot-moderation-pipeline]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ConcurrentDictionary on Singleton service for cross-scope in-memory state"
    - "Reachability gate pattern: CheckHealthAsync gates the full PerformHealthCheckAsync"
    - "3-strike failure pattern: track consecutive failures, act at threshold, reset on action or success"

key-files:
  created:
    - TelegramGroupsAdmin.UnitTests/Telegram/Services/ChatHealthRefreshOrchestratorTests.cs
  modified:
    - TelegramGroupsAdmin.Telegram/Services/ChatHealthRefreshOrchestrator.cs
    - TelegramGroupsAdmin.Telegram/Services/Bot/IChatHealthCache.cs
    - TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs

key-decisions:
  - "Failure counter stored in IChatHealthCache (Singleton) not on orchestrator (Scoped) — counter must survive across scoped lifetimes"
  - "ConcurrentDictionary with AddOrUpdate for thread-safe increment; TryRemove for reset"
  - "CheckHealthAsync runs BEFORE PerformHealthCheckAsync — quick reachability gate avoids wasted API calls on unreachable chats"
  - "Counter resets after MarkInactiveAsync call so 4th+ failures start a new 3-strike sequence"
  - "Transient errors count toward the 3-strike threshold (by design per plan spec)"

patterns-established:
  - "Cross-scope in-memory state pattern: Scoped services use Singleton cache for state that must outlive one request"
  - "Reachability gate: cheap BotChatService.CheckHealthAsync check prevents expensive PerformHealthCheckAsync on unreachable chats"

requirements-completed: [BACK-01]

# Metrics
duration: 15min
completed: 2026-03-17
---

# Phase 7 Plan 02: ChatHealthRefreshOrchestrator Health Wiring Summary

**BotChatService.CheckHealthAsync wired as reachability gate with in-memory 3-strike MarkInactiveAsync counter stored in IChatHealthCache Singleton**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-17T15:15:00Z
- **Completed:** 2026-03-17T15:33:48Z
- **Tasks:** 1 (TDD: test + implementation)
- **Files modified:** 4

## Accomplishments

- Added `IncrementFailureCount(long chatId)` and `ResetFailureCount(long chatId)` to `IChatHealthCache` and implemented with `ConcurrentDictionary<long, int>` in `ChatHealthCache` (Singleton, thread-safe)
- Wired `BotChatService.CheckHealthAsync` as a quick reachability gate at the top of `RefreshHealthForChatAsync` — unreachable chats never reach the expensive `PerformHealthCheckAsync`
- After 3 consecutive failures, `ManagedChatsRepository.MarkInactiveAsync` is called and the counter resets; counter also resets on any successful check
- Created 11 unit tests covering: delegation call, 1/2-strike no-action, 3-strike trigger, counter reset on success, fail-fail-success-fail-fail pattern, per-chat isolation, and counter reset after MarkInactive

## Task Commits

1. **Task 1: Wire CheckHealthAsync delegation and 3-strike MarkInactiveAsync** - `97fd1246` (feat)

**Plan metadata:** TBD (docs commit)

## Files Created/Modified

- `TelegramGroupsAdmin.Telegram/Services/ChatHealthRefreshOrchestrator.cs` - Added CheckHealthAsync gate with 3-strike failure counter logic; returns early with Error health status when unreachable
- `TelegramGroupsAdmin.Telegram/Services/Bot/IChatHealthCache.cs` - Added IncrementFailureCount/ResetFailureCount to interface
- `TelegramGroupsAdmin.Telegram/Services/Bot/ChatHealthCache.cs` - Implemented IncrementFailureCount/ResetFailureCount with ConcurrentDictionary; field added alongside existing health cache
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/ChatHealthRefreshOrchestratorTests.cs` - 11 unit tests (new file)

## Decisions Made

- Failure counter stored in `IChatHealthCache` (Singleton) not on the orchestrator itself (Scoped) — Scoped services are recreated per scope, so an in-class field would reset every invocation
- `ConcurrentDictionary.AddOrUpdate` for atomic increment; `TryRemove` for reset — matches pattern already used for `_healthCache` in the same class
- `CheckHealthAsync` gates `PerformHealthCheckAsync` entirely — if unreachable, the full health check is skipped and an Error status is written to cache directly
- Counter resets after `MarkInactiveAsync` so subsequent failures start a fresh 3-strike sequence

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] MediaType.Photo missing from enum — build-blocking pre-existing bug**
- **Found during:** Initial build attempt (pre-task)
- **Issue:** `ContentDetectionOrchestrator.cs` referenced `MediaType.Photo` which didn't exist in the `MediaType` enum, blocking compilation of the whole solution
- **Fix:** Added `Photo = 8` to `TelegramGroupsAdmin.Telegram/Models/MediaType.cs`
- **Files modified:** `TelegramGroupsAdmin.Telegram/Models/MediaType.cs`
- **Verification:** Solution builds with zero errors
- **Committed in:** `19746d21` (pre-existing 07-01 commit already included `MediaType.Photo = 8` — confirmed duplicate, no action needed)

Note: Investigation revealed `MediaType.Photo = 8` was already committed in `19746d21` (07-01 fix). The working tree showed this as modified only due to the `develop` base not yet having that commit merged.

---

**Total deviations:** 1 investigation (pre-existing fix confirmed already handled by 07-01)
**Impact on plan:** No scope creep. 07-02 task executed exactly as specified.

## Issues Encountered

- `TelegramPhotoService.ForPartsOf<>()` in test SetUp failed with `IOException: Read-only file system: '/data'` — the constructor calls `Directory.CreateDirectory`. Fixed by passing `AppOptions { DataPath = Path.GetTempPath() }` to provide a writable temp path for the partial substitute.

## Next Phase Readiness

- Health orchestrator now has a functioning 3-strike safety net for stale active chats
- Phase 07-03 can proceed (frontend service fixes)
- No blockers

---
*Phase: 07-backend-service-fixes*
*Completed: 2026-03-17*
