---
phase: 04-main-application
plan: 01
subsystem: cleanup
tags: [dead-code, razor, di, http-client, seo-scraper]

# Dependency graph
requires:
  - phase: 03-telegram-project
    provides: "Dead code removal pattern for Telegram layer files"
provides:
  - "9 dead files removed from main TelegramGroupsAdmin project"
  - "Dead SeoPreviewScraper DI registration removed"
  - "Dead SeoScraperTimeout constant removed"
  - "Accurate TranslationConfig.LatinScriptThreshold documentation"
affects: [04-main-application]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - TelegramGroupsAdmin/ServiceCollectionExtensions.cs
    - TelegramGroupsAdmin/Constants/HttpConstants.cs
    - TelegramGroupsAdmin.Configuration/Models/ContentDetection/TranslationConfig.cs

key-decisions:
  - "Deleted 4 orphaned component test files alongside their dead Razor components (tests that only test dead code are themselves dead)"

patterns-established: []

requirements-completed: [FILE-16, FILE-17, FILE-18, FILE-19, FILE-20, FILE-21, FILE-22, FILE-26, DI-02, CMNT-01]

# Metrics
duration: 4min
completed: 2026-03-16
---

# Phase 4 Plan 1: Main Application Dead File Cleanup Summary

**Deleted 9 dead files (SEO scraper, dialog models, Telegram preview components, test artifact) and cleaned 3 stale code references from ServiceCollectionExtensions, HttpConstants, and TranslationConfig**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-16T22:10:58Z
- **Completed:** 2026-03-16T22:15:11Z
- **Tasks:** 2
- **Files modified:** 16 (9 deleted + 4 dead tests deleted + 3 edited)

## Accomplishments
- Removed SeoPreviewScraper.cs, SeoPreviewResult.cs, and their DI registration and timeout constant
- Removed AddSpamSampleData.cs and EditSpamSampleData.cs (dead dialog models)
- Removed 4 dead Telegram preview Razor components and their 4 component test files
- Removed test-backup.tar.gz (89KB orphaned test artifact)
- Fixed misleading "Deprecated" comment on TranslationConfig.LatinScriptThreshold

## Task Commits

Each task was committed atomically:

1. **Task 1: Delete 9 dead files from the main application** - `a21b771f` (refactor)
2. **Task 2: Remove dead DI registration, dead constant, and fix misleading comment** - `41597916` (refactor)

## Files Created/Modified
- `TelegramGroupsAdmin/SeoPreviewScraper.cs` - Deleted (dead SEO scraper service)
- `TelegramGroupsAdmin/SeoPreviewResult.cs` - Deleted (dead SEO result model)
- `TelegramGroupsAdmin/Models/Dialogs/AddSpamSampleData.cs` - Deleted (dead dialog model)
- `TelegramGroupsAdmin/Models/Dialogs/EditSpamSampleData.cs` - Deleted (dead dialog model)
- `TelegramGroupsAdmin/Components/Shared/TelegramPreview.razor` - Deleted (dead preview component)
- `TelegramGroupsAdmin/Components/Shared/TelegramBotMessage.razor` - Deleted (dead bot message component)
- `TelegramGroupsAdmin/Components/Shared/TelegramUserMessage.razor` - Deleted (dead user message component)
- `TelegramGroupsAdmin/Components/Shared/TelegramReturnButton.razor` - Deleted (dead return button component)
- `TelegramGroupsAdmin/test-backup.tar.gz` - Deleted (orphaned test artifact)
- `TelegramGroupsAdmin.ComponentTests/Components/TelegramBotMessageTests.cs` - Deleted (dead test)
- `TelegramGroupsAdmin.ComponentTests/Components/TelegramPreviewTests.cs` - Deleted (dead test)
- `TelegramGroupsAdmin.ComponentTests/Components/TelegramReturnButtonTests.cs` - Deleted (dead test)
- `TelegramGroupsAdmin.ComponentTests/Components/TelegramUserMessageTests.cs` - Deleted (dead test)
- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` - Removed AddHttpClient<SeoPreviewScraper> block
- `TelegramGroupsAdmin/Constants/HttpConstants.cs` - Removed SeoScraperTimeout constant
- `TelegramGroupsAdmin.Configuration/Models/ContentDetection/TranslationConfig.cs` - Fixed LatinScriptThreshold doc comment

## Decisions Made
- Deleted 4 orphaned component test files (TelegramBotMessageTests, TelegramPreviewTests, TelegramReturnButtonTests, TelegramUserMessageTests) alongside their dead Razor components -- tests that only test dead code are themselves dead, consistent with project-wide decision from Init phase

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Deleted 4 orphaned component test files**
- **Found during:** Task 2 (build verification)
- **Issue:** Build failed with 84 errors in ComponentTests project -- 4 test files referenced the deleted Razor components (TelegramBotMessage, TelegramPreview, TelegramReturnButton, TelegramUserMessage)
- **Fix:** Deleted all 4 test files via `git rm` since they exclusively tested deleted components
- **Files modified:** TelegramGroupsAdmin.ComponentTests/Components/TelegramBotMessageTests.cs, TelegramGroupsAdmin.ComponentTests/Components/TelegramPreviewTests.cs, TelegramGroupsAdmin.ComponentTests/Components/TelegramReturnButtonTests.cs, TelegramGroupsAdmin.ComponentTests/Components/TelegramUserMessageTests.cs
- **Verification:** `dotnet build` passes with 0 errors, 0 warnings
- **Committed in:** 41597916 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug/blocking)
**Impact on plan:** Essential for build correctness. Tests were dead code themselves. No scope creep.

## Issues Encountered
None beyond the deviation documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Main application dead file cleanup complete
- Ready for 04-02 (remaining main application dead code)
- Build compiles cleanly with 0 errors, 0 warnings

## Self-Check: PASSED

- FOUND: 04-01-SUMMARY.md
- FOUND: a21b771f (Task 1 commit)
- FOUND: 41597916 (Task 2 commit)

---
*Phase: 04-main-application*
*Completed: 2026-03-16*
