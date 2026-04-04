# Codebase Structure

**Analysis Date:** 2026-03-15

## Directory Layout

```
TelegramGroupsAdmin/ (root)
├── TelegramGroupsAdmin/                  # Main web app (Blazor + API)
│   ├── Program.cs                        # Startup, DI wiring, middleware pipeline
│   ├── ServiceCollectionExtensions.cs    # AddBlazorServices(), AddApplicationServices()
│   ├── WebApplicationExtensions.cs       # ConfigurePipeline(), MapApiEndpoints()
│   ├── Components/                       # Blazor components
│   │   ├── Pages/                        # Page components (routable)
│   │   ├── Shared/                       # Shared components (non-routable)
│   │   ├── Reports/                      # Report-specific components
│   │   └── Layout/                       # MainLayout.razor, NavMenu.razor
│   ├── Endpoints/                        # API endpoints (minimal APIs)
│   │   ├── AuthEndpoints.cs              # /api/auth/*, /api/auth/verify-totp
│   │   └── EmailVerificationEndpoints.cs # /api/verify-email
│   ├── Models/                           # UI DTOs for Blazor (never Data layer *Dto)
│   │   ├── Analytics/
│   │   └── Dialogs/
│   ├── Services/                         # Business logic services
│   │   ├── Auth/                         # IAuthService, password hashing, TOTP
│   │   ├── Email/                        # SendGrid email service
│   │   └── Notifications/                # INotificationService, web push
│   ├── Repositories/                     # Data access (Web UI specific)
│   │   ├── UserRepository.cs             # User management operations
│   │   └── Mappings/                     # *Dto ↔ Domain model conversions
│   ├── Auth/                             # Cookie auth implementation
│   ├── Constants/                        # BlazorConstants.cs
│   ├── Helpers/                          # UI helper utilities
│   ├── wwwroot/                          # Static files (CSS, JS)
│   ├── Data/                             # Test/backup data
│   └── lang-models/                      # ML model files (generated)
│
├── TelegramGroupsAdmin.Data/             # Data persistence layer (EF Core)
│   ├── AppDbContext.cs                   # DbContext with all DbSets
│   ├── AppDbContextFactory.cs            # EF Core design-time factory
│   ├── Migrations/                       # EF Core migrations (auto-generated)
│   ├── Models/                           # All *Dto entity classes
│   │   ├── MessageRecordDto.cs
│   │   ├── DetectionResultRecordDto.cs
│   │   ├── TelegramUserDto.cs
│   │   └── ... (80+ total)
│   ├── Attributes/                       # [Table], [Column] metadata
│   ├── Constants/                        # SQL-related constants
│   └── Extensions/                       # DbContext fluent config helpers
│
├── TelegramGroupsAdmin.Core/             # Shared domain models (no dependencies on other projects)
│   ├── Models/                           # Core domain types
│   │   ├── Actor.cs                      # Who did something (User/TgUser/System)
│   │   ├── UserIdentity.cs               # Type-safe Telegram user reference
│   │   ├── ChatIdentity.cs               # Type-safe Telegram chat reference
│   │   ├── AuditEventType.cs             # Enum of auditable events
│   │   └── BackgroundJobSettings/        # Job-specific config POCOs
│   ├── Repositories/                     # Core repository interfaces (implemented elsewhere)
│   │   ├── IAuditLogRepository.cs
│   │   ├── INotificationPreferencesRepository.cs
│   │   └── Mappings/                     # Core-layer *Dto ↔ model conversions
│   ├── JobPayloads/                      # Job payload DTOs (for Quartz.NET)
│   │   ├── FileScanJobPayload.cs
│   │   └── ... (job-specific payloads)
│   ├── Services/                         # Core services (audit, notifications)
│   │   ├── IAuditService.cs
│   │   ├── IAuditLogService.cs
│   │   └── AI/                           # AI-related interfaces
│   ├── Extensions/                       # Extension methods
│   │   ├── CoreLoggingExtensions.cs      # Identity type logging helpers
│   │   └── ...
│   ├── Security/                         # Security utilities
│   └── Utilities/                        # Shared utilities
│
├── TelegramGroupsAdmin.Telegram/         # Telegram bot domain logic
│   ├── Handlers/                         # Message processing handlers
│   │   ├── ContentDetectionOrchestrator.cs  # Orchestrates all checks
│   │   ├── TranslationHandler.cs         # Non-Latin translation
│   │   ├── MediaProcessingHandler.cs     # Image/video processing
│   │   ├── FileScanningHandler.cs        # Async file scanning
│   │   └── MessageEditProcessor.cs       # Edit message handling
│   ├── Services/                         # Telegram-specific services (50+ files)
│   │   ├── Bot/                          # Bot polling, message processing
│   │   │   ├── TelegramBotPollingHost.cs # Background service (IHostedService)
│   │   │   └── MessageProcessingService.cs
│   │   ├── BotCommands/                  # Command handlers (/ban, /kick, etc.)
│   │   ├── Moderation/                   # Moderation intents and services
│   │   │   ├── ModerationIntents.cs      # 14 intent types
│   │   │   └── IBotModerationService.cs
│   │   ├── BackgroundServices/           # ChatHealthRefreshOrchestrator, etc.
│   │   ├── BanCallbackService.cs         # Ban action callbacks
│   │   ├── BanCelebrationService.cs      # Ban celebration GIFs + captions
│   │   ├── ExamFlowService.cs            # Entrance exam orchestration
│   │   ├── CasCheckService.cs            # Combot Anti-Spam integration
│   │   └── DmNotifications/              # DM notification delivery
│   ├── Repositories/                     # Data access (Telegram domain)
│   │   ├── ITelegramUserRepository.cs
│   │   ├── IMessageRepository.cs
│   │   ├── IManagedChatRepository.cs
│   │   └── Mappings/                     # Dto → domain model conversions
│   ├── Models/                           # Telegram domain models
│   │   ├── Ban.cs
│   │   ├── Mute.cs
│   │   └── ...
│   ├── Extensions/                       # Extension methods
│   │   ├── TelegramLoggingExtensions.cs  # SDK type logging
│   │   ├── IdentityExtensions.cs         # Identity factory methods
│   │   └── ...
│   ├── Constants/                        # Telegram-specific constants
│   └── Helpers/                          # Utilities (TelegramDisplayName, etc.)
│
├── TelegramGroupsAdmin.ContentDetection/  # Content detection algorithms (self-contained)
│   ├── Checks/                           # Algorithm implementations
│   │   ├── SpamCheckV2.cs                # ML.NET spam classifier
│   │   ├── BayesClassifierCheck.cs       # Naive Bayes classifier
│   │   ├── OCRCheck.cs                   # OpenAI Vision (OCR + translate + classify in single call)
│   │   ├── LanguageDetectionCheck.cs
│   │   ├── ImpersonationAlertCheck.cs    # Impersonation detection
│   │   ├── VedisCheckV2.cs               # ClamAV file scanner integration
│   │   ├── VirusTotalCheck.cs            # VirusTotal API integration
│   │   ├── URLFilterCheck.cs             # Blocklist-based URL filtering
│   │   └── ProfilePhotoScanCheck.cs
│   ├── Services/                         # Detection support services
│   │   ├── ClamAVScannerService.cs       # ClamAV protocol client
│   │   ├── VirusTotalScannerService.cs   # VirusTotal API client
│   │   ├── BlocklistSyncService.cs       # URL blocklist management
│   │   └── ...
│   ├── ML/                               # Machine learning models
│   │   ├── IMLTextClassifierService.cs   # ML.NET text classifier
│   │   ├── IBayesClassifierService.cs    # Bayes classifier
│   │   └── ...
│   ├── Repositories/                     # Detection result persistence
│   │   ├── IDetectionResultRepository.cs
│   │   ├── ITrainingLabelRepository.cs
│   │   └── Mappings/
│   ├── Models/                           # Detection domain models
│   │   ├── ContentCheckResult.cs         # Detection algorithm result
│   │   └── ...
│   ├── Abstractions/                     # Interfaces for checks
│   │   └── IContentCheckV2.cs            # Standard check interface
│   ├── Extensions/                       # DI registration
│   │   └── ServiceCollectionExtensions.cs  # AddContentDetection()
│   └── Constants/                        # Content detection constants
│
├── TelegramGroupsAdmin.Configuration/    # App configuration (IOptions<T>)
│   ├── Models/                           # Config POCOs
│   │   ├── AppOptions.cs                 # App-level settings
│   │   ├── TelegramOptions.cs            # Telegram bot token, settings
│   │   ├── OpenAIOptions.cs
│   │   ├── SendGridOptions.cs
│   │   └── ... (configuration classes)
│   ├── Repositories/                     # Config persistence
│   │   └── IConfigRepository.cs
│   ├── Mappings/
│   └── Extensions/                       # ServiceCollectionExtensions
│       └── ServiceCollectionExtensions.cs  # AddApplicationConfiguration()
│
├── TelegramGroupsAdmin.BackgroundJobs/   # Quartz.NET scheduled jobs
│   ├── Jobs/                             # IJob implementations (18+ jobs)
│   │   ├── TextClassifierRetrainingJob.cs
│   │   ├── BayesClassifierRetrainingJob.cs
│   │   ├── FileScanJob.cs                # Async file scanning queue
│   │   ├── DataCleanupJob.cs             # Retention cleanup
│   │   ├── ScheduledBackupJob.cs         # Encrypted backups
│   │   ├── RotateBackupPassphraseJob.cs
│   │   ├── ChatHealthCheckJob.cs
│   │   ├── ProfileScanJob.cs
│   │   ├── TempbanExpiryJob.cs           # Temporary ban expiration
│   │   └── ...
│   ├── Services/                         # Job support services
│   │   ├── Backup/IBackupService.cs      # Encrypted backup/restore
│   │   └── ...
│   ├── Extensions/                       # DI & Quartz configuration
│   │   └── ServiceCollectionExtensions.cs  # AddBackgroundJobs()
│   ├── Listeners/                        # Quartz.NET listeners
│   │   └── JobLoggingListener.cs
│   ├── Helpers/                          # Job utilities
│   │   └── JobPayloadHelper.cs
│   └── Constants/                        # Job-related constants
│
├── TelegramGroupsAdmin.E2ETests/         # End-to-end browser tests (Playwright)
│   ├── Tests/                            # Test files
│   ├── PageObjects/                      # Page Object Model
│   ├── Fixtures/                         # Test fixtures
│   └── Infrastructure/                   # Test setup (containers, factories)
│
├── TelegramGroupsAdmin.IntegrationTests/ # Integration tests (real database, no UI)
│   ├── Migrations/                       # Migration tests (vs. real PostgreSQL 18)
│   ├── Repositories/                     # Repository tests
│   ├── Services/                         # Service integration tests
│   ├── ContentDetection/                 # Detection algorithm tests
│   ├── Jobs/                             # Background job tests
│   ├── Fixtures/                         # Test data fixtures
│   └── TestHelpers/                      # Test utilities
│
├── TelegramGroupsAdmin.ComponentTests/   # Blazor component tests
│   ├── Components/                       # Component test files
│   └── Services/                         # Mock services for tests
│
├── TelegramGroupsAdmin.UnitTests/        # Unit tests (fast, isolated)
│   ├── BackgroundJobs/
│   ├── Configuration/
│   ├── ContentDetection/
│   ├── Core/
│   ├── Services/
│   ├── Telegram/
│   └── Utilities/
│
├── docs/                                 # Markdown documentation
│   ├── getting-started/
│   ├── features/
│   ├── admin/
│   ├── user/
│   └── 03-algorithms/
│
├── compose/                              # Docker Compose local development
│   ├── docker-compose.yml
│   ├── data-test/                        # Test data volume
│   └── db-test/                          # Test database volume
│
├── tessdata/                             # DEAD - Tesseract OCR data, no longer used (cleanup needed)
│
├── .planning/                            # GSD planning documents
│   └── codebase/                         # Architecture & structure docs
│
├── .github/                              # GitHub config
│   ├── workflows/                        # CI/CD pipelines
│   └── ISSUE_TEMPLATE/
│
├── .devcontainer/                        # Dev container config
├── .claude/                              # AI assistant rules
├── .vscode/                              # VS Code settings
├── Directory.Packages.props               # Central NuGet version management
└── TelegramGroupsAdmin.sln               # Solution file
```

## Directory Purposes

**TelegramGroupsAdmin** (Main Web Application):
- Purpose: Blazor Server UI, API endpoints, service orchestration
- Contains: Pages (routable components), endpoints (minimal APIs), services, repositories, auth
- Key Files: `Program.cs` (startup), `ServiceCollectionExtensions.cs` (DI), `WebApplicationExtensions.cs` (pipeline)

**TelegramGroupsAdmin.Data** (Data Persistence):
- Purpose: EF Core database context, migrations, entity models
- Contains: `AppDbContext` with 80+ `*Dto` entities, migrations (auto-generated)
- Key Files: `AppDbContext.cs` (entity configuration), `Migrations/` (EF Core migration history)
- Rule: Zero project dependencies - only NuGet packages

**TelegramGroupsAdmin.Core** (Shared Domain):
- Purpose: Types shared across projects (breaks circular dependencies)
- Contains: `Actor`, identity types, audit models, job payloads, repository interfaces
- Key Files: `Models/Actor.cs`, `Models/UserIdentity.cs`, `JobPayloads/` (Quartz.NET payloads)

**TelegramGroupsAdmin.Telegram** (Bot Domain Logic):
- Purpose: Telegram bot functionality, message handling, moderation
- Contains: Handlers (message processing), services (bot operations), repositories, commands
- Key Files: `Handlers/ContentDetectionOrchestrator.cs`, `Services/Bot/TelegramBotPollingHost.cs`, `Services/Moderation/ModerationIntents.cs`

**TelegramGroupsAdmin.ContentDetection** (Detection Algorithms):
- Purpose: 9 independent content detection algorithms
- Contains: Checks (algorithm implementations), services (external APIs), ML models
- Key Files: `Checks/SpamCheckV2.cs`, `Services/ClamAVScannerService.cs`, `ML/IMLTextClassifierService.cs`

**TelegramGroupsAdmin.Configuration** (Config Management):
- Purpose: IOptions<T> classes for all application settings
- Contains: Config POCOs, config repositories, DI registration
- Key Files: `Models/AppOptions.cs`, `Extensions/ServiceCollectionExtensions.cs`

**TelegramGroupsAdmin.BackgroundJobs** (Scheduled Tasks):
- Purpose: Quartz.NET scheduled jobs and job orchestration
- Contains: Job implementations (18+ total), backup/restore services, job listeners
- Key Files: `Jobs/TextClassifierRetrainingJob.cs`, `Services/Backup/IBackupService.cs`

## Key File Locations

**Entry Points:**
- `TelegramGroupsAdmin/Program.cs`: Main startup, DI, middleware pipeline
- `TelegramGroupsAdmin.Telegram/Services/Bot/TelegramBotPollingHost.cs`: Bot polling (IHostedService)
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/*.cs`: Quartz.NET scheduled jobs

**Configuration:**
- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`: Service registration (Blazor, auth, services)
- `TelegramGroupsAdmin/WebApplicationExtensions.cs`: Middleware pipeline, endpoint mapping
- `TelegramGroupsAdmin.Configuration/Extensions/ServiceCollectionExtensions.cs`: App options registration
- `Directory.Packages.props`: Central NuGet version management

**Core Logic:**
- `TelegramGroupsAdmin.Telegram/Handlers/ContentDetectionOrchestrator.cs`: Content detection orchestration
- `TelegramGroupsAdmin.Telegram/Services/Moderation/ModerationIntents.cs`: 14 moderation intent types
- `TelegramGroupsAdmin.ContentDetection/Checks/*.cs`: Algorithm implementations (9 total)

**Testing:**
- `TelegramGroupsAdmin.IntegrationTests/Migrations/`: Migration validation (real PostgreSQL 18)
- `TelegramGroupsAdmin.E2ETests/`: Browser-based UI tests (Playwright)
- `TelegramGroupsAdmin.UnitTests/`: Fast unit tests

## Naming Conventions

**Files:**
- PascalCase: `MessageRecordDto.cs`, `TranslationHandler.cs`, `IContentCheckV2.cs`
- Suffixes: `*Dto` (Data layer), `*Service` (business logic), `*Repository` (data access), `*Handler` (message handlers), `*Intent` (moderation)
- Extension classes: `ServiceCollectionExtensions`, `TelegramLoggingExtensions`, `IdentityExtensions`

**Directories:**
- PascalCase: `Components/`, `Handlers/`, `Services/`, `Repositories/`
- Lowercase context: `lang-models/` (language models)
- Suffixes for grouping: `Services/Bot/`, `Services/Moderation/`, `Services/BackgroundServices/`

**Types:**
- Dto suffix required: `MessageRecordDto`, `TelegramUserDto`, `ReportDto` (Data layer only)
- Service interfaces: `IAuthService`, `IMessageRepository`, `IBotModerationService`
- Records for data: `Actor`, `UserIdentity`, `ChatIdentity` (immutable, value semantics)
- Enums: `ActorType`, `AuditEventType`, `PermissionLevel`
- Namespaces: Hierarchical matching folder structure (e.g., `TelegramGroupsAdmin.Telegram.Services.Moderation`)

## Where to Add New Code

**New Feature:**
- Primary code: `TelegramGroupsAdmin.Telegram/Services/` if bot-related, `TelegramGroupsAdmin.ContentDetection/Checks/` if detection
- Tests: `TelegramGroupsAdmin.IntegrationTests/Services/` (integration) or `TelegramGroupsAdmin.UnitTests/` (unit)
- Models: `TelegramGroupsAdmin.Core/Models/` if shared, `TelegramGroupsAdmin.Data/Models/` if persistence-specific
- API Endpoint: `TelegramGroupsAdmin/Endpoints/FeatureEndpoints.cs` (create new file, map in `WebApplicationExtensions`)

**New Component/Module:**
- Blazor page: `TelegramGroupsAdmin/Components/Pages/FeatureName.razor`
- Shared component: `TelegramGroupsAdmin/Components/Shared/FeatureName.razor` (or subdir like `Components/Shared/Settings/`)
- Service: `TelegramGroupsAdmin/Services/IFeatureService.cs` + implementation
- Repository: `TelegramGroupsAdmin/Repositories/IFeatureRepository.cs` + mapping in `Repositories/Mappings/`

**Utilities:**
- Telegram utilities: `TelegramGroupsAdmin.Telegram/Helpers/UtilityName.cs` or `TelegramGroupsAdmin.Telegram/Extensions/`
- Core utilities: `TelegramGroupsAdmin.Core/Utilities/UtilityName.cs`
- Shared extensions: `TelegramGroupsAdmin.Telegram/Extensions/IdentityExtensions.cs` (C# 14 `extension(Type)` syntax)

**Background Jobs:**
- Job: `TelegramGroupsAdmin.BackgroundJobs/Jobs/MyNewJob.cs` (implement `IJob`, use `[DisallowConcurrentExecution]` if needed)
- Payload: `TelegramGroupsAdmin.Core/JobPayloads/MyNewJobPayload.cs` (keeps Core dependency clean)
- Service: `TelegramGroupsAdmin.BackgroundJobs/Services/MyJobService.cs` if job needs helper logic

## Special Directories

**TelegramGroupsAdmin/Data/:**
- Purpose: Test/backup data storage
- Generated: No (user-managed)
- Committed: No (ignored in .gitignore)

**TelegramGroupsAdmin/lang-models/:**
- Purpose: ML.NET model files (trained spam classifier)
- Generated: Yes (trained on startup via `IMLTextClassifierService.TrainModelAsync()`)
- Committed: No (generated fresh on each run in development)

**compose/:**
- Purpose: Docker Compose setup for local development
- Generated: Volumes `compose/db-test/`, `compose/data-test/` are mounted at runtime
- Committed: Yes (compose file), No (database/data volumes)

**tessdata/:**
- Purpose: DEAD — was Tesseract OCR language data, no longer used (OCR now via OpenAI Vision)
- Cleanup needed: Remove directory from repo AND from Dockerfile (downloads eng.traineddata at build time, ~14.7MB dead weight in image)
- Generated: No
- Committed: Yes (should be removed)

**Migrations (TelegramGroupsAdmin.Data/Migrations/):**
- Purpose: EF Core migration history
- Generated: Yes (`dotnet ef migrations add MigrationName`)
- Committed: Yes (part of source control)
- Notes: Prefer code-first via Fluent API wherever possible; manual edits sometimes unavoidable for PostgreSQL features EF Core doesn't support. Revert with `dotnet ef migrations remove` and regenerate if wrong

---

*Structure analysis: 2026-03-15*
