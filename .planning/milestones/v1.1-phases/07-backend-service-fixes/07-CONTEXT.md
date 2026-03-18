# Phase 7: Backend Service Fixes - Context

**Gathered:** 2026-03-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix three backend service correctness issues: wire up the health orchestrator's unused methods (#333), eliminate spurious startup/runtime log warnings (#309 items 1-2 only), and ensure media is downloaded when manually marking spam to populate training samples (#262). No new features, no API changes beyond what's needed for the fixes.

</domain>

<decisions>
## Implementation Decisions

### Health orchestrator wiring (BACK-01, #333)
- Wire the orchestrator to delegate to `BotChatService.CheckHealthAsync` instead of doing health checks inline
- Wire `ManagedChatsRepository.MarkInactiveAsync` to be called when a chat is detected unreachable
- Require **3 consecutive failures** before marking a chat inactive (resilient to transient Telegram API errors)
- Failure counter stored **in-memory** (resets on restart) — acceptable for homelab single-instance
- This is a safety net for missed `MyChatMember` events (network blip during bot kick)

### Spurious warnings (BACK-02, #309)
- **Item 1**: Remove `UseHttpsRedirection()` call in `WebApplicationExtensions.cs:43` — container never serves HTTPS, TLS termination handled by reverse proxy
- **Item 2**: Downgrade "Photo file not found" warnings to `LogDebug` in `ContentDetectionOrchestrator.cs:78` and `ImageTrainingSamplesRepository.cs:102`, AND queue the missing file for re-download via `MediaRefetchQueueService`
- **Item 3**: Already resolved — `CleanupBackgroundService` was migrated to `DataCleanupJob` (commit `7d033363`). Update GitHub issue #309 with a comment noting this, and mention in the PR
- `PhotoHashService.cs:36` already logs at Trace level — no change needed there
- If re-download fails (file expired on Telegram servers): log at Debug and give up. No retry loop.

### Media download on spam marking (BACK-03, #262)
- Add defensive download-if-not-cached guard in `TrainingHandler.CreateSpamSampleAsync`
- Fix applies to BOTH image_training_samples AND video_training_samples
- Check if `MediaLocalPath` is null despite having a `PhotoFileId`/video file ID, and download if missing
- Media IS already downloaded on message arrival for non-Document types — this is a safety net for edge cases (download failed, file expired/cleaned)
- Do NOT investigate root cause of null MediaLocalPath — just handle it defensively

### Testing strategy
- **BACK-01**: Integration tests in `TelegramGroupsAdmin.IntegrationTests` with mocked Telegram responses — verify orchestrator delegates to CheckHealthAsync, verify 3 consecutive failures trigger MarkInactiveAsync, verify counter resets on success
- **BACK-02**: Unit tests verifying UseHttpsRedirection is removed (or build verification), and that photo warning log sites emit Debug not Warning
- **BACK-03**: Unit tests in `TelegramGroupsAdmin.UnitTests` verifying the download-if-not-cached branch for both image and video paths

### Claude's Discretion
- Exact implementation of the in-memory failure counter (ConcurrentDictionary, simple dictionary, etc.)
- How to integrate the re-fetch queue call at the photo warning sites
- Whether BACK-02 item 1 (UseHttpsRedirection removal) needs a dedicated test or if build verification is sufficient

</decisions>

<specifics>
## Specific Ideas

- User confirmed media IS downloaded on message arrival — the #262 bug is a narrow edge case, not a systemic missing download
- #309 item 3 is already resolved — comment on GitHub issue and note in PR
- Mocking Telegram types is heavy (concrete non-virtual properties) — use integration tests with mocked responses, not unit tests with NSubstitute

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MediaRefetchQueueService` + `MediaRefetchWorkerService` — existing queue-based re-download mechanism, already handles the download+retry lifecycle
- `BotChatService.CheckHealthAsync` — method exists but unused, ready to wire
- `ManagedChatsRepository.MarkInactiveAsync` — method exists but unused, ready to wire
- `MediaProcessingHandler.ProcessMediaAsync` — downloads media on message arrival (non-Document types)
- `BotMediaService.DownloadAndSaveMediaAsync` — actual download implementation

### Established Patterns
- Health checks run via `ChatHealthRefreshOrchestrator` as a background job
- Photo warnings in 3 locations: `PhotoHashService.cs:36` (already Trace), `ContentDetectionOrchestrator.cs:78` (Warning), `ImageTrainingSamplesRepository.cs:102` (Warning)
- `TrainingHandler.CreateSpamSampleAsync` handles both image and video training sample creation
- Existing unit tests in `TrainingHandlerTests.cs` — add to these for BACK-03

### Integration Points
- `ChatHealthRefreshOrchestrator` → `BotChatService.CheckHealthAsync` → Telegram API
- `ChatHealthRefreshOrchestrator` → `ManagedChatsRepository.MarkInactiveAsync` after 3 failures
- Warning sites → `MediaRefetchQueueService.EnqueueAsync` for re-download
- `TrainingHandler` → `BotMediaService` for defensive media download

</code_context>

<deferred>
## Deferred Ideas

- Investigating root cause of null MediaLocalPath — fix defensively for now, investigate if it becomes frequent
- #84 (convert remaining BackgroundServices to Quartz jobs) — separate issue, not in this milestone
- Proactive media download for all flagged messages — not needed since media is already downloaded on arrival

</deferred>

---

*Phase: 07-backend-service-fixes*
*Context gathered: 2026-03-16*
