# Requirements: Dead Code Removal (#396)

**Defined:** 2026-03-16
**Core Value:** Remove all 62 confirmed dead code items without changing runtime behavior

## v1 Requirements

Requirements for this cleanup. Each maps to roadmap phases.

### File Deletions

- [ ] **FILE-01**: Remove Core/Extensions/EnumExtensions.cs (unused generic GetDisplayName)
- [ ] **FILE-02**: Remove Configuration/TelegramOptions.cs (self-documents as no longer used)
- [ ] **FILE-03**: Remove Configuration/OpenAIOptions.cs (superseded by database config)
- [ ] **FILE-04**: Remove Configuration/SendGridOptions.cs (superseded by database config)
- [ ] **FILE-05**: Remove Configuration/EmailOptions.cs (never registered in DI)
- [ ] **FILE-06**: Remove Data/Models/StopWordWithEmailDto.cs (query DTO never used)
- [ ] **FILE-07**: Remove Telegram/Constants/NotificationConstants.cs (dead copy)
- [ ] **FILE-08**: Remove Telegram/Services/Welcome/WelcomeChatPermissions.cs (predefined permissions never referenced)
- [ ] **FILE-09**: Remove Telegram/Models/BotPermissionsTest.cs (replaced by Chat Health)
- [ ] **FILE-10**: Remove Telegram/Models/BotProtectionStats.cs (replaced by analytics views)
- [ ] **FILE-11**: Remove Telegram/Models/FalsePositiveStats.cs (replaced by DetectionAccuracyStats)
- [ ] **FILE-12**: Remove Telegram/Models/DailyFalsePositive.cs (only referenced by dead FalsePositiveStats)
- [ ] **FILE-13**: Remove Telegram/Models/ReportActionResult.cs (zero references)
- [ ] **FILE-14**: Remove Telegram/Services/Media/IMediaNotificationService.cs + MediaNotificationService.cs (registered but never injected)
- [ ] **FILE-15**: Remove Telegram/Services/Moderation/Events/ModerationActionType.cs (superseded by UserActionType)
- [ ] **FILE-16**: Remove TelegramGroupsAdmin/SeoPreviewScraper.cs + SeoPreviewResult.cs (registered but never injected)
- [ ] **FILE-17**: Remove Models/Dialogs/AddSpamSampleData.cs (superseded by AddTrainingSampleData)
- [ ] **FILE-18**: Remove Models/Dialogs/EditSpamSampleData.cs (superseded by EditTrainingSampleData)
- [ ] **FILE-19**: Remove Components/Shared/TelegramPreview.razor (has tests but never rendered)
- [ ] **FILE-20**: Remove Components/Shared/TelegramBotMessage.razor (has tests but never rendered)
- [ ] **FILE-21**: Remove Components/Shared/TelegramUserMessage.razor (has tests but never rendered)
- [ ] **FILE-22**: Remove Components/Shared/TelegramReturnButton.razor (has tests but never rendered)
- [ ] **FILE-23**: Remove Repositories/Mappings/DetectionAccuracyMappings.cs + Models/Analytics/DetectionAccuracyRecord.cs (repo constructs inline)
- [ ] **FILE-24**: Remove Repositories/Mappings/HourlyDetectionStatsMappings.cs + Models/Analytics/HourlyDetectionStats.cs (repo constructs inline)
- [ ] **FILE-25**: Remove Repositories/Mappings/WelcomeResponseSummaryMappings.cs + Models/Analytics/WelcomeResponseSummary.cs (repo constructs inline)
- [ ] **FILE-26**: Remove test-backup.tar.gz (unreferenced test artifact)
- [ ] **FILE-27**: Remove ContentDetection/Services/AdvancedTokenizerService.cs (DI registers simpler TokenizerService)
- [ ] **FILE-28**: Remove ContentDetection/Services/MessageContextProvider.cs (replaced by MessageContextAdapter)
- [ ] **FILE-29**: Remove ContentDetection/Models/DetectionStats.cs (only returned by dead GetStatsAsync)

### DI Registrations

- [ ] **DI-01**: Remove IMediaNotificationService -> MediaNotificationService registration from ServiceCollectionExtensions
- [ ] **DI-02**: Remove unused AddHttpClient registration from ServiceCollectionExtensions

### Dead Methods

- [ ] **METH-01**: Remove IMessageQueryService.GetMessagesBeforeAsync + implementation
- [ ] **METH-02**: Remove IMessageQueryService.GetMessagesByDateRangeAsync + implementation
- [ ] **METH-03**: Remove IMessageQueryService.GetDistinctUserNamesAsync + implementation
- [ ] **METH-04**: Remove IMessageQueryService.GetDistinctChatNamesAsync + implementation
- [ ] **METH-05**: Remove IJobTriggerService.ScheduleOnceAsync + implementation
- [ ] **METH-06**: Remove IJobTriggerService.CancelScheduledJobAsync + implementation
- [ ] **METH-07**: Remove IJobScheduler.IsScheduledAsync + implementation
- [ ] **METH-08**: Remove IBotMediaService.DownloadFileAsBytesAsync + implementation
- [ ] **METH-09**: Remove TelegramLoggingExtensions.GetUserLogDisplayAsync
- [ ] **METH-10**: Remove TelegramLoggingExtensions.GetChatLogDisplayAsync
- [ ] **METH-11**: Remove JobPayloadHelper.GetRequiredPayload()
- [ ] **METH-12**: Remove IDetectionResultsRepository.GetRecentAsync + implementation
- [ ] **METH-13**: Remove IDetectionResultsRepository.GetStatsAsync + implementation
- [ ] **METH-14**: Remove IDetectionResultsRepository.DeleteOlderThanAsync + implementation
- [ ] **METH-15**: Remove IDetectionResultsRepository.GetHamSamplesForSimilarityAsync + implementation
- [ ] **METH-16**: Remove IFileScanQuotaRepository.GetCurrentQuotaAsync, CleanupExpiredQuotasAsync, GetServiceQuotasAsync, ResetQuotaAsync + implementations
- [ ] **METH-17**: Remove ITokenizerService.GetWordFrequencies, IsStopWord + implementations in TokenizerService
- [ ] **METH-18**: Remove ModelMappings.FileScanQuotaModel.ToDto()

### Dead Properties

- [ ] **PROP-01**: Remove ContentCheckRequest.CheckOnly
- [ ] **PROP-02**: Remove ContentCheckRequest.ImageFileName
- [ ] **PROP-03**: Remove ContentCheckRequest.PhotoUrl
- [ ] **PROP-04**: Remove ImageCheckRequest.PhotoUrl

### Dead Enum Value

- [ ] **ENUM-01**: Remove ScanResultType.Suspicious

### Stale Comments

- [ ] **CMNT-01**: Remove misleading "Deprecated" comment on TranslationConfig.cs:34 (property is actively used)
- [ ] **CMNT-02**: Remove transition comment in ConfigurationExtensions.cs:20

### Orphaned Tests

- [ ] **TEST-01**: Remove component tests for 4 Telegram preview components (FILE-19 through FILE-22)
- [ ] **TEST-02**: Remove unit tests for GetRequiredPayload (METH-11)
- [ ] **TEST-03**: Remove unit tests for DownloadFileAsBytesAsync (METH-08)
- [ ] **TEST-04**: Remove integration tests for dead IMessageQueryService methods (METH-01 through METH-04)
- [ ] **TEST-05**: Remove orphaned mapping round-trip tests in ContentDetectionConfigMappingsTests.cs

## v2 Requirements

None — this is a one-shot cleanup milestone.

## Out of Scope

| Feature | Reason |
|---------|--------|
| IFileScanResultRepository.CleanupExpiredResultsAsync wiring | Separate work — #398 |
| IBlocklistSubscriptionsRepository.FindByUrlAsync wiring | Separate work — #399 |
| ContentCheckMetadata population | Separate work — #400 |
| FileScanQuotaModel computed properties | Needs investigation — #401 |
| ConfigRecord.cs + WelcomeConfigMappings.cs design | Design violation, not dead code — #342 |
| New functionality or behavioral changes | Pure deletion only |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| (populated during roadmap creation) | | |

**Coverage:**
- v1 requirements: 47 total
- Mapped to phases: 0
- Unmapped: 47

---
*Requirements defined: 2026-03-16*
*Last updated: 2026-03-16 after initial definition*
