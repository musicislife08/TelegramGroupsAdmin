# CLAUDE.md - TelegramGroupsAdmin

ASP.NET Core 10.0 Blazor Server + Minimal API. Telegram spam detection (text + image). PostgreSQL database.

## Tech Stack
.NET 10.0 (preview), Blazor Server (MudBlazor v8.13.0), PostgreSQL 17 + Npgsql, Dapper + FluentMigrator, Cookie auth + TOTP 2FA, VirusTotal API, OpenAI Vision API, SendGrid email, TickerQ 2.5.3 (background jobs)

## Solution Structure

### Projects
- **TelegramGroupsAdmin** - Main app (Blazor + API), extension method architecture, TickerQ jobs (3 jobs in `/Jobs`)
- **TelegramGroupsAdmin.Configuration** - Config option classes, `AddApplicationConfiguration()` extension
- **TelegramGroupsAdmin.Data** - EF Core DbContext, migrations, Data Protection, internal to repositories
- **TelegramGroupsAdmin.Telegram** - Bot services/workers, 9 commands, repos, orchestrators, `AddTelegramServices()`
- **TelegramGroupsAdmin.Telegram.Abstractions** - Shared abstractions (TelegramBotClientFactory, job payloads), breaks circular dependencies
- **TelegramGroupsAdmin.SpamDetection** - 9 spam algorithms, self-contained, database-driven

### Extension Methods
**ServiceCollectionExtensions.cs**: `AddBlazorServices()`, `AddCookieAuthentication()`, `AddApplicationServices()`, `AddHttpClients()`, `AddTelegramServices()`, `AddRepositories()`, `AddTgSpamWebDataServices()`, `AddTickerQBackgroundJobs()`
**WebApplicationExtensions.cs**: `ConfigurePipeline()`, `MapApiEndpoints()`, `RunDatabaseMigrationsAsync()`
**ConfigurationExtensions.cs**: `AddApplicationConfiguration()` (binds IOptions from env vars)

## Architecture

### Spam Detection Library
**Core**: `ISpamDetectorFactory` (orchestration + confidence aggregation), 9 specialized checks, OpenAI veto, continuous learning
**Algorithms**: StopWords, CAS, Similarity (TF-IDF), Bayes (self-learning), MultiLanguage (translation), Spacing (invisible chars), OpenAI (GPT veto), ThreatIntel (VirusTotal + Safe Browsing), Image (Vision)
**Features**: Self-improving, database-driven, performance optimized (caching, early exit), fail-open design, multi-chat support, Telegram API aligned

### Services
**Spam**: ISpamDetectorFactory, ITokenizerService, IOpenAITranslationService, IMessageHistoryService, IStopWordsRepository, ISpamSamplesRepository, ITrainingSamplesRepository, ISpamCheck (9 implementations)
**Core**: IThreatIntelService, IVisionSpamDetectionService, ITelegramImageService, IAuthService, IIntermediateAuthService, IInviteService, IUserManagementService, IMessageExportService, IEmailService, IReportActionsService, AdminMentionHandler

### Layered Architecture
**3-tier with UI/Data separation**: UI Models (Blazor DTOs) → Repositories (conversion layer) → Data Models (DB internal)
**Benefits**: Database independence, type safety, single responsibility
**Conversion**: ModelMappings.cs with `.ToUiModel()` / `.ToDataModel()` extensions, repos return/accept UI models only
**Organization**: TelegramGroupsAdmin/Models/ (UI), TelegramGroupsAdmin/Repositories/ (access), TelegramGroupsAdmin.Data/Models/ (DB)

## Database Schema (PostgreSQL)

**Single DB**: `telegram_groups_admin`, 18 tables
**Initial**: `202601100_InitialSchema.cs` (validated)
**Latest**: `AddWelcomeResponsesTable` (Phase 4.4)

### Core Tables
**messages**: message_id (PK), chat_id, user_id, user_name, chat_name, timestamp, edit_date, message_text, photo fields, urls, content_hash. Retention: 180d default (except spam/ham refs)
**detection_results**: id (PK), message_id (FK cascade), detected_at, detection_source (auto/manual), is_spam, confidence, reason, detection_method, added_by. Purpose: history, training data (10k recent + all manual), false positives. Retention: permanent
**message_edits**: id (PK), message_id (FK cascade), edit_date, previous_text, previous_content_hash. Purpose: spam tactic tracking
**user_actions**: id (PK), user_id, action_type (ban/warn/mute/trust/unban), message_id, issued_by, issued_at, expires_at, reason. Global across all chats. Retention: permanent
**welcome_responses**: id (PK), chat_id, user_id, username, welcome_message_id, response (accepted/denied/timeout/left), responded_at, dm_sent, dm_fallback, created_at. Analytics for acceptance rates, DM success

### Configuration Tables
**stop_words**: id (PK), word, word_type (0=message/1=username/2=userID), added_date, source, enabled, added_by, detection_count, last_detected_date
**spam_detection_configs**: chat_id (PK), min_confidence_threshold, enabled_checks, custom_prompt, auto_ban_threshold, created_at, updated_at
**spam_check_configs**: check_name (PK), enabled, confidence_weight, config_json, updated_at

### Identity Tables
**users**: id (GUID PK), email/normalized_email, password_hash, security_stamp, permission_level (0=ReadOnly/1=Admin/2=Owner), invited_by, is_active, totp fields, timestamps, status (0=Pending/1=Active/2=Disabled/3=Deleted), modified_by, email verification fields, password reset fields
**invites**: token (PK), created_by, created_at, expires_at, used_by, permission_level, status (0=Pending/1=Used/2=Revoked), modified_at
**audit_log**: id (PK), event_type (enum), timestamp, actor_user_id, target_user_id, value
**verification_tokens**: id (PK), user_id, token_type (email_verify/password_reset/email_change), token, value, expires_at, created_at, used_at

### Design Principles
Normalized storage, cascade deletes (edits cascade, detections/actions remain), configurable retention, global actions, complete audit trail

### Background Services
**Composition Pattern** (separated by responsibility):
1. **TelegramAdminBotService** (208 lines) - Bot polling, update routing (5 types: Message, EditedMessage, MyChatMember, ChatMember, CallbackQuery), command registration, implements IMessageHistoryService
2. **MessageProcessingService** (509 lines) - New message processing/storage, edit monitoring with re-scanning, image download/thumbnails, spam orchestration, URL extraction
3. **ChatManagementService** (359 lines) - MyChatMember handling, admin cache refresh, health checking, chat name updates
4. **SpamActionService** (230 lines) - Training quality control, auto-ban execution (cross-chat), borderline reports, confidence-based decisions
5. **CleanupBackgroundService** (80 lines) - Message retention cleanup (keeps spam/ham samples)

## Configuration (Env Vars)
**Required**: VIRUSTOTAL__APIKEY, OPENAI__APIKEY, TELEGRAM__BOTTOKEN, TELEGRAM__CHATID, SPAMDETECTION__APIKEY, SENDGRID__APIKEY/FROMEMAIL/FROMNAME
**Optional**: APP__BASEURL, OPENAI__MODEL/MAXTOKENS, MESSAGEHISTORY__*, SPAMDETECTION__*, IDENTITY__DATABASEPATH, DATAPROTECTION__KEYSPATH

## Logging
Microsoft: Warning, Microsoft.Hosting.Lifetime: Information, TelegramGroupsAdmin: Information
**Levels**: Error (exceptions), Warning (user errors, rate limits), Information (ops events)
**Rate Limits**: VirusTotal (4 req/min, LogWarning), OpenAI (HTTP 429, retry-after), both fail open

## Key Implementations
**Rate Limiting**: VirusTotal (Polly PartitionedRateLimiter, 4/min sliding), OpenAI (429 detection)
**Edit Spam Detection**: On edit → save to message_edits, update messages, re-run detection, action if spam
**Email Verification**: 24h token (32 random bytes, base64), login blocked until verified (except first Owner)
**TOTP Security**: IntermediateAuthService (5min tokens after password), prevents direct access, 15min expiry for abandoned setups
**2FA Reset**: Owners can reset any user's TOTP, clears secrets, audit logged

## API Endpoints
GET /health - Status + bot stats
POST /api/auth/login - Returns {requiresTotp, userId, intermediateToken}
POST /api/auth/register - Auto-login
POST /api/auth/logout
POST /api/auth/verify-totp - Requires intermediateToken
GET /verify-email?token=X
POST /resend-verification, /forgot-password, /reset-password

## Blazor Pages
**Public**: /login, /login/verify, /login/setup-2fa, /register
**Authenticated**: / (Home dashboard), /analytics (#spam/#trends/#performance - Admin+), /messages (viewer, export), /spam (#stopwords/#training - Admin+), /users (management - Admin+), /reports (queue - Admin+), /audit (Admin+), /settings (#spam/#general/#telegram/#notifications/#security/#integrations - Admin+), /profile
**Features**: URL fragment navigation, logical menu grouping, top bar with user email, component reuse

## Permissions & Invite System
**Levels**: 0=ReadOnly, 1=Admin, 2=Owner (hierarchy: Owner > Admin > ReadOnly, cannot escalate above own level)
**User Status**: 0=Pending, 1=Active, 2=Disabled, 3=Deleted (soft delete)
**Invites**: 0=Pending/1=Used/2=Revoked, first user auto-Owner, 7-day expiry, permission inheritance, audit trail

## Build Quality
**Perfect Build (January 2025)**: 0 errors, 0 warnings
158+ errors → 0, 62+ warnings → 0, MudBlazor v8.13.0, modern patterns, triple-verified
**Modernizations**: MudBlazor API updates, records → classes for binding, async patterns, null safety, type safety, Telegram API alignment (group → chat), enum cleanup (13 duplicates removed), UI/UX improvements

## Troubleshooting
**Bot not caching**: Check TELEGRAM__BOTTOKEN, bot in chat, privacy mode off
**Image spam failing**: Check OPENAI__APIKEY, /data mounted
**DB growing**: Check retention (720h default), cleanup running
**Rate limits**: Check logs for VirusTotalService or OpenAIVisionSpamDetectionService warnings
**Build issues**: `dotnet clean && dotnet build` (0 errors/warnings standard)
**TickerQ attributes**: `using TickerQ.Utilities.Base;` (TickerFunctionAttribute), `using TickerQ.Utilities.Models;` (TickerFunctionContext<T>), clone GitHub repo if docs unclear

## Development Roadmap

### Phase 1: Foundation ✅ COMPLETE
Blazor Server UI, cookie auth + TOTP 2FA, user management + invite system, audit logging, message history + export, email verification, image spam detection (OpenAI Vision), text spam detection

### Phase 2: Unified Telegram Bot ✅ COMPLETE
**2.1**: 9 spam algorithms, SpamDetectorFactory, normalized schema, self-improving system, shared services, production-ready
**2.2**: FluentMigrator normalization, removed training_samples/spam_checks tables, detection_results/user_actions created, data migrated, repos/UI updated
**2.3**: Training data imported (191 spam + 26 ham, 11 stop words), Latin script detection, model separation, OpenAI veto optimization, performance (<100ms cached, ~4s first URL), 30d retention
**2.4**: Bot command routing, 9 commands (/help /report /spam /ban /trust /unban /warn /delete /link), permissions (web→telegram admin→none), /reports UI, @admin mentions, message history + edit tracking
**2.5**: Backup/restore (gzip JSON, 81% compression), reflection-based, all 18 tables, Data Protection for TOTP, CLI flags (--export/--import), transaction safety, FK-aware deletion
**2.6**: Weighted voting (net confidence), asymmetric confidence (simple 20% ham, trained full), two-tier decision (net >50 OR max >85 → OpenAI veto, net 0-50 → reports, <0 → allow), training quality control (OpenAI 85%+ or net >80), Messages UI (mark spam/ham, detection history)
**2.7**: Auto-reports (net +0 to +50), auto-ban (net >50 + OpenAI 85%+), unban logic, command actions with Telegram API, cross-chat enforcement, edit re-scanning, refactoring (1,243 lines → 4 services)
**2.8**: Chat icons (TelegramPhotoService, 64x64 cached), Messages UI improvements (chat prominent, inline filters, quick filters), service message filtering, delete button, architecture cleanup (ModerationActionService DI)

### Phase 3: Advanced Multi-Chat ✅ COMPLETE
Cross-chat spam detection (global bans), shared/global blacklist (user_actions, stop_words), global moderation auto-apply
Not implemented (not needed): Chat delegation, templates, bulk UI (already automatic)

### Phase 4: Infrastructure & Configuration ⏳ IN PROGRESS
**4.1** ✅: TickerQ PostgreSQL job queue installed, ready for implementations
**4.2** ✅: Unified configs table (JSONB, chat_id NULL=global), IConfigService (Save/Get/GetEffective/Delete), auto-merging, migrated spam_detection_configs, dependency chain fixed, DI lifetime fixed
**4.2.1** ✅: 36 timestamps bigint → timestamptz, all DTOs → DateTimeOffset, repos/services/Blazor updated, TickerQ entity configs, 228 messages preserved
**4.3** ✅: TelegramGroupsAdmin.Telegram library, moved 6 models + 13 repos + all services + 9 commands + 4 background services, AddTelegramServices() extension, ModelMappings public, clean dependency flow, 100+ using updates
**4.4** ✅: Welcome system complete - DB schema, WelcomeService, Repository, bot integration, config from DB, chat name caching, TickerQ timeout job (WelcomeTimeoutJob in `/Jobs`), TickerQ delete message job (DeleteMessageJob in `/Jobs`), callback validation, user leaves handling, /settings#telegram UI. **C1 CRITICAL RESOLVED**: All fire-and-forget Task.Run replaced with persistent TickerQ jobs (retry logic, logging, survives restarts, proper error handling with TickerResult validation). **TickerQ Architecture**: Job implementations in main app (`/Jobs`) for source generator discovery (TickerQ v2.5.3 limitation), job payload types in Telegram.Abstractions (`/Jobs/JobPayloads.cs`) to avoid circular dependencies. Flow: restrict on join → welcome + buttons → timeout auto-kick (60s default) → accept (restore + DM/fallback) → deny/timeout (kick). Templates: chat_welcome, dm_template, chat_fallback. Variables: {chat_name}, {username}, {rules_text}. WelcomeConfig converted from record to class for Blazor binding. Successfully tested end-to-end: user join → 60s timeout → auto-kick.
**4.5** ✅: OpenAI tri-state result system (spam/clean/review), SpamCheckResultType enum refactor, modular prompt building (technical base + custom rules + mode guidance), review queue integration, UI display updates. Prompt architecture: GetBaseTechnicalPrompt() (JSON format, unchangeable), GetDefaultRulesPrompt() (spam/legitimate indicators, user-overridable), GetModeGuidancePrompt() (veto vs detection logic), BuildSystemPrompt() factory. Custom prompts replace rules section only while preserving technical requirements and mode behavior. Tested: testimonial spam detection (95% confirm) vs legitimate crypto discussion (90% veto).
**4.6**: /tempban (5min/1hr/24hr presets, Telegram until_date auto-unrestrict, user_actions audit, UI integration)
**4.7**: /settings#logging (dynamic log levels like *arr apps, configs JSONB storage, ILoggerFactory immediate application)
**4.8** ✅: Settings UI completion - All tabs implemented with real functionality. General (App + MessageHistory env display), Integrations (OpenAI/SendGrid/VirusTotal/Telegram status + masked API keys), Notifications (email/Telegram/webhook future features listed), Security (auth/password/audit/API/data protection current + future), Logging (Phase 4.7 placeholder + troubleshooting guide). All settings read-only (env var configured), informational display only. 0 errors, 0 warnings.
**4.9**: Dynamic bot connection management - Hot-reload bot without restart, IBotLifecycleService interface, ReconnectAsync/DisconnectAsync methods, internal CancellationTokenSource for bot-specific cancellation, BotConnectionManager.razor component with real-time status updates, /settings#bot-connection tab, persist connection state in configs, Owner-only access. Enables bot token hot-swap, maintenance mode, and troubleshooting without app restart.

### Phase 5: Analytics & Data Aggregation 🔮 FUTURE
**5.1**: Analytics repo (time-series queries, false positive/negative rates, per-check performance)
**5.2**: TickerQ daily aggregation (analytics_daily_stats, api_usage_stats, check_performance_stats, weekly/monthly rollups)
**5.3**: Analytics UI (/analytics#trends - volume/ratios/patterns, /analytics#performance - accuracy/check perf/confidence)
**5.4**: Charting library (MudBlazor Charts or ApexCharts.Blazor, line/bar/pie charts)

### Phase 6: ML-Powered Insights 🔮 FUTURE
**6.1**: Insights service (manual override analysis, check performance, stop word suggestions, pattern detection via ML clustering, auto-trust effectiveness)
**6.2**: OpenAI recommendations (ML → natural language, "Apply" buttons, priority levels)
**6.3**: Insights dashboard (/analytics#insights - config/performance/cost/patterns/auto-trust cards, historical tracking)
**6.4**: Background insights (TickerQ daily job, high-priority notifications)

### Phase 7: Advanced Features 🔮 OPTIONAL
ML-based spam detection (10th algorithm using historical data), sentiment analysis (shelved - false positives, /report sufficient), API for third-party integrations (not needed)

## Future Enhancements (Pending Feedback)
**Cross-Group Welcome Exemption**: Auto-delete rules notification for already-trusted users joining new groups (no restrictions, tagged message with rules, X seconds auto-delete, maintains trust transfer). Decision pending: awaiting admin feedback on auto-trust vs per-group vetting preference.

## Comprehensive Refactoring Review Task

**Purpose**: Systematic code quality analysis across entire codebase using dotnet-refactor-advisor agent

**Scope**: Full solution (all 5 projects), focusing on:
- Architecture patterns (DI, service composition, separation of concerns)
- Performance opportunities (query optimization, caching, async patterns)
- Modern C# usage (C# 12/13 features, collection expressions, pattern matching)
- Code duplication (extract common patterns, shared utilities)
- Error handling consistency (logging, fail-open patterns, exception boundaries)
- Test coverage gaps (unit testability, mock-friendly abstractions)
- Configuration management (magic numbers, hardcoded values)
- Security patterns (input validation, sanitization, rate limiting)

**Deliverable**: Prioritized refactoring backlog with:
- Severity classification (Critical/High/Medium/Low)
- Effort estimates (hours)
- Impact analysis (performance, maintainability, security)
- Before/after code examples
- Recommended execution order

**Constraints**:
- Must maintain 0 errors, 0 warnings build quality
- No breaking changes to public APIs
- Preserve existing functionality and behavior
- Follow readability-first principle (modern features only when clarity improves)
- Consider production stability (low-risk changes first)

**Agent Invocation**: Use dotnet-refactor-advisor agent with instructions to analyze entire solution structure, focus on high-impact opportunities, provide concrete recommendations with code examples, and output structured markdown for REFACTORING_BACKLOG.md

**Priority**: Medium (schedule after Phase 4.4 completion, before Phase 4.5 starts)

## Current Status (October 2025)

### Completion Metrics
**Overall**: ~83% complete, ~96% core features
**Phase 1**: 100% ✅
**Phase 2**: 100% ✅ (2.1-2.8 all complete)
**Phase 3**: 100% ✅
**Phase 4**: 75% (4.1-4.5, 4.8 complete, 4.6-4.7 pending)
**Phases 5-7**: 0% (future/optional)

### Production Readiness
**Migration & Backup**: ✅ Complete (consolidated InitialSchema, validated, backup/restore with Data Protection, topological sort, sequence resets, strict DTO validation)
**Build Quality**: ✅ 0 errors, 0 warnings (production-ready)
**System Ready For**: Fresh DB init (`dotnet run --migrate-only`), cross-machine backup/restore with TOTP preservation, production deployment

### Next Steps (Prioritized)
**Immediate** (this week): Phase 4.6 temporary ban system (2-3 hours) OR Phase 4.7 runtime log config (4-6 hours)
**Short Term** (next 2 weeks): Complete remaining Phase 4 items (4.6 + 4.7)
**Post Phase 4**: Comprehensive refactoring review (dotnet-refactor-advisor agent), then optionally Phase 5-6 (analytics + ML insights)
**Phase 4.8** ✅ COMPLETE: All Settings tabs implemented, 0 errors/warnings maintained

### Critical Issues (REFACTORING_BACKLOG.md)
**C1 - Fire-and-Forget Tasks** (✅ RESOLVED): Replaced all `Task.Run` with TickerQ in WelcomeService. WelcomeTimeoutJob + DeleteMessageJob implemented with persistence/retry/logging. Production reliability ensured.
**Performance Optimizations** (✅ MH1+MH2 COMPLETE): GetStatsAsync (2→1 query, 80% faster), CleanupExpiredAsync (3→1 query, 50% faster). Both use single-query aggregation with GroupBy.
**High Priority Refactorings** (✅ H1+H2 COMPLETE): ChatPermissions extracted to static helpers, magic numbers moved to database config (MaxConfidenceVetoThreshold, Translation thresholds). No migration needed - C# defaults handle missing properties.

**Recommended Order**: Comprehensive review agent → Optional: M1-M4 (readability improvements) → Phase 4.6 (/tempban)
