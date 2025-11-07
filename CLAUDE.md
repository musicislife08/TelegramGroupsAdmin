# TelegramGroupsAdmin - AI Reference

## Stack

.NET 9.0 (9.0.100), Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 9.0, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

**Note**: Migrated from .NET 10 RC2 → .NET 9 due to a framework bug where Blazor Server apps don't generate `wwwroot/_framework/blazor.web.js` during publish, causing 404s in Production mode. Will upgrade to .NET 10 after RTM release (November 11, 2025).

## Use Case & Deployment Context

**Target Environment**: Homelab deployment for personal/community use (10-1,000 member groups, 500-20,000 messages/day)
**Design Philosophy**: Optimize for **operational simplicity** and **reliability** over horizontal scalability. Prefer single-instance architecture with minimal external dependencies (no Redis, no S3, no message queues). Code maintainability prioritized for solo/small team maintenance.

**AI Agent Guidance**: Evaluate against homelab deployment standards, not enterprise SaaS requirements. Prioritize operational simplicity, feature completeness, and single-maintainer comprehensibility over microservices patterns, distributed systems, or premature optimization for scale.

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

- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs
- **TelegramGroupsAdmin.Core**: Shared models, enums, interfaces (breaks circular dependencies between projects)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (DB-internal models)
- **TelegramGroupsAdmin.Telegram**: Bot services, commands, repos, orchestrators, DM notifications, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks Telegram → Main circular dep)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven
- **TelegramGroupsAdmin.ContentDetection**: URL filtering, impersonation detection, file scanning (ClamAV+VirusTotal)
- **TelegramGroupsAdmin.Tests**: Migration tests (NUnit + Testcontainers.PostgreSQL), validates against real PostgreSQL 17

## Architecture Patterns

**Extension Methods**: ServiceCollectionExtensions (Add* methods), WebApplicationExtensions (Configure*, Map*, RunDatabaseMigrationsAsync), ConfigurationExtensions

**Layered Architecture**:
- Data layer: DB-internal models (Data.Models namespace)
- Telegram layer: Telegram domain models (Telegram.Models namespace)
- UI layer: Blazor DTOs
- Repositories handle conversion via .ToModel()/.ToDto() extensions (26 files in Mappings/ subdirectory)
- Repos return/accept UI models only, never expose Data models

**Partial Unique Indexes** (PostgreSQL):
- Pattern: `WHERE` clause to enforce constraints only when specific condition met
- Used in: configs (chat_id=0 singleton), reports (status=0 pending), message_translations (message_id/edit_id not null)
- Purpose: State-dependent uniqueness (e.g., only ONE pending report per message, but can re-report after resolution)

**Exclusive Arc Pattern**:
- Pattern: Mutually exclusive foreign keys (message_id XOR edit_id, checked via constraints)
- Used in: message_translations (exclusive to message OR edit), audit_log (actor can be user OR telegram_user OR system)
- Purpose: Polymorphic relationships without nullable FKs

**Database-First Configuration**:
- Pattern: Settings stored in database (encrypted when sensitive), not env vars
- Migration: TelegramConfigMigrationService, ApiKeyMigrationService auto-migrate on first startup
- Fallback: Env vars used for first-time setup only
- UI: Settings pages allow live editing without restart

**Background Services**: TelegramAdminBotService (bot polling), MessageProcessingService (messages/edits/spam), ChatManagementService (admin cache), SpamActionService (training QC, cross-chat bans), CleanupBackgroundService (retention)

## Configuration

**Pattern**: Database-first, all configuration via Settings UI (no env vars required)

**UI-Managed** (encrypted in database):
- **Telegram Bot Token**: Settings → Telegram → Bot Configuration → General
- **OpenAI API Key**: Settings → System → OpenAI
- **SendGrid Settings**: Settings → System → Email
- **VirusTotal API Key**: Settings → Content Detection → File Scanning
- **CAS API Key**: Settings → Content Detection → Detection Algorithms

**Migration Services** (optional, one-time only):
- TelegramConfigMigrationService: Migrates `TELEGRAM__BOTTOKEN` env var to database on first startup
- ApiKeyMigrationService: Migrates OpenAI/SendGrid env vars to database on first startup
- After migration, remove env vars - all future changes via Settings UI

**Runtime Editing**: Settings UI allows live config changes without restart

## Key Architectural Features

**Telegram Bot API Dual-Mode**: Standard api.telegram.org (20MB limit) or self-hosted Bot API server (up to 2GB). TelegramBotClientFactory caches clients by `token::url` key. Graceful fallback for oversized files.

**File Scanning Streaming Architecture**: FileScanJob passes file paths (not loaded into memory). ClamAVScannerService validates size <2GB, VirusTotalScannerService uses StreamContent. Supports 2GB files with minimal memory.

**TickerQ Job Architecture**: Jobs in main app (for source generator), payloads in Abstractions (breaks circular deps). Jobs re-throw exceptions for retry/logging. Polling interval: 5s (default 60s override).

**Translation Storage**: Exclusive arc pattern (message_id XOR edit_id) in message_translations table. MessageProcessingService translates before save (≥10 chars, <80% Latin script). Reused by spam detection.

**DM Notifications**: IDmDeliveryService (Singleton), pending_notifications table (30d expiry), auto-delivery on `/start`. Account linking (`/link`) separate from DM setup.

## Permissions

**Levels**: 0=Admin (chat-scoped), 1=GlobalAdmin (global moderation), 2=Owner (full access) - hierarchy, cannot escalate above own
**Chat Access**: Admin sees only chats they're Telegram admin in (uses chat_admins table), GlobalAdmin/Owner see all chats
**Enum Location**: Core.Models.PermissionLevel (canonical with Display attributes), Data.Models.PermissionLevel (DB-only, self-contained)
**User Status**: 0=Pending, 1=Active, 2=Disabled, 3=Deleted
**Invites**: 7-day expiry, first user auto-Owner, permission inheritance

## Troubleshooting

- Bot not caching: Check TELEGRAM__BOTTOKEN, bot in chat, privacy mode off
- Image spam failing: Check OPENAI__APIKEY, /data mounted
- DB growing: Check retention (720h default), cleanup running
- Rate limits: Check logs for VirusTotalService/OpenAIVisionSpamDetectionService warnings
- Testing: Always use `--migrate-only` flag, never run app in normal mode (only one instance allowed)
- **Blazor 404 in Production**: .NET 10 RC2 bug - framework files not generated during publish. Use .NET 9 stable until .NET 10 RTM.

### TickerQ Background Jobs

**Dashboard**: `/tickerq-dashboard` (development mode only)

**Common Issues**:
- **0 Active Functions**: Source generator not discovering jobs. Check: (1) Explicit analyzer reference in .csproj, (2) `TickerQInstanceFactory.Initialize()` called BEFORE `app.UseTickerQ()` in Program.cs (timing critical)
- **0 Active Threads**: Workers not starting. Check: (1) Initialize() timing, (2) Debug logging for "TickerQ" messages, (3) Tables exist in `ticker` schema (NOT public)
- **Job Syntax**: Use `TickerQ.Utilities.Base.TickerFunctionAttribute` and `TickerFunctionContext<T>`. Reference existing jobs for patterns.

## Documentation

**BACKLOG.md**: Contains **pending work only** (features, bugs, refactoring). Do not add "Completed Work" sections - use `git log` for history.
**Commit Messages**: All completed work documented with full technical details. Single source of truth for project history.

## Testing

**Migration Tests**: NUnit + Testcontainers.PostgreSQL, validates all migrations against real PostgreSQL 17 (tests always passing)

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
