# TelegramGroupsAdmin - AI Reference

## Stack

.NET 9.0 (9.0.100), Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 9.0, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid, Seq (datalust/seq:latest), OpenTelemetry

**Note**: Migrated from .NET 10 RC2 → .NET 9 due to a framework bug where Blazor Server apps don't generate `wwwroot/_framework/blazor.web.js` during publish, causing 404s in Production mode. Will upgrade to .NET 10 after RTM release (November 11, 2025).

## Git Workflow (CRITICAL - FOLLOW EVERY TIME)

**Repository**: https://github.com/musicislife08/TelegramGroupsAdmin

**Branch Protection Enforced:**
- ❌ **NEVER commit directly to `master` or `develop`** - Both branches are protected and require PRs
- ❌ **NEVER create PRs from feature branches to `master`** - Automated validation will block this
- ✅ **ALWAYS use feature branches** - Create branch for every change, no matter how small
- ✅ **ALWAYS create PR to `develop` first** - All changes must go through develop for testing
- ✅ **ALWAYS wait for CI to pass** - Both `Build and Test` and `Validate PR Source Branch` must be green

**Required Workflow for ALL Changes:**

```bash
# 1. Start from latest develop
git checkout develop
git pull origin develop

# 2. Create feature branch (descriptive name)
git checkout -b feature/your-feature-name
# OR: git checkout -b fix/bug-description
# OR: git checkout -b refactor/what-you-refactored

# 3. Make changes, commit with conventional commit messages
git add .
git commit -m "feat: add new feature"
# OR: fix:, refactor:, docs:, test:, ci:, chore:

# 4. Push feature branch
git push -u origin feature/your-feature-name

# 5. Create PR to develop (NOT master)
# IMPORTANT: Add closing keywords to link issues in the Development section
# This auto-closes issues when PR merges and shows them in the sidebar
gh pr create --base develop --head feature/your-feature-name \
  --title "feat: Add new feature" \
  --body "Closes #123, Closes #456

## Summary
Description of changes..."

# Closing keywords: Closes, Fixes, Resolves (case-insensitive, with variations)
# Format: "Closes #123" or "Fixes #456" (separate multiple with commas)
# These appear in the PR's Development section sidebar and auto-close on merge

# 6. Wait for CI checks to pass (Build and Test runs during PR review, not on merge)
# 7. Merge PR via GitHub UI (Docker images publish after merge, build does not re-run)
# 8. Delete feature branch after merge
# 9. Pull latest develop
git checkout develop
git pull origin develop
```

**Release to Production (develop → master):**

```bash
# Only when develop is stable and ready for release
# IMPORTANT: Add closing keywords at top of PR body to link resolved issues
gh pr create --base master --head develop \
  --title "Release: v1.2.3" \
  --body "Closes #31, Closes #35, Closes #40

## Summary
Release notes here..."

# Merge via GitHub UI after CI passes
# Docker images auto-published:
#   - ghcr.io/musicislife08/telegramgroupsadmin:latest
#   - ghcr.io/musicislife08/telegramgroupsadmin:1.2.3
#   - ghcr.io/musicislife08/telegramgroupsadmin:1.2
```

**Emergency Hotfix (RARE - User must explicitly request):**

```bash
# Create hotfix branch from master
git checkout master
git pull origin master
git checkout -b hotfix/critical-bug

# Make minimal fix, commit, push
git push -u origin hotfix/critical-bug

# Create PR to master (user bypasses protection as admin)
gh pr create --base master --head hotfix/critical-bug

# After merge to master, backport to develop
git checkout develop
git merge master
git push origin develop
```

**Docker Image Tags:**
- **develop branch** → `development` + semver (e.g., `1.3.0-beta.5`)
- **master branch** → `latest` + semver + major.minor (e.g., `latest`, `1.2.3`, `1.2`)
- **Architectures**: Multi-arch images support `linux/amd64` and `linux/arm64`

**AI Agent Instructions:**
- At the start of EVERY session, check current branch with `git branch`
- If on `master` or `develop`, IMMEDIATELY switch to a feature branch
- NEVER suggest bypassing the PR workflow "to save time" - branch protection is mandatory
- If user asks to commit directly to master/develop, remind them of the protected workflow
- Always create PRs to `develop` first, never to `master` (validation will block it)
- **ALWAYS include closing keywords** (`Closes #123`) at the top of PR body to link issues in Development section

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

**Observability (Optional)** (environment variables, for debugging/development):
- `SEQ_URL`: Seq server URL (e.g., `http://seq:5341`) - if not set, logs only to console
- `SEQ_API_KEY`: Seq API key for ingestion authentication (optional, leave empty for no auth)
- `OTEL_SERVICE_NAME`: Service name for traces/metrics (default: `TelegramGroupsAdmin`)

When `SEQ_URL` is configured, the application automatically enables:
- **Structured Logging**: Serilog logs sent to Seq with trace correlation
- **Distributed Tracing**: OpenTelemetry traces for HTTP requests, database queries, background jobs
- **Metrics**: Prometheus metrics endpoint at `/metrics` (runtime performance, request rates, job execution)

## Key Architectural Features

**Telegram Bot API**: Uses standard api.telegram.org endpoint with 20MB file download limit. TelegramBotClientFactory caches clients by bot token. Graceful fallback for oversized files.

**File Scanning Streaming Architecture**: FileScanJob passes file paths (not loaded into memory). ClamAVScannerService validates size <20MB, VirusTotalScannerService uses StreamContent. Supports files up to 20MB with minimal memory.

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

### Observability (Seq + OpenTelemetry)

**Accessing Seq**: http://localhost:5341 (when `SEQ_URL` environment variable is configured)

**Common Issues**:
- **Seq not receiving logs**: Check `SEQ_URL` environment variable, verify network connectivity (`docker network inspect telegram-admin`), check container logs (`docker logs tga-seq`)
- **Missing traces**: Verify `ActivitySource` registration in Program.cs, check OTLP exporter configuration points to correct Seq URL
- **`/metrics` returning 404**: Ensure `app.MapPrometheusScrapingEndpoint()` called after `app.UseRouting()` in Program.cs, verify `SEQ_URL` is set (metrics endpoint only mapped when observability enabled)
- **High memory usage**: Configure OpenTelemetry batch size limits via environment variables, adjust sampling rate if needed
- **Seq retention**: Configure retention policies in Seq UI (Settings → Retention), default is 7 days
- **Application works without Seq**: By design - all observability is optional. If `SEQ_URL` not set, app logs to console only and operates normally

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
