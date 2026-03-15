# Architecture

**Analysis Date:** 2026-03-15

## Pattern Overview

**Overall:** Layered monolith with clear separation of concerns via project structure. Specialized content detection library and background job orchestration layer. Single-instance deployment enforced by Telegram Bot API constraints.

**Key Characteristics:**
- **Clean Layering**: Data → Core/Domain → Telegram/ContentDetection → Main → UI (Blazor)
- **Domain-Driven Models**: Identity types (`UserIdentity`, `ChatIdentity`), Actor polymorphism, ModerationIntent hierarchy
- **Handler Chain Pattern**: Message processing flows through sequential handlers (Translation → Detection → Media → Filing)
- **Event Sourcing Patterns**: Audit logs, detection results, and training data immutable and queryable
- **Singleton Constraint**: Telegram API enforces single bot connection; all background jobs, web UI, and polling run in one process

## Layers

**Data Layer (`TelegramGroupsAdmin.Data`):**
- Purpose: EF Core persistence, database schema, migrations
- Location: `TelegramGroupsAdmin.Data/`
- Contains: `AppDbContext`, all `*Dto` entity models, migrations, data protection key storage
- Depends on: NuGet packages only (EF Core, Npgsql)
- Used by: All other projects (foundation layer)
- Key Rule: **No project references** - must stay isolated as the foundation

**Domain/Core Layer (`TelegramGroupsAdmin.Core`):**
- Purpose: Shared models, enums, interfaces, audit, notifications
- Location: `TelegramGroupsAdmin.Core/`
- Contains: `Actor`, `UserIdentity`, `ChatIdentity`, `ModerationIntent` hierarchy, job payloads, audit models
- Depends on: Data project only (for DTOs via mapping extensions)
- Used by: Telegram, ContentDetection, BackgroundJobs, Configuration projects
- Rationale: Breaks circular dependencies (Core can't depend on Telegram/ContentDetection, but they depend on Core)

**Telegram Domain Layer (`TelegramGroupsAdmin.Telegram`):**
- Purpose: Telegram-specific business logic, bot command handling, message orchestration
- Location: `TelegramGroupsAdmin.Telegram/`
- Contains: Bot services, message handlers, repositories, moderation intents, DM notification system
- Depends on: Core, Data, ContentDetection.Abstractions
- Used by: Main project, BackgroundJobs
- Key Services: `ContentCheckCoordinator` (orchestrates detection), handlers chain, moderation services

**Content Detection Layer (`TelegramGroupsAdmin.ContentDetection`):**
- Purpose: 9 independent content detection algorithms (ML, Bayes, OCR, ClamAV, VirusTotal, URL filtering, impersonation, etc.)
- Location: `TelegramGroupsAdmin.ContentDetection/`
- Contains: Checks (algorithm implementations), Services (ClamAV, VirusTotal, blocklists), ML (text/image classifiers)
- Depends on: Data, Core
- Used by: Telegram layer (via ContentCheckCoordinator)
- Design: Self-contained, testable in isolation, database-driven configurations

**Configuration Layer (`TelegramGroupsAdmin.Configuration`):**
- Purpose: IOptions<T> classes for all settings, AddApplicationConfiguration() extension
- Location: `TelegramGroupsAdmin.Configuration/`
- Contains: `AppOptions`, `TelegramOptions`, `OpenAIOptions`, `SendGridOptions`, Quartz.NET configs
- Depends on: Core
- Used by: Main project's service configuration

**Background Jobs Layer (`TelegramGroupsAdmin.BackgroundJobs`):**
- Purpose: Quartz.NET scheduled jobs, job payloads, job listeners
- Location: `TelegramGroupsAdmin.BackgroundJobs/`
- Contains: IJob implementations (retraining, backups, cleanups, health checks), payload helpers, services
- Depends on: Core, Data, Telegram, ContentDetection
- Used by: Main project's dependency injection
- Design: Jobs run in main process (singleton constraint), payloads in Core to avoid circular deps

**UI/Presentation Layer (`TelegramGroupsAdmin`):**
- Purpose: Blazor Server UI, API endpoints, service configuration orchestration
- Location: `TelegramGroupsAdmin/`
- Contains: Razor components, endpoints, authentication/authorization, repositories, DI wiring
- Depends on: All other projects (orchestrates them)
- Used by: External clients (browsers, API consumers)
- Key Extensions: `ServiceCollectionExtensions` (DI), `WebApplicationExtensions` (pipeline, endpoints)

## Data Flow

**Bot Message Reception Flow:**

1. `TelegramBotPollingHost` (background service) receives updates via Telegram Bot API polling
2. Message routed to `MessageProcessingService` in `TelegramGroupsAdmin.Telegram`
3. `ContentCheckCoordinator.RunContentCheckAsync()` orchestrates detection:
   - **TranslationHandler**: Translates non-Latin text (≥10 chars, <80% Latin) for analysis
   - **ContentDetectionOrchestrator**: Runs 9 checks in sequence (spam, phishing, OCR, etc.)
   - **MediaProcessingHandler**: Processes images (AI detection) and video frames
   - **FileScanningHandler**: Schedules file scan job (async, returns immediately)
4. Results saved to `DetectionResultRecordDto`, training labels tracked
5. User actions (bans, kicks, mutes) trigger moderation flows via `ModerationIntent` dispatch
6. Audit logged via `IAuditService.LogAsync()` with `Actor` context

**UI Page Rendering Flow:**

1. User navigates to e.g., `/messages` → routes to `Components/Pages/Messages.razor`
2. Component loads data via injected repositories (e.g., `IMessageRepository.GetMessagesAsync()`)
3. Repositories fetch from database via `AppDbContext`, map `*Dto` → domain models
4. MudBlazor components render with data, bind to handlers
5. Button clicks or form submissions call API endpoints (e.g., `/api/auth/login`)
6. Endpoints invoke service layer (`IAuthService`, `IUserManagementService`, etc.)
7. Services modify database state, trigger background tasks if needed
8. Response returned to browser

**Background Job Execution Flow:**

1. `QuartzSchedulingSyncService` ensures default job configs exist at startup
2. Quartz scheduler triggers job at configured time (e.g., "0 */6 * * * ?" = every 6 hours)
3. Job runs in main process, accesses database via `IServiceScopeFactory`
4. Job accesses payload via `context.MergedJobDataMap` (for on-demand jobs), or reads config
5. Job performs work (e.g., retraining ML model, scanning files, cleaning old data)
6. Job re-throws exceptions for retry logic
7. Results persisted to database or external systems (S3 backups, etc.)

**Moderation Flow:**

1. Decision to moderate triggered by: user action, admin command, auto-detection
2. `ModerationIntent` created (one of 14 derived types: `BanIntent`, `MuteIntent`, `CasCheckIntent`, etc.)
3. Intent contains full context: `UserIdentity User`, `Actor Executor`, `string Reason`
4. `IBotModerationService.ExecuteAsync(intent)` dispatches to appropriate handler
5. Handler performs action (ban via API, DM notification, audit log)
6. Ban celebration triggered if configured
7. Audit logged with full actor/reason context

**State Management:**

- **In-Memory Caching**: `HybridCache` (registered in `AddHttpClients()`) provides L1 in-memory caching for frequently accessed data
- **Database-First Configuration**: All settings stored in `ConfigRecordDto` table, editable via Settings UI
- **Telegram User Cache**: `TelegramUserDto` with embedded `Warnings` JSONB array, refreshed periodically
- **Detection State**: Immutable detection results stored, training labels derived from them
- **Session State**: Exam sessions in `ExamSessionDto`, welcome responses in `WelcomeResponseDto`

## Key Abstractions

**Identity Types:**
- Purpose: Type-safe representation of Telegram users and chats with cached display names
- Examples: `UserIdentity.FromId(123)`, `UserIdentity.FromAsync(repo)`, `ChatIdentity.From(tgChat)`
- Pattern: Sealed records, init-only properties, static factories with `From*` methods
- Location: `TelegramGroupsAdmin.Core.Models/`, extensions in `TelegramGroupsAdmin.Telegram.Extensions/`

**Actor Polymorphism:**
- Purpose: Represent who performed an action (Web User, Telegram User, System)
- Examples: `Actor.FromWebUser(userId)`, `Actor.FromTelegramUser(tgId)`, `Actor.AutoDetection`
- Pattern: Record with discriminated union via `ActorType` enum, factory methods, display helpers
- Location: `TelegramGroupsAdmin.Core.Models.Actor`
- Stores as: Exclusive arc in database (one of web_user_id, telegram_user_id, system_identifier non-null)

**ModerationIntent Hierarchy:**
- Purpose: Polymorphic moderation actions with full context
- Examples: `BanIntent`, `MuteIntent`, `KickIntent`, `CasCheckIntent`, `LanguageWarningIntent`
- Pattern: Abstract base with `User`, `Executor`, `Reason` properties, 14 derived types
- Location: `TelegramGroupsAdmin.Telegram.Services.Moderation/ModerationIntents.cs`
- Usage: `IBotModerationService.ExecuteAsync(ModerationIntent intent)`

**Content Check Interface:**
- Purpose: Standardized interface for all detection algorithms
- Examples: `ISpamCheckV2`, `IPhishingCheck`, `IOcrCheck`, `IVedisCheckV2`
- Pattern: Sync/async methods returning `ContentCheckResult` with score, details, and reason
- Location: `TelegramGroupsAdmin.ContentDetection.Abstractions/`
- Runner: `ContentDetectionOrchestrator.RunAllChecksAsync()` executes all in sequence

**Handler Chain Pattern:**
- Purpose: Sequential processing of messages with isolated concerns
- Examples: `TranslationHandler`, `MediaProcessingHandler`, `FileScanningHandler`
- Pattern: Each handler accepts message context, performs work, returns typed result
- Location: `TelegramGroupsAdmin.Telegram.Handlers/`
- Orchestrator: `ContentDetectionOrchestrator.RunAllChecksAsync()` chains them

**Repository Mapping Pattern:**
- Purpose: Convert between Data layer DTOs and Domain models
- Examples: `report.ToDto()`, `reportDto.ToModel()`
- Pattern: Extension methods in `Repositories/Mappings/` subdirectory
- Location: Each project's `Repositories/Mappings/` folder (26 files total)
- Rule: Repositories never expose `*Dto` types in public interfaces

**Partial Unique Index Pattern:**
- Purpose: State-dependent database constraints
- Examples: Only one pending report per message (status=0), only one singleton config (chat_id=0)
- Pattern: `HasIndex().HasFilter("WHERE status = 0").IsUnique()`
- Location: `TelegramGroupsAdmin.Data.AppDbContext.OnModelCreating()`
- Benefit: Allows multiple non-pending reports per message, multiple configs for different chats

**Exclusive Arc Pattern:**
- Purpose: Polymorphic relationships without nullable foreign keys
- Examples: `MessageTranslationDto` (message_id XOR edit_id), `AuditLogRecordDto` (actor can be user OR tg_user OR system)
- Pattern: Multiple nullable FK columns with database check constraint
- Location: `TelegramGroupsAdmin.Data.AppDbContext.OnModelCreating()`
- Benefit: Type safety at domain layer via inheritance, referential integrity at database layer

## Entry Points

**Web Request Entry (`TelegramGroupsAdmin/Program.cs:320`):**
- Triggers: HTTP requests to `/`, `/chats`, `/settings`, `/api/*`, etc.
- Responsibilities:
  - Configure middleware pipeline (auth, static files, error handling)
  - Map Blazor components and API endpoints
  - Serve UI to browsers

**Bot Polling Entry (`TelegramBotPollingHost`):**
- Triggers: Telegram Bot API updates (messages, edits, callbacks)
- Responsibilities:
  - Poll Telegram API for updates
  - Route updates to appropriate handlers
  - Dispatch to `MessageProcessingService`, callback handlers, etc.

**Background Job Entry (`BackgroundJobs/Jobs/*.cs`):**
- Triggers: Quartz.NET scheduler at configured time or on-demand via `TriggerNowAsync()`
- Responsibilities:
  - Execute long-running tasks (ML retraining, file scanning, cleanup)
  - Access database via scoped context
  - Log results and errors
  - Re-throw exceptions for retry mechanism

**Command Line Entry (`Program.cs:185-273`):**
- Triggers: `--migrate-only`, `--backup`, `--restore` flags
- Responsibilities:
  - Run migrations and exit
  - Create/restore encrypted backups
  - Perform data export/import

## Error Handling

**Strategy:** Result-based errors with logging, exception re-throwing for background jobs

**Patterns:**

- **Repositories**: Return `null` for not-found, throw `InvalidOperationException` for constraint violations
- **Services**: Return typed results (e.g., `AuthResult` with `Success` bool, `ErrorMessage`), log and re-throw on critical errors
- **Handlers**: Catch known exceptions, log with context, continue processing or fail gracefully
- **Background Jobs**: Re-throw exceptions immediately (Quartz catches for retry logic), don't swallow errors
- **API Endpoints**: Catch exceptions, return 500 with generic error message, log full details with context
- **UI Components**: Wrap in try-catch, display user-friendly error messages via MudBlazor snackbars
- **Validation**: Use `FluentValidation` or custom guards at service entry points, reject invalid state early

**Logging Context:**

- Add context via `LogContext.PushProperty()` in handlers and services
- Log identity info (via `.ToLogInfo()` or `.ToLogDebug()`) in audit-relevant operations
- Suppress sensitive data (passwords, tokens) at all log levels
- Use structured logging with named properties for querying in Seq

## Cross-Cutting Concerns

**Logging:**
- Framework: Serilog with dynamic log level switching (loaded from database)
- Sinks: Console (always), Seq (optional via `SEQ_URL` env var), OpenTelemetry (optional via `OTEL_EXPORTER_OTLP_ENDPOINT`)
- Structured Logging: Serilog.Extensions.Logging enriches with LogContext properties
- Configuration File: `appsettings.json` minimal, real config via database

**Validation:**
- At service entry points (controllers, endpoints, handlers)
- Custom guards: `if (param == null) throw new ArgumentNullException(...)`
- FluentValidation for complex objects (auth requests, settings updates)
- Database-level: Check constraints for exclusive arcs, unique indexes for state-dependent constraints

**Authentication:**
- Scheme: Cookie-based authentication (HttpOnly, SameSite=Lax)
- Cookie Name: `TgSpam.Auth`
- Expiry: 30 days with sliding expiration
- MFA: TOTP-based (via `ITotpService`), recovery codes backup
- Intermediate Token: 5-minute token for password verification → TOTP flow

**Authorization:**
- Policies: `"GlobalAdminOrOwner"` (role-based), `"OwnerOnly"`
- Levels: 0=Admin (chat-scoped), 1=GlobalAdmin (all chats), 2=Owner (full access)
- Implementation: Claims in cookie + role check in handlers
- Chat Access: Admin sees only chats they're Telegram admin in (uses `chat_admins` table)

**Rate Limiting:**
- Framework: Built-in via `SlidingWindowRateLimiter` in `RateLimitService`
- Applied to: `/api/auth/login`, `/api/auth/register`, `/api/auth/verify-totp` (SECURITY-5)
- Per-user tracking: Identity (email for login, userId for TOTP)
- Windows: Configurable per endpoint (e.g., 5 attempts per 15 minutes)

**Transaction Management:**
- Default: EF Core's implicit transactions via `SaveChangesAsync()`
- Explicit: `using var transaction = await context.Database.BeginTransactionAsync()` for multi-step operations
- Isolation Level: Read Committed (PostgreSQL default)

**Caching:**
- L1 In-Memory: `HybridCache` registered in `AddHttpClients()` (optional, can be disabled)
- L2 None: No Redis or distributed cache (single instance, in-memory sufficient)
- Invalidation: Handled per-service (e.g., `BanCelebrationCache.Invalidate()` when settings change)
- Database Config Cache: Settings fetched once at startup, refreshed via admin UI (requires restart)

---

*Architecture analysis: 2026-03-15*
