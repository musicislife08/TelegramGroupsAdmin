# Roadmap: TelegramGroupsAdmin

## Milestones

- ✅ **v1.0 Dead Code Removal** - Phases 1-5 (shipped 2026-03-17)
- 🚧 **v1.1 Bug Fix Sweep** - Phases 6-8 (in progress)

## Phases

<details>
<summary>✅ v1.0 Dead Code Removal (Phases 1-5) - SHIPPED 2026-03-17</summary>

### Phase 1: Core and Configuration
**Goal**: Dead files in Core and Configuration projects are deleted and the stale transition comment in ConfigurationExtensions is removed
**Depends on**: Nothing (first phase)
**Requirements**: FILE-01, FILE-02, FILE-03, FILE-04, FILE-05, CMNT-02
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the deletions
  2. `Core/Extensions/EnumExtensions.cs` no longer exists in the repository
  3. All four dead Configuration options files (TelegramOptions, OpenAIOptions, SendGridOptions, EmailOptions) no longer exist
  4. The transition comment block in `ConfigurationExtensions.cs` line 20 is gone; the surrounding code is intact and compiles
**Plans**: 1/1 plans complete

Plans:
- [x] 01-01: Delete 5 dead files and remove stale transition comment

### Phase 2: Data and Mapping Models
**Goal**: Dead Data model and the three dead main-app analytics mapping pairs (model + mapping file) are deleted
**Depends on**: Phase 1
**Requirements**: FILE-06, FILE-23, FILE-24, FILE-25
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the deletions
  2. `Data/Models/StopWordWithEmailDto.cs` no longer exists
  3. The three dead mapping file pairs (DetectionAccuracyMappings + DetectionAccuracyRecord, HourlyDetectionStatsMappings + HourlyDetectionStats, WelcomeResponseSummaryMappings + WelcomeResponseSummary) no longer exist
  4. Repositories that formerly referenced those mapping files still compile and return data correctly
**Plans**: 1/1 plans complete

Plans:
- [x] 02-01: Delete 7 dead files (1 Data DTO + 3 mapping pairs)

### Phase 3: Telegram Project
**Goal**: All dead code within the Telegram project is removed — dead files, one dead DI registration, 11 dead interface methods with their implementations, and three groups of orphaned tests
**Depends on**: Phase 2
**Requirements**: FILE-07, FILE-08, FILE-09, FILE-10, FILE-11, FILE-12, FILE-13, FILE-14, FILE-15, DI-01, MTD-01, MTD-02, MTD-03, MTD-04, MTD-05, MTD-06, MTD-07, MTD-08, MTD-09, MTD-10, MTD-11, TEST-02, TEST-03, TEST-04
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. All nine dead Telegram model/service/constant files no longer exist
  3. `IMediaNotificationService` and its implementation are gone and the DI registration for it is removed
  4. Dead methods removed from all affected interfaces and implementations
  5. Orphaned test classes for deleted code no longer exist and the test project compiles cleanly
**Plans**: 2/2 plans complete

Plans:
- [x] 03-01: Delete 10 dead Telegram files and remove IMediaNotificationService DI registration
- [x] 03-02: Remove 11 dead methods from interfaces/implementations and delete orphaned tests

### Phase 4: Main Application
**Goal**: All dead code in the main TelegramGroupsAdmin project is removed — dead service files, dead DI registration, dead dialog models, dead Blazor components, a stale comment, and the orphaned component tests
**Depends on**: Phase 3
**Requirements**: FILE-16, FILE-17, FILE-18, FILE-19, FILE-20, FILE-21, FILE-22, FILE-26, DI-02, CMNT-01, TEST-01
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. `SeoPreviewScraper.cs`, `SeoPreviewResult.cs`, and the `AddHttpClient` DI registration are gone
  3. `AddSpamSampleData.cs`, `EditSpamSampleData.cs`, and the four dead Razor components no longer exist
  4. `test-backup.tar.gz` no longer exists in the repository
  5. The misleading "Deprecated" comment on `TranslationConfig.cs:34` is removed
  6. Orphaned test classes for deleted Razor components no longer exist and the test project compiles cleanly
**Plans**: 2/2 plans complete

Plans:
- [x] 04-01: Delete 9 dead files, remove SeoPreviewScraper DI registration, fix misleading comment
- [x] 04-02: Delete 4 orphaned component test files and verify full solution build

### Phase 5: ContentDetection Project
**Goal**: All dead code in the ContentDetection project is removed — dead service files, dead interface methods with implementations, dead properties on check request types, the dead enum value, and the orphaned mapping round-trip test
**Depends on**: Phase 4
**Requirements**: FILE-27, FILE-28, FILE-29, MTD-12, MTD-13, MTD-14, MTD-15, MTD-16, MTD-17, MTD-18, PROP-01, PROP-02, PROP-03, PROP-04, ENUM-01, TEST-05
**Success Criteria** (what must be TRUE):
  1. `dotnet build` passes with zero errors after the removals
  2. `AdvancedTokenizerService.cs`, `MessageContextProvider.cs`, and `DetectionStats.cs` no longer exist
  3. Dead methods removed from `IDetectionResultsRepository`, `IFileScanQuotaRepository`, `ITokenizerService`, and `ModelMappings.FileScanQuotaModel`
  4. `ContentCheckRequest` no longer has `CheckOnly`, `ImageFileName`, or `PhotoUrl`; `ImageCheckRequest` no longer has `PhotoUrl`
  5. `ScanResultType.Suspicious` no longer exists and all switch expressions still compile
  6. Orphaned round-trip tests removed and all remaining tests pass
**Plans**: 2/2 plans complete

Plans:
- [x] 05-01: Delete 3 dead files and remove 11 dead methods from interfaces/implementations plus 1 dead mapping extension
- [x] 05-02: Remove 4 dead properties, dead enum value, and orphaned CasConfig tests

</details>

### v1.1 Bug Fix Sweep (In Progress)

**Milestone Goal:** Fix 7 known correctness bugs across data layer, backend services, and Blazor frontend.

#### Phase 6: Data Layer Fixes
**Goal**: Repository database access is correct and race-condition-free — all repositories use IDbContextFactory for safe lifetime management, and concurrent TelegramUser upserts are serialized
**Depends on**: Phase 5
**Requirements**: DATA-01, DATA-02
**Success Criteria** (what must be TRUE):
  1. Background services that call any repository no longer throw ObjectDisposedException on DbContext
  2. All repositories construct their DbContext via IDbContextFactory (no constructor-injected AppDbContext)
  3. Running TelegramUserRepository.UpsertAsync concurrently for the same user produces exactly one record with no duplicate key violation
  4. Integration tests in `TelegramGroupsAdmin.IntegrationTests` cover DATA-01 (repository behavior with a real Testcontainers PostgreSQL instance confirms no ObjectDisposedException) and DATA-02 (concurrent UpsertAsync calls for the same user confirm exactly one resulting row with no duplicate key violation)
  5. `dotnet build` passes and all tests pass after the changes
**Plans**: 2 plans

Plans:
- [ ] 06-01-PLAN.md — Migrate 7 repos to IDbContextFactory and rewrite UpsertAsync with ON CONFLICT
- [ ] 06-02-PLAN.md — Integration tests for IDbContextFactory migration and concurrent upsert

#### Phase 7: Backend Service Fixes
**Goal**: Background services behave correctly at runtime — health orchestrator methods are invoked, startup and runtime logs are clean, and marking a message as spam populates image training samples
**Depends on**: Phase 5
**Requirements**: BACK-01, BACK-02, BACK-03
**Success Criteria** (what must be TRUE):
  1. Health orchestrator CheckHealthAsync and MarkInactiveAsync are called by the appropriate host or job (verifiable via logs or debugger)
  2. Application startup produces no spurious log warnings from the three identified sources (#309)
  3. When a moderator marks a message as spam, its media is downloaded and a record appears in image_training_samples (if media was not already cached)
  4. Unit tests in `TelegramGroupsAdmin.UnitTests` cover BACK-01 (verify CheckHealthAsync and MarkInactiveAsync are invoked on the orchestrator via mocked dependencies) and BACK-03 (verify the download-if-not-cached branch executes when media is absent from the local cache); BACK-02 warnings are covered by unit or integration tests depending on each warning's source
  5. `dotnet build` passes and all tests pass after the changes
**Plans**: 2/2 plans complete

Plans:
- [x] 07-01-PLAN.md — Eliminate spurious log warnings + defensive media download on spam marking (BACK-02, BACK-03)
- [x] 07-02-PLAN.md — Wire CheckHealthAsync and MarkInactiveAsync in health orchestrator (BACK-01)

#### Phase 8: Frontend Fixes
**Goal**: Blazor UI renders and responds correctly — analytics percentages update when the time range changes, and timezone detection does not throw during server-side prerendering
**Depends on**: Phase 5
**Requirements**: FRONT-01, FRONT-02
**Success Criteria** (what must be TRUE):
  1. Selecting a different time range on the Analytics overview page recalculates and displays updated percentage values without a full page reload
  2. The timezone detection JS interop call does not throw a JSException or similar error during Blazor server-side prerendering
  3. Users visiting the app for the first time (cold prerender) see no error page or console error from timezone detection
  4. Component tests in `TelegramGroupsAdmin.ComponentTests` cover FRONT-01 (analytics card component recalculates percentage values when a different time range is selected); FRONT-02 may require E2E tests (`TelegramGroupsAdmin.E2ETests`) instead of component tests since bUnit stubs JS interop — evaluate during planning whether bUnit's fake IJSRuntime can adequately simulate the prerender scenario or if a Playwright test is needed
  5. `dotnet build` passes and all tests pass after the changes
**Plans**: 4 plans

Plans:
- [x] 08-01-PLAN.md — Fix analytics growth percentages: decouple from date range, add DailyAverageGrowthPercent, hide on All Time
- [x] 08-02-PLAN.md — Move timezone detection to MainLayout cascade, update LocalTimestamp to C# conversion
- [ ] 08-03-PLAN.md — Gap closure: bUnit component tests for overview card chip recalculation (FRONT-01)
- [ ] 08-04-PLAN.md — Gap closure: Playwright E2E test for timezone prerender safety (FRONT-02)

#### Phase 08.1: Fix review-all critical and suggestions (INSERTED)

**Goal:** Fix 7 /review-all findings -- 2 critical vacuous test assertions, 1 empty catch block, and 4 convention/quality improvements across tests, components, and services
**Requirements**: REVIEW-FIX-01, REVIEW-FIX-02, REVIEW-FIX-03, REVIEW-FIX-04, REVIEW-FIX-05, REVIEW-FIX-06, REVIEW-FIX-07
**Depends on:** Phase 8
**Plans:** 1 plan

Plans:
- [ ] 08.1-01-PLAN.md — Fix test assertions, add logging, enforce conventions across 12 files

## Progress

**Execution Order:**
Phases execute in numeric order: 6 → 7 → 8

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Core and Configuration | v1.0 | 1/1 | Complete | 2026-03-16 |
| 2. Data and Mapping Models | v1.0 | 1/1 | Complete | 2026-03-16 |
| 3. Telegram Project | v1.0 | 2/2 | Complete | 2026-03-16 |
| 4. Main Application | v1.0 | 2/2 | Complete | 2026-03-16 |
| 5. ContentDetection Project | v1.0 | 2/2 | Complete | 2026-03-17 |
| 6. Data Layer Fixes | v1.1 | 0/2 | Not started | - |
| 7. Backend Service Fixes | v1.1 | 2/2 | Complete | 2026-03-17 |
| 8. Frontend Fixes | 4/4 | Complete   | 2026-03-17 | - |
| 08.1. Fix review-all findings | v1.1 | 0/1 | Planned | - |
