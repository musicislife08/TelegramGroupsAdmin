# Requirements: Dead Code Removal (#396)

**Defined:** 2026-03-16
**Core Value:** Remove all 62 confirmed dead code items without changing runtime behavior

## v1 Requirements

Requirements for this cleanup. Each maps to roadmap phases.

### File Deletions

- [x] **FILE-01**: Remove Core/Extensions/EnumExtensions.cs (unused generic GetDisplayName)
- [x] **FILE-02**: Remove Configuration/TelegramOptions.cs (self-documents as no longer used)
- [x] **FILE-03**: Remove Configuration/OpenAIOptions.cs (superseded by database config)
- [x] **FILE-04**: Remove Configuration/SendGridOptions.cs (superseded by database config)
- [x] **FILE-05**: Remove Configuration/EmailOptions.cs (never registered in DI)
- [x] **FILE-06**: Remove Data/Models/StopWordWithEmailDto.cs (query DTO never used)
- [x] **FILE-07**: Remove Telegram/Constants/NotificationConstants.cs (dead copy)
- [x] **FILE-08**: Remove Telegram/Services/Welcome/WelcomeChatPermissions.cs (predefined permissions never referenced)
- [x] **FILE-09**: Remove Telegram/Models/BotPermissionsTest.cs (replaced by Chat Health)
- [x] **FILE-10**: Remove Telegram/Models/BotProtectionStats.cs (replaced by analytics views)
- [x] **FILE-11**: Remove Telegram/Models/FalsePositiveStats.cs (replaced by DetectionAccuracyStats)
- [x] **FILE-12**: Remove Telegram/Models/DailyFalsePositive.cs (only referenced by dead FalsePositiveStats)
- [x] **FILE-13**: Remove Telegram/Models/ReportActionResult.cs (zero references)
- [x] **FILE-14**: Remove Telegram/Services/Media/IMediaNotificationService.cs + MediaNotificationService.cs (registered but never injected)
- [x] **FILE-15**: Remove Telegram/Services/Moderation/Events/ModerationActionType.cs (superseded by UserActionType)
- [x] **FILE-16**: Remove TelegramGroupsAdmin/SeoPreviewScraper.cs + SeoPreviewResult.cs (registered but never injected)
- [x] **FILE-17**: Remove Models/Dialogs/AddSpamSampleData.cs (superseded by AddTrainingSampleData)
- [x] **FILE-18**: Remove Models/Dialogs/EditSpamSampleData.cs (superseded by EditTrainingSampleData)
- [x] **FILE-19**: Remove Components/Shared/TelegramPreview.razor (has tests but never rendered)
- [x] **FILE-20**: Remove Components/Shared/TelegramBotMessage.razor (has tests but never rendered)
- [x] **FILE-21**: Remove Components/Shared/TelegramUserMessage.razor (has tests but never rendered)
- [x] **FILE-22**: Remove Components/Shared/TelegramReturnButton.razor (has tests but never rendered)
- [x] **FILE-23**: Remove Repositories/Mappings/DetectionAccuracyMappings.cs + Models/Analytics/DetectionAccuracyRecord.cs (repo constructs inline)
- [x] **FILE-24**: Remove Repositories/Mappings/HourlyDetectionStatsMappings.cs + Models/Analytics/HourlyDetectionStats.cs (repo constructs inline)
- [x] **FILE-25**: Remove Repositories/Mappings/WelcomeResponseSummaryMappings.cs + Models/Analytics/WelcomeResponseSummary.cs (repo constructs inline)
- [x] **FILE-26**: Remove test-backup.tar.gz (unreferenced test artifact)
- [ ] **FILE-27**: Remove ContentDetection/Services/AdvancedTokenizerService.cs (DI registers simpler TokenizerService)
- [ ] **FILE-28**: Remove ContentDetection/Services/MessageContextProvider.cs (replaced by MessageContextAdapter)
- [ ] **FILE-29**: Remove ContentDetection/Models/DetectionStats.cs (only returned by dead GetStatsAsync)

### DI Registrations

- [x] **DI-01**: Remove IMediaNotificationService -> MediaNotificationService registration from ServiceCollectionExtensions
- [x] **DI-02**: Remove unused AddHttpClient registration from ServiceCollectionExtensions

### Dead Methods

- [x] **MTD-01**: Remove IMessageQueryService.GetMessagesBeforeAsync + implementation
- [x] **MTD-02**: Remove IMessageQueryService.GetMessagesByDateRangeAsync + implementation
- [x] **MTD-03**: Remove IMessageQueryService.GetDistinctUserNamesAsync + implementation
- [x] **MTD-04**: Remove IMessageQueryService.GetDistinctChatNamesAsync + implementation
- [x] **MTD-05**: Remove IJobTriggerService.ScheduleOnceAsync + implementation
- [x] **MTD-06**: Remove IJobTriggerService.CancelScheduledJobAsync + implementation
- [x] **MTD-07**: Remove IJobScheduler.IsScheduledAsync + implementation
- [x] **MTD-08**: Remove IBotMediaService.DownloadFileAsBytesAsync + implementation
- [x] **MTD-09**: Remove TelegramLoggingExtensions.GetUserLogDisplayAsync
- [x] **MTD-10**: Remove TelegramLoggingExtensions.GetChatLogDisplayAsync
- [x] **MTD-11**: Remove JobPayloadHelper.GetRequiredPayload()
- [ ] **MTD-12**: Remove IDetectionResultsRepository.GetRecentAsync + implementation
- [ ] **MTD-13**: Remove IDetectionResultsRepository.GetStatsAsync + implementation
- [ ] **MTD-14**: Remove IDetectionResultsRepository.DeleteOlderThanAsync + implementation
- [ ] **MTD-15**: Remove IDetectionResultsRepository.GetHamSamplesForSimilarityAsync + implementation
- [ ] **MTD-16**: Remove IFileScanQuotaRepository.GetCurrentQuotaAsync, CleanupExpiredQuotasAsync, GetServiceQuotasAsync, ResetQuotaAsync + implementations
- [ ] **MTD-17**: Remove ITokenizerService.GetWordFrequencies, IsStopWord + implementations in TokenizerService
- [ ] **MTD-18**: Remove ModelMappings.FileScanQuotaModel.ToDto()

### Dead Properties

- [x] **PROP-01**: Remove ContentCheckRequest.CheckOnly
- [x] **PROP-02**: Remove ContentCheckRequest.ImageFileName
- [x] **PROP-03**: Remove ContentCheckRequest.PhotoUrl
- [x] **PROP-04**: Remove ImageCheckRequest.PhotoUrl

### Dead Enum Value

- [x] **ENUM-01**: Remove ScanResultType.Suspicious

### Stale Comments

- [x] **CMNT-01**: Remove misleading "Deprecated" comment on TranslationConfig.cs:34 (property is actively used)
- [x] **CMNT-02**: Remove transition comment in ConfigurationExtensions.cs:20

### Orphaned Tests

- [x] **TEST-01**: Remove component tests for 4 Telegram preview components (FILE-19 through FILE-22)
- [x] **TEST-02**: Remove unit tests for GetRequiredPayload (MTD-11)
- [x] **TEST-03**: Remove unit tests for DownloadFileAsBytesAsync (MTD-08)
- [x] **TEST-04**: Remove integration tests for dead IMessageQueryService methods (MTD-01 through MTD-04)
- [x] **TEST-05**: Remove orphaned mapping round-trip tests in ContentDetectionConfigMappingsTests.cs

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
| FILE-01 | Phase 1 | Complete |
| FILE-02 | Phase 1 | Complete |
| FILE-03 | Phase 1 | Complete |
| FILE-04 | Phase 1 | Complete |
| FILE-05 | Phase 1 | Complete |
| CMNT-02 | Phase 1 | Complete |
| FILE-06 | Phase 2 | Complete |
| FILE-23 | Phase 2 | Complete |
| FILE-24 | Phase 2 | Complete |
| FILE-25 | Phase 2 | Complete |
| FILE-07 | Phase 3 | Complete |
| FILE-08 | Phase 3 | Complete |
| FILE-09 | Phase 3 | Complete |
| FILE-10 | Phase 3 | Complete |
| FILE-11 | Phase 3 | Complete |
| FILE-12 | Phase 3 | Complete |
| FILE-13 | Phase 3 | Complete |
| FILE-14 | Phase 3 | Complete |
| FILE-15 | Phase 3 | Complete |
| DI-01 | Phase 3 | Complete |
| MTD-01 | Phase 3 | Complete |
| MTD-02 | Phase 3 | Complete |
| MTD-03 | Phase 3 | Complete |
| MTD-04 | Phase 3 | Complete |
| MTD-05 | Phase 3 | Complete |
| MTD-06 | Phase 3 | Complete |
| MTD-07 | Phase 3 | Complete |
| MTD-08 | Phase 3 | Complete |
| MTD-09 | Phase 3 | Complete |
| MTD-10 | Phase 3 | Complete |
| MTD-11 | Phase 3 | Complete |
| TEST-02 | Phase 3 | Complete |
| TEST-03 | Phase 3 | Complete |
| TEST-04 | Phase 3 | Complete |
| FILE-16 | Phase 4 | Complete |
| FILE-17 | Phase 4 | Complete |
| FILE-18 | Phase 4 | Complete |
| FILE-19 | Phase 4 | Complete |
| FILE-20 | Phase 4 | Complete |
| FILE-21 | Phase 4 | Complete |
| FILE-22 | Phase 4 | Complete |
| FILE-26 | Phase 4 | Complete |
| DI-02 | Phase 4 | Complete |
| CMNT-01 | Phase 4 | Complete |
| TEST-01 | Phase 4 | Complete |
| FILE-27 | Phase 5 | Pending |
| FILE-28 | Phase 5 | Pending |
| FILE-29 | Phase 5 | Pending |
| MTD-12 | Phase 5 | Pending |
| MTD-13 | Phase 5 | Pending |
| MTD-14 | Phase 5 | Pending |
| MTD-15 | Phase 5 | Pending |
| MTD-16 | Phase 5 | Pending |
| MTD-17 | Phase 5 | Pending |
| MTD-18 | Phase 5 | Pending |
| PROP-01 | Phase 5 | Complete |
| PROP-02 | Phase 5 | Complete |
| PROP-03 | Phase 5 | Complete |
| PROP-04 | Phase 5 | Complete |
| ENUM-01 | Phase 5 | Complete |
| TEST-05 | Phase 5 | Complete |

**Coverage:**
- v1 requirements: 61 total (FILE-01..29=29, DI-01..02=2, MTD-01..18=18, PROP-01..04=4, ENUM-01=1, CMNT-01..02=2, TEST-01..05=5)
- Mapped to phases: 61
- Unmapped: 0

Note: Requirements header previously read "47 total" — actual count is 61 requirement IDs as listed above.

---
*Requirements defined: 2026-03-16*
*Last updated: 2026-03-16 after roadmap creation (traceability populated)*
