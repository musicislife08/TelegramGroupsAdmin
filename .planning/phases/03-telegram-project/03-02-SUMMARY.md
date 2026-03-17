---
phase: 03-telegram-project
plan: 02
subsystem: backend
tags: [dead-code, interfaces, telegram, background-jobs, logging]

# Dependency graph
requires:
  - phase: 01-core-and-configuration
    provides: Core config and model deletions (no runtime dependencies on those)
  - phase: 02-data-and-mapping
    provides: Data DTO and mapping deletions (no runtime dependencies on those)
provides:
  - 11 dead method declarations removed from 6 interfaces
  - 11 corresponding implementations removed
  - 10 orphaned test methods removed from 3 test files
  - Cleaned stale mock setups referencing removed interface methods
affects: [04-web-ui-project, 05-tests-project]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - TelegramGroupsAdmin.Telegram/Services/IMessageQueryService.cs
    - TelegramGroupsAdmin.Telegram/Services/MessageQueryService.cs
    - TelegramGroupsAdmin.Core/Services/IJobTriggerService.cs
    - TelegramGroupsAdmin.BackgroundJobs/Services/JobTriggerService.cs
    - TelegramGroupsAdmin.Core/BackgroundJobs/IJobScheduler.cs
    - TelegramGroupsAdmin.BackgroundJobs/Services/QuartzJobScheduler.cs
    - TelegramGroupsAdmin.Telegram/Services/Bot/IBotMediaService.cs
    - TelegramGroupsAdmin.Telegram/Services/Bot/BotMediaService.cs
    - TelegramGroupsAdmin.Telegram/Extensions/TelegramLoggingExtensions.cs
    - TelegramGroupsAdmin.BackgroundJobs/Helpers/JobPayloadHelper.cs
    - TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs
    - TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs
    - TelegramGroupsAdmin.UnitTests/BackgroundJobs/Helpers/JobPayloadHelperTests.cs
    - TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/BotMediaServiceTests.cs
    - TelegramGroupsAdmin.IntegrationTests/Repositories/MessageHistoryRepositoryTests.cs

key-decisions:
  - "Removed unused ITelegramUserRepository/IManagedChatsRepository using directive from TelegramLoggingExtensions after removing async extension blocks"

patterns-established: []

requirements-completed: [MTD-01, MTD-02, MTD-03, MTD-04, MTD-05, MTD-06, MTD-07, MTD-08, MTD-09, MTD-10, MTD-11, TEST-02, TEST-03, TEST-04]

# Metrics
duration: 5min
completed: 2026-03-16
---

# Phase 3 Plan 2: Telegram Project Dead Method Removal Summary

**Removed 11 dead methods from 6 interfaces/implementations across Telegram, BackgroundJobs, and Core projects, plus 10 orphaned tests**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-16T21:49:46Z
- **Completed:** 2026-03-16T21:55:16Z
- **Tasks:** 2
- **Files modified:** 15

## Accomplishments
- Removed 4 dead query methods (GetMessagesBeforeAsync, GetMessagesByDateRangeAsync, GetDistinctUserNamesAsync, GetDistinctChatNamesAsync) from IMessageQueryService and MessageQueryService
- Removed 2 dead scheduling methods (ScheduleOnceAsync, CancelScheduledJobAsync) from IJobTriggerService and JobTriggerService
- Removed IsScheduledAsync from IJobScheduler and QuartzJobScheduler
- Removed DownloadFileAsBytesAsync from IBotMediaService and BotMediaService
- Removed 2 async repository logging extensions (GetUserLogDisplayAsync, GetChatLogDisplayAsync) from TelegramLoggingExtensions
- Removed GetRequiredPayload from JobPayloadHelper
- Removed 10 orphaned test methods across 3 test files
- Cleaned up stale IsScheduledAsync mock setups in E2E and integration test infrastructure

## Task Commits

Each task was committed atomically:

1. **Task 1: Remove dead methods from interfaces and implementations** - `77c8272b` (refactor)
2. **Task 2: Remove orphaned test methods for dead code** - `33b44214` (test)

## Files Created/Modified
- `TelegramGroupsAdmin.Telegram/Services/IMessageQueryService.cs` - Removed 4 dead method declarations
- `TelegramGroupsAdmin.Telegram/Services/MessageQueryService.cs` - Removed 4 dead method implementations
- `TelegramGroupsAdmin.Core/Services/IJobTriggerService.cs` - Removed ScheduleOnceAsync, CancelScheduledJobAsync declarations
- `TelegramGroupsAdmin.BackgroundJobs/Services/JobTriggerService.cs` - Removed ScheduleOnceAsync, CancelScheduledJobAsync implementations
- `TelegramGroupsAdmin.Core/BackgroundJobs/IJobScheduler.cs` - Removed IsScheduledAsync declaration
- `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzJobScheduler.cs` - Removed IsScheduledAsync implementation
- `TelegramGroupsAdmin.Telegram/Services/Bot/IBotMediaService.cs` - Removed DownloadFileAsBytesAsync declaration
- `TelegramGroupsAdmin.Telegram/Services/Bot/BotMediaService.cs` - Removed DownloadFileAsBytesAsync implementation
- `TelegramGroupsAdmin.Telegram/Extensions/TelegramLoggingExtensions.cs` - Removed 2 async extension blocks and unused using
- `TelegramGroupsAdmin.BackgroundJobs/Helpers/JobPayloadHelper.cs` - Removed GetRequiredPayload method
- `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs` - Removed stale IsScheduledAsync mock setup
- `TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs` - Removed stale IsScheduledAsync mock setup
- `TelegramGroupsAdmin.UnitTests/BackgroundJobs/Helpers/JobPayloadHelperTests.cs` - Removed 3 GetRequiredPayload test methods
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/BotMediaServiceTests.cs` - Removed 2 DownloadFileAsBytesAsync test methods
- `TelegramGroupsAdmin.IntegrationTests/Repositories/MessageHistoryRepositoryTests.cs` - Removed 5 dead query test methods

## Decisions Made
- Removed unused `using TelegramGroupsAdmin.Telegram.Repositories;` from TelegramLoggingExtensions after the async extension blocks (the only consumers) were deleted

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed stale IsScheduledAsync mock setups in test infrastructure**
- **Found during:** Task 1 (IsScheduledAsync removal verification)
- **Issue:** Two test infrastructure files (TestWebApplicationFactory.cs and BackupServiceTests.cs) had NSubstitute mock setups for IJobScheduler.IsScheduledAsync, which no longer exists on the interface
- **Fix:** Removed the mock setup lines from both files
- **Files modified:** TelegramGroupsAdmin.E2ETests/Infrastructure/TestWebApplicationFactory.cs, TelegramGroupsAdmin.IntegrationTests/Services/Backup/BackupServiceTests.cs
- **Verification:** Grep confirms no remaining references to IsScheduledAsync in any .cs files except planning docs
- **Committed in:** 77c8272b (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Essential fix -- without removing these mock setups, the build would fail on a non-existent interface method. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All Telegram, BackgroundJobs, and Core dead methods removed
- All orphaned tests cleaned up
- Ready for Phase 4 (Web UI) and Phase 5 (Tests) dead code cleanup

---
*Phase: 03-telegram-project*
*Completed: 2026-03-16*

## Self-Check: PASSED
- All 11 modified files exist on disk
- Both task commits (77c8272b, 33b44214) verified in git log
- No dead method references remain in any .cs files (grep verified)
