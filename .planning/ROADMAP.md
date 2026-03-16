# Roadmap: Dead Code Removal (#396)

## Overview

Remove all 62 confirmed dead code items from the TelegramGroupsAdmin solution in a single feature branch. Work proceeds layer by layer — Core/Configuration first, then Data, then Telegram and ContentDetection projects, then the main application — so that each phase leaves the solution in a compiling state with all remaining tests passing.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Core and Configuration** - Delete dead files and fix stale comment in Core and Configuration projects
- [ ] **Phase 2: Data and Mapping Models** - Delete dead Data model and dead main-app mapping records/files
- [ ] **Phase 3: Telegram Project** - Delete dead Telegram files, remove dead DI registration, remove dead interface methods and orphaned tests
- [ ] **Phase 4: Main Application** - Delete dead main-app files, DI registration, stale comment, and orphaned component tests
- [ ] **Phase 5: ContentDetection Project** - Delete dead ContentDetection services/models, dead interface methods, dead properties, dead enum value, and orphaned mapping test

## Phase Details

### Phase 1: Core and Configuration
**Goal**: Dead files in Core and Configuration projects are deleted and the stale transition comment in ConfigurationExtensions is removed
**Depends on**: Nothing (first phase)
**Requirements**: FILE-01, FILE-02, FILE-03, FILE-04, FILE-05, CMNT-02
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the deletions
  2. `Core/Extensions/EnumExtensions.cs` no longer exists in the repository
  3. All four dead Configuration options files (TelegramOptions, OpenAIOptions, SendGridOptions, EmailOptions) no longer exist
  4. The transition comment block in `ConfigurationExtensions.cs` line 20 is gone; the surrounding code is intact and compiles
**Plans:** 1 plan

Plans:
- [ ] 01-01-PLAN.md — Delete 5 dead files and remove stale transition comment

### Phase 2: Data and Mapping Models
**Goal**: Dead Data model and the three dead main-app analytics mapping pairs (model + mapping file) are deleted
**Depends on**: Phase 1
**Requirements**: FILE-06, FILE-23, FILE-24, FILE-25
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the deletions
  2. `Data/Models/StopWordWithEmailDto.cs` no longer exists
  3. The three dead mapping file pairs (DetectionAccuracyMappings + DetectionAccuracyRecord, HourlyDetectionStatsMappings + HourlyDetectionStats, WelcomeResponseSummaryMappings + WelcomeResponseSummary) no longer exist
  4. Repositories that formerly referenced those mapping files still compile and return data correctly (inline construction preserved)
**Plans**: TBD

### Phase 3: Telegram Project
**Goal**: All dead code within the Telegram project is removed — dead files, one dead DI registration, 11 dead interface methods with their implementations, and three groups of orphaned tests
**Depends on**: Phase 2
**Requirements**: FILE-07, FILE-08, FILE-09, FILE-10, FILE-11, FILE-12, FILE-13, FILE-14, FILE-15, DI-01, MTD-01, MTD-02, MTD-03, MTD-04, MTD-05, MTD-06, MTD-07, MTD-08, MTD-09, MTD-10, MTD-11, TEST-02, TEST-03, TEST-04
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. All nine dead Telegram model/service/constant files no longer exist
  3. `IMediaNotificationService` and its implementation are gone and the DI registration for it is removed from `ServiceCollectionExtensions`
  4. `IMessageQueryService`, `IJobTriggerService`, `IJobScheduler`, `IBotMediaService`, `TelegramLoggingExtensions`, and `JobPayloadHelper` no longer declare or implement the removed methods
  5. Test classes for `GetRequiredPayload`, `DownloadFileAsBytesAsync`, and the dead `IMessageQueryService` methods no longer exist and the test project compiles cleanly
**Plans**: TBD

### Phase 4: Main Application
**Goal**: All dead code in the main TelegramGroupsAdmin project is removed — dead service files, dead DI registration, dead dialog models, dead Blazor components, a stale comment, and the orphaned component tests
**Depends on**: Phase 3
**Requirements**: FILE-16, FILE-17, FILE-18, FILE-19, FILE-20, FILE-21, FILE-22, FILE-26, DI-02, CMNT-01, TEST-01
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. `SeoPreviewScraper.cs`, `SeoPreviewResult.cs`, and the `AddHttpClient` DI registration are gone
  3. `AddSpamSampleData.cs`, `EditSpamSampleData.cs`, and the four dead Telegram preview Razor components no longer exist
  4. `test-backup.tar.gz` no longer exists in the repository
  5. The misleading "Deprecated" comment on `TranslationConfig.cs:34` is removed and the property is visibly undeprecated
  6. Test classes for the four deleted Razor components no longer exist and the test project compiles cleanly
**Plans**: TBD

### Phase 5: ContentDetection Project
**Goal**: All dead code in the ContentDetection project is removed — dead service files, dead interface methods with implementations, dead properties on check request types, the dead enum value, and the orphaned mapping round-trip test
**Depends on**: Phase 4
**Requirements**: FILE-27, FILE-28, FILE-29, MTD-12, MTD-13, MTD-14, MTD-15, MTD-16, MTD-17, MTD-18, PROP-01, PROP-02, PROP-03, PROP-04, ENUM-01, TEST-05
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. `AdvancedTokenizerService.cs`, `MessageContextProvider.cs`, and `DetectionStats.cs` no longer exist
  3. `IDetectionResultsRepository`, `IFileScanQuotaRepository`, `ITokenizerService`, and `ModelMappings.FileScanQuotaModel` no longer declare or implement the removed methods/extension
  4. `ContentCheckRequest` no longer has `CheckOnly`, `ImageFileName`, or `PhotoUrl`; `ImageCheckRequest` no longer has `PhotoUrl`; all call sites compile
  5. `ScanResultType.Suspicious` no longer exists and all switch expressions / pattern matches over `ScanResultType` still compile
  6. The orphaned round-trip tests in `ContentDetectionConfigMappingsTests.cs` are removed and the test project compiles and all remaining tests pass
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Core and Configuration | 0/1 | Planned | - |
| 2. Data and Mapping Models | 0/TBD | Not started | - |
| 3. Telegram Project | 0/TBD | Not started | - |
| 4. Main Application | 0/TBD | Not started | - |
| 5. ContentDetection Project | 0/TBD | Not started | - |
