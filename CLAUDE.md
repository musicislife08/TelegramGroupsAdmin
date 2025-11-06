# TelegramGroupsAdmin - AI Reference

## Stack

.NET 9.0 (9.0.100), Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 9.0, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

**Note**: Migrated from .NET 10 RC2 → .NET 9 due to [framework bug](https://github.com/dotnet/aspnetcore/issues/XXXXX) where Blazor Server apps don't generate `wwwroot/_framework/blazor.web.js` during publish, causing 404s in Production mode. Will revisit .NET 10 after RTM release.

## Use Case & Deployment Context

**Target Environment**: Homelab deployment for personal/community use
**Workload**: Small to mid-sized Telegram groups (10-1,000 members)
**Message Volume**: 500-5,000 messages/day (current load), designed to handle up to 20,000/day
**User Base**: Single administrator or small admin team (1-5 people)
**Network**: Private network, reverse proxy optional, trusted user environment

**Performance Benchmarks** (measured in production):

- Spam detection: 255ms average, 821ms P95 (9 algorithms + OpenAI)
- Analytics queries: <100ms
- Message page load: 50+ messages without lag

**Design Philosophy**: Optimize for **operational simplicity** and **reliability** over horizontal scalability. Prefer single-instance architecture with minimal external dependencies (no Redis, no S3, no message queues). Code maintainability prioritized for solo/small team maintenance.

**AI Agent Guidance**: When reviewing this codebase, evaluate against homelab deployment standards, not enterprise SaaS requirements. Recommendations should prioritize operational simplicity, feature completeness, and single-maintainer comprehensibility over microservices patterns, distributed systems, or premature optimization for scale.

## Deployment Architecture

**Single Instance Design** (architectural constraint, not limitation):
The Telegram Bot API enforces **one active connection per bot token** (webhook OR polling). This makes the application inherently singleton - running 2+ instances causes bot connection conflicts where Telegram disconnects earlier instances. All services (bot polling, web UI, background jobs) run in a single container by design.

**Deployment Model**:

```plain
┌─────────────────────────────────────┐
│  TelegramGroupsAdmin Container      │
│  ├─ TelegramAdminBotService         │ ← Telegram polling (SINGLETON enforced by API)
│  ├─ Blazor Server UI                │
│  ├─ API Endpoints                   │
│  └─ TickerQ Background Jobs         │
└─────────────────────────────────────┘
         ↓
    PostgreSQL 17
         ↓
    /data volume (media, keys)
```

**Technology Choices Optimized for Singleton**:

- **In-memory caching** - No Redis/distributed cache (unnecessary for single instance)
- **Local file storage** - /data/media on filesystem (no S3/blob storage complexity)
- **TickerQ PostgreSQL backend** - No separate message queue dependency
- **Direct database access** - No service mesh or API gateway layers
- **Embedded background jobs** - Jobs run in main process (no separate worker containers)

**Horizontal Scaling**: Not applicable unless bot service is extracted to separate container (adds message queue, distributed state, operational complexity - not planned for homelab use case). Current architecture handles 20,000+ messages/day on single instance.

**AI Agent Guidance**: Do not recommend distributed systems patterns (Redis, RabbitMQ, S3, Kubernetes, microservices) unless the user explicitly plans to scale beyond single-instance limits. The singleton constraint is a feature, not a bug.

## Projects

- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob, TempbanExpiryJob, FileScanJob)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (internal to repos)
- **TelegramGroupsAdmin.Telegram**: Bot services, 14 commands, repos, orchestrators, DM notifications, TelegramMediaService, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks circular deps)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven
- **TelegramGroupsAdmin.ContentDetection**: URL filtering (blocklists, domain filters), impersonation detection (photo hash, Levenshtein), file scanning (ClamAV+VirusTotal), AddContentDetectionServices()
- **TelegramGroupsAdmin.Tests**: Migration tests (NUnit + Testcontainers.PostgreSQL), 19 tests in 5 phases, validates migrations against real PostgreSQL 17

## Architecture Patterns

**Extension Methods**: ServiceCollectionExtensions (AddBlazorServices, AddCookieAuthentication, AddApplicationServices, AddHttpClients, AddTelegramServices, AddRepositories, AddTgSpamWebDataServices, AddTickerQBackgroundJobs), WebApplicationExtensions (ConfigurePipeline, MapApiEndpoints, RunDatabaseMigrationsAsync), ConfigurationExtensions (AddApplicationConfiguration)

**Layered**: UI Models (Blazor DTOs) → Repositories (conversion) → Data Models (DB internal). ModelMappings.cs: .ToModel()/.ToDto() extensions. Repos return/accept UI models only.

**Spam Detection**: ISpamDetectorFactory orchestrates 9 checks (StopWords, CAS, Similarity/TF-IDF, Bayes, MultiLanguage, Spacing, OpenAI, ThreatIntel/VirusTotal, Image/Vision), confidence aggregation, OpenAI veto, self-learning

**Background Services** (composition pattern):

1. TelegramAdminBotService - Bot polling, update routing (5 types), command registration, health checks every 1 min
2. MessageProcessingService - New messages, edits, media download (Animation/Video/Audio/Voice/Sticker/VideoNote), spam orchestration, URL extraction, user photo scheduling, file scan scheduling, **translation before save** (Phase 4.20), **language warnings** (Phase 4.21)
3. ChatManagementService - MyChatMember, admin cache, health checks (permissions + invite link validation), chat names
4. SpamActionService - Training QC, auto-ban (cross-chat), borderline reports
5. CleanupBackgroundService - Message retention (keeps spam/ham samples)

## Configuration (Env Vars)

**Required**: OPENAI__APIKEY, SPAMDETECTION__APIKEY, SENDGRID__APIKEY/FROMEMAIL/FROMNAME
**Optional**: APP__BASEURL, OPENAI__MODEL/MAXTOKENS, MESSAGEHISTORY__*, IDENTITY__DATABASEPATH, DATAPROTECTION__KEYSPATH
**Database-Managed** (with env var fallback for first-time setup):
- **TELEGRAM__BOTTOKEN**: Bot API token (encrypted in `configs.telegram_bot_token_encrypted`)
- **TELEGRAM__CHATID**: Primary chat ID (stored in `configs.telegram_bot_config` JSONB)
- **TELEGRAM__APISERVERURL**: Optional self-hosted Bot API server URL for unlimited file downloads
- **VIRUSTOTAL__APIKEY**: VirusTotal API key (encrypted in `configs.api_keys`)

**Migration**: TelegramConfigMigrationService auto-migrates Telegram env vars → database on first startup. Configure via Settings → Telegram → Bot Configuration UI.

## Key Implementations

- **Telegram Bot API Dual-Mode**: Supports standard api.telegram.org (20MB file limit) and self-hosted Bot API server (unlimited downloads up to 2GB). TelegramBotClientFactory caches clients by `token::url` key. Graceful error handling logs warnings for files >20MB on standard API with guidance to configure self-hosted mode. Settings UI provides inline setup instructions. See `docker-compose.bot-api.yml` for production-ready deployment template.
- **File Scanning (Streaming Architecture)**: FileScanJob passes file path instead of loading entire files into memory. ClamAVScannerService validates file size <2GB before reading. VirusTotalScannerService uses StreamContent for efficient HTTP uploads. Tier1VotingCoordinator scans in parallel from disk. Supports files up to 2GB with minimal memory footprint.
- **API Key Management**: File scanning API key (VirusTotal) stored encrypted in configs.api_keys (TEXT column, ASP.NET Core Data Protection). ApiKeyMigrationService migrates env vars on first startup. ApiKeyDelegatingHandler dynamically loads keys from database at request time with env var fallback. Settings UI allows editing. Backup/restore auto-handles decryption/re-encryption via [ProtectedData] attribute.
- **Rate Limiting**: VirusTotal (Polly PartitionedRateLimiter, 4/min), OpenAI (429 detection), both fail-open
- **Edit Detection**: On edit → message_edits, update messages, re-run spam detection, action if spam
- **Email Verification**: 24h token (32 random bytes), login blocked until verified (except first Owner)
- **TOTP Security**: IntermediateAuthService (5min tokens after password), 15min expiry for abandoned setups
- **TickerQ Jobs**: All jobs re-throw exceptions for proper retry/logging. WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob, TempbanExpiryJob, FileScanJob. Jobs in main app for source generator, payloads in Abstractions. Polling interval: 5s (default 60s) via UpdateMissedJobCheckDelay().
- **Infinite Scroll**: IntersectionObserver on scroll sentinel, timestamp-based pagination (`beforeTimestamp`), MudVirtualize, loads 50 messages/page
- **Scroll Preservation**: Handles negative scrollTop (Chrome/Edge flex-reverse), captures state before DOM update, double requestAnimationFrame for layout completion, polarity-aware adjustment formula, 5px bottom threshold
- **DM Notifications**: IDmDeliveryService (Singleton, creates scopes), pending_notifications (30d expiry), auto-delivery on `/start`. Account linking (`/link`) separate from DM setup. Future: Notification preferences UI with deep link to enable bot DMs.
- **Media Attachments**: TelegramMediaService downloads and saves media (Animation/Video/Audio/Voice/Sticker/VideoNote) to /data/media, stored in messages table. Documents metadata-only (no download for display, file scanner handles temp download). MediaType enum duplicated in Data/Telegram layers (architectural boundary). UI displays media with HTML5 elements (video/audio controls, autoplay for GIFs).
- **AI Prompt Builder**: Meta-AI feature using OpenAI to generate/improve custom spam detection prompts. Two workflows: (1) Generate from scratch via form (group topic, rules, strictness, training samples), (2) Improve existing version with feedback. prompt_versions table tracks history with versioning, metadata (generation params), created_by. Shared PromptImprovementDialog reused by generation flow and version history. Auto-grow textarea shows full prompt. Settings → Content Detection → OpenAI Integration shows version history with View/Restore/Improve buttons.
- **Translation Storage** (Phase 4.20): message_translations table with exclusive arc pattern (message_id XOR edit_id), LEFT JOIN in main query. MessageProcessingService translates before save (≥10 chars, <80% Latin script) using OpenAITranslationService. UI: toggle button (🌐 badge), manual translate button in popover menu, EditHistoryDialog shows translations for edits. Translation reused by spam detection (OpenAI Vision, MultiLanguage). CASCADE delete, partial indexes for performance.

## API Endpoints

- GET /health
- POST /api/auth/login → {requiresTotp, userId, intermediateToken}
- POST /api/auth/register, /api/auth/logout, /api/auth/verify-totp
- GET /verify-email?token=X
- POST /resend-verification, /forgot-password, /reset-password

## Blazor Pages

**Public**: /login, /login/verify, /login/setup-2fa, /register
**Authenticated**: / (dashboard), /analytics (Admin+), /messages, /users (Admin+), /reports (Admin+), /audit (Admin+), /settings (Admin+), /profile
**Features**: URL fragment nav, nested sidebar navigation in Settings, component reuse

## Permissions

**Levels**: 0=Admin (chat-scoped), 1=GlobalAdmin (global moderation), 2=Owner (full access) - hierarchy, cannot escalate above own
**Chat Access**: Admin sees only chats they're Telegram admin in (uses chat_admins table), GlobalAdmin/Owner see all chats
**Enum Location**: Core.Models.PermissionLevel (canonical with Display attributes), Data.Models.PermissionLevel (DB-only, self-contained)
**User Status**: 0=Pending, 1=Active, 2=Disabled, 3=Deleted
**Invites**: 7-day expiry, first user auto-Owner, permission inheritance

## Build Quality

**Standard**: 0 errors, 0 warnings (production-ready)
**History**: 158+ errors→0, 62+ warnings→0, MudBlazor v8.13.0, records→classes for binding, null safety, type safety

## Troubleshooting

- Bot not caching: Check TELEGRAM__BOTTOKEN, bot in chat, privacy mode off
- Image spam failing: Check OPENAI__APIKEY, /data mounted
- DB growing: Check retention (720h default), cleanup running
- Rate limits: Check logs for VirusTotalService/OpenAIVisionSpamDetectionService warnings
- Testing: Always use `--migrate-only` flag, never run app in normal mode (only one instance allowed)
- **Blazor 404 in Production**: .NET 10 RC2 bug - framework files not generated during publish. Use .NET 9 stable until .NET 10 RTM.

### TickerQ Background Jobs

**Dashboard**: `/tickerq-dashboard` (development mode only, disabled in production)

**0 Active Functions** (source generator not discovering jobs):

1. Verify explicit analyzer reference in .csproj: `<Analyzer Include="$(NuGetPackageRoot)tickerq/2.5.3/analyzers/dotnet/cs/TickerQ.SourceGenerator.dll" />`
2. Build with `/p:EmitCompilerGeneratedFiles=true` to check `obj/generated/TickerQ.SourceGenerator/*/TickerQInstanceFactory.g.cs` exists
3. Confirm `TelegramGroupsAdmin.TickerQInstanceFactory.Initialize()` is called in Program.cs **BEFORE** `app.UseTickerQ()` (timing critical for .NET 10 RC2)

**0 Active Threads** (workers not starting):

1. Check `TickerQInstanceFactory.Initialize()` runs BEFORE `app.UseTickerQ()` in pipeline (Program.cs line ~59)
2. Enable debug logging: `builder.Logging.SetMinimumLevel(LogLevel.Debug)` and search for "TickerQ" or "Hosting" messages
3. Verify tables exist: `SELECT table_name FROM information_schema.tables WHERE table_schema = 'ticker';` (NOT public schema!)

**Job Syntax**: `using TickerQ.Utilities.Base;` (TickerFunctionAttribute), `using TickerQ.Utilities.Models;` (`TickerFunctionContext<T>`)

## Development Status

**Complete**: Phase 1-4 (Foundation, spam detection, TickerQ jobs, welcome system, file scanning, translations, notifications)
**Roadmap**: See BACKLOG.md for all pending work and future features

**Note**: BACKLOG.md contains **pending work only** (features, bugs, refactoring). For completed work history, use `git log` - all completed tasks are documented in commit messages with full technical details. Do not maintain a "Completed Work" section in BACKLOG.md to avoid duplication.

## Testing

**Migration Tests**: 22 tests (all passing) via Testcontainers.PostgreSQL, validates against real PostgreSQL 17

## CRITICAL RULES

### Application Runtime

- NEVER run the app in normal mode - only one instance allowed (Telegram singleton constraint), user tests in Rider
- Validate builds with `dotnet run --migrate-only` to catch startup issues without running the bot
- Always defer to manual user testing for runtime behavior - too complex to validate automatically

### EF Core Migrations

- Workflow: Modify Data models + AppDbContext FIRST → then run `dotnet ef migrations add` (never reverse)
- Prefer Fluent API configuration in AppDbContext over custom SQL for indexes, constraints, relationships

### UI Frameworks

- MudBlazor v8+: Use `IMudDialogInstance` (interface), not `MudDialogInstance` (concrete class)

### Documentation

- Never include time estimates in documentation or backlog items
