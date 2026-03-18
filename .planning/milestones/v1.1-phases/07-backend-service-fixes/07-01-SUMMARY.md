---
phase: 07-backend-service-fixes
plan: 01
subsystem: backend
tags: [logging, media, training, telegram, content-detection]

requires: []
provides:
  - "UseHttpsRedirection removed from HTTP pipeline (container runs behind reverse proxy)"
  - "Photo/video file-not-found warnings downgraded to Debug in orchestrator and repositories"
  - "Missing photos queued for re-download via IMediaRefetchQueueService in ContentDetectionOrchestrator"
  - "MediaType.Photo = 8 added to enum for photo refetch support"
  - "ITelegramMediaService interface extracted from TelegramMediaService for testability"
  - "TrainingHandler saves both image and video training samples when marking spam"
  - "TrainingHandler defensively downloads media when MediaLocalPath is null but file ID exists"
affects: [training, content-detection, media-refetch, moderation]

tech-stack:
  added: []
  patterns:
    - "ITelegramMediaService: thin interface extracted from concrete class for DI testability"
    - "Defensive download guard: if MediaLocalPath null + fileId present, download before training sample"
    - "MediaType.Photo = 8: photos now enumerated as media type for unified refetch queue support"

key-files:
  created:
    - TelegramGroupsAdmin.Telegram/Services/ITelegramMediaService.cs
  modified:
    - TelegramGroupsAdmin/WebApplicationExtensions.cs
    - TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs
    - TelegramGroupsAdmin.ContentDetection/Repositories/ImageTrainingSamplesRepository.cs
    - TelegramGroupsAdmin.ContentDetection/Repositories/VideoTrainingSamplesRepository.cs
    - TelegramGroupsAdmin.Telegram/Models/MediaType.cs
    - TelegramGroupsAdmin.Telegram/Services/Media/MediaRefetchWorkerService.cs
    - TelegramGroupsAdmin.Telegram/Services/TelegramMediaService.cs
    - TelegramGroupsAdmin.Core/Utilities/MediaPathUtilities.cs
    - TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/TrainingHandler.cs
    - TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs
    - TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/TrainingHandlerTests.cs

key-decisions:
  - "MediaType.Photo = 8 added to enum so photo refetch can flow through EnqueueMediaAsync — worker updated to use PhotoFileId when MediaType == Photo"
  - "ITelegramMediaService thin interface introduced for testability — registered as forwarding factory from existing TelegramMediaService scoped registration"
  - "Defensive download placed BEFORE training sample saves — sample repos gracefully return false if file still unavailable after download attempt"
  - "ContentDetection repositories do NOT add refetch queue calls — dependency direction prevents ContentDetection from referencing Telegram project"

patterns-established:
  - "Photo re-download pattern: EnqueueMediaAsync with MediaType.Photo → worker resolves PhotoFileId branch"
  - "Defensive media fetch: MediaLocalPath == null + fileId present → try download → update DB if successful → continue regardless"

requirements-completed: [BACK-02, BACK-03]

duration: 11min
completed: 2026-03-17
---

# Phase 7 Plan 01: Backend Service Fixes Summary

**Downgraded 4 spurious photo/video warnings to Debug, wired missing-photo refetch queue in ContentDetectionOrchestrator, and added defensive media download + video training samples to TrainingHandler**

## Performance

- **Duration:** 11 min
- **Started:** 2026-03-17T15:25:25Z
- **Completed:** 2026-03-17T15:36:20Z
- **Tasks:** 2 (Task 1: warnings fix; Task 2: TDD defensive download)
- **Files modified:** 11

## Accomplishments

- Removed `UseHttpsRedirection()` from HTTP pipeline — eliminates startup warning in container (reverse proxy handles TLS)
- Downgraded "Photo file not found" and "Video file not found" warnings to `LogDebug` across ContentDetectionOrchestrator, ImageTrainingSamplesRepository, and VideoTrainingSamplesRepository
- ContentDetectionOrchestrator now enqueues missing photos for re-download via `IMediaRefetchQueueService` when the local file is absent
- TrainingHandler defensively downloads media when `MediaLocalPath` is null but a file ID exists — ensures training samples are populated even when original download failed
- TrainingHandler now saves both image AND video training samples on spam marking
- 12 unit tests pass covering all new and existing TrainingHandler behaviors

## Task Commits

1. **Task 1: Eliminate spurious warnings (BACK-02)** — `19746d21` (fix)
2. **Task 2 prep: extract ITelegramMediaService interface** — `1a1f8a85` (chore)
3. **Task 2 RED: failing tests for download-when-missing** — `89381aba` (test)
4. **Task 2 GREEN: defensive media download implementation** — `24539d7c` (feat)

## Files Created/Modified

- `TelegramGroupsAdmin/WebApplicationExtensions.cs` — Removed `UseHttpsRedirection()`
- `TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs` — LogDebug + EnqueueMediaAsync for missing photos
- `TelegramGroupsAdmin.ContentDetection/Repositories/ImageTrainingSamplesRepository.cs` — LogWarning → LogDebug
- `TelegramGroupsAdmin.ContentDetection/Repositories/VideoTrainingSamplesRepository.cs` — LogWarning → LogDebug
- `TelegramGroupsAdmin.Telegram/Models/MediaType.cs` — Added `Photo = 8`
- `TelegramGroupsAdmin.Telegram/Services/Media/MediaRefetchWorkerService.cs` — Handle MediaType.Photo via PhotoFileId
- `TelegramGroupsAdmin.Telegram/Services/TelegramMediaService.cs` — Implements ITelegramMediaService; added Photo → ".jpg" extension
- `TelegramGroupsAdmin.Core/Utilities/MediaPathUtilities.cs` — Added Photo → "photo" subdirectory mapping
- `TelegramGroupsAdmin.Telegram/Services/ITelegramMediaService.cs` — New interface for testability
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/TrainingHandler.cs` — Defensive download + video samples
- `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` — Register ITelegramMediaService
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/TrainingHandlerTests.cs` — 12 tests (7 existing + 5 new)

## Decisions Made

- **MediaType.Photo = 8:** Photo is not in the existing MediaType enum but the refetch queue uses MediaType. Added Photo as enum value 8 and updated worker to branch on PhotoFileId vs MediaFileId depending on type.
- **ITelegramMediaService interface:** TelegramMediaService was a concrete class with no interface, making it untestable. Introduced thin interface with just `DownloadAndSaveMediaAsync`. Registered via forwarding factory to share the same scoped instance.
- **Defensive download before samples:** Placed download attempt BEFORE both image and video sample saves so the repositories see the updated local path in the database if download succeeds.
- **ContentDetection repos do NOT refetch:** Adding refetch queue calls to ImageTrainingSamplesRepository or VideoTrainingSamplesRepository would violate the dependency direction (ContentDetection cannot reference Telegram). Refetch is handled upstream in ContentDetectionOrchestrator.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] MediaType.Photo missing from enum**
- **Found during:** Task 1 (ContentDetectionOrchestrator refetch queue wiring)
- **Issue:** Plan instructed calling `EnqueueMediaAsync(..., MediaType.Photo)` but `Photo` was not a member of the `MediaType` enum (which only covered animation/video/audio/sticker/document). Build error CS0117.
- **Fix:** Added `Photo = 8` to `MediaType` enum, added `Photo → "photo"` subdirectory mapping in `MediaPathUtilities`, added `Photo → ".jpg"` extension fallback in `TelegramMediaService.GetFileExtension`, and updated `MediaRefetchWorkerService` to resolve `PhotoFileId` (instead of `MediaFileId`) when `MediaType == Photo`.
- **Files modified:** `TelegramGroupsAdmin.Telegram/Models/MediaType.cs`, `TelegramGroupsAdmin.Core/Utilities/MediaPathUtilities.cs`, `TelegramGroupsAdmin.Telegram/Services/TelegramMediaService.cs`, `TelegramGroupsAdmin.Telegram/Services/Media/MediaRefetchWorkerService.cs`
- **Verification:** Build passes zero errors; photo refetch logic handles PhotoFileId branch correctly
- **Committed in:** `19746d21` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug: missing enum value)
**Impact on plan:** Required to make photo refetch functional. No scope creep.

## Issues Encountered

- Prior session had already committed some Task 2 work (test file and interface extraction) on `feature/07-02-health-orchestrator-check-health-wiring`. Execution continued from the correct point — `TrainingHandler` implementation was the remaining GREEN work.
- Pre-existing `ChatHealthRefreshOrchestratorTests.cs` (untracked file from another in-progress plan) causes unit test project build errors — out of scope for this plan, deferred.

## Next Phase Readiness

- Phase 07 Plan 01 requirements complete (BACK-02, BACK-03)
- TrainingHandler is now robust against missing local media paths
- Photo refetch via queue is wired end-to-end in ContentDetectionOrchestrator
- Ready for remaining Phase 07 plans if any are pending

---
*Phase: 07-backend-service-fixes*
*Completed: 2026-03-17*
