# TelegramGroupsAdmin - AI Reference

## Stack

.NET 10.0 (10.0.100), Blazor Server, MudBlazor 8.15.0, PostgreSQL 18, EF Core 10.0, Npgsql 10.0.0, Quartz.NET 3.15.1, OpenAI API, VirusTotal, SendGrid, Seq (datalust/seq:latest), OpenTelemetry

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

# AI AGENT NOTE: For complex multi-line commit messages, use properly escaped heredoc:
# git commit -F- <<'EOF'
# Multi-line message here
# Can include quotes, apostrophes, etc.
# EOF
# Key: Use -F- (read from stdin) and <<'EOF' (single quotes prevent expansion)

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
# 1. Create and merge release PR (develop → master)
gh pr create --base master --head develop \
  --title "Release: v1.2.3" \
  --body "Closes #31, Closes #35, Closes #40

## Summary
Release notes here..."

# Merge via GitHub UI after CI passes
# NOTE: Merging to master does NOT publish Docker images (that's triggered by release creation)

# 2. Create git tag on master
git checkout master && git pull origin master
git tag -a v1.2.3 -m "Release v1.2.3 - Brief description"
git push origin v1.2.3

# 3. Create GitHub Release (THIS triggers Docker image publish)
gh release create v1.2.3 --title "v1.2.3" --notes "
## What's New
- Feature 1
- Feature 2

## Bug Fixes
- Fix 1

## Docker Images
docker pull ghcr.io/musicislife08/telegramgroupsadmin:latest
docker pull ghcr.io/musicislife08/telegramgroupsadmin:1.2.3
"

# Docker images are published ONLY when the release is created:
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
- **develop branch** (on push) → `development` + semver (e.g., `1.3.0-beta.5`)
- **GitHub Release** (on publish) → `latest` + semver + major.minor (e.g., `latest`, `1.2.3`, `1.2`)
- **master branch** (on merge) → No Docker publish (release creation triggers publish)
- **Architectures**: Multi-arch images support `linux/amd64` and `linux/arm64`

**AI Agent Instructions:**
- At the start of EVERY session, check current branch with `git branch`
- If on `master` or `develop`, IMMEDIATELY switch to a feature branch
- NEVER suggest bypassing the PR workflow "to save time" - branch protection is mandatory
- If user asks to commit directly to master/develop, remind them of the protected workflow
- Always create PRs to `develop` first, never to `master` (validation will block it)
- **ALWAYS include closing keywords** (`Closes #123`) at the top of PR body to link issues in Development section
- **ALWAYS prefer new commits over amending** - create a new commit instead of using `git commit --amend`

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
│  ├─ TelegramBotPollingHost          │ ← Telegram polling (SINGLETON enforced by API)
│  ├─ Blazor Server UI                │
│  ├─ API Endpoints                   │
│  └─ Quartz.NET Background Jobs      │
└─────────────────────────────────────┘
         ↓
    PostgreSQL 18
         ↓
    /data volume (media, keys)
```

**Technology Choices Optimized for Singleton**:

- **In-memory caching** - No Redis/distributed cache (unnecessary for single instance)
- **Local file storage** - /data/media on filesystem (no S3/blob storage complexity)
- **Quartz.NET PostgreSQL backend** - No separate message queue dependency
- **Direct database access** - No service mesh or API gateway layers
- **Embedded background jobs** - Jobs run in main process (no separate worker containers)

**Horizontal Scaling**: Not applicable unless bot service is extracted to separate container (adds message queue, distributed state, operational complexity - not planned for homelab use case). Current architecture handles 20,000+ messages/day on single instance.

**AI Agent Guidance**: Do not recommend distributed systems patterns (Redis, RabbitMQ, S3, Kubernetes, microservices) unless the user explicitly plans to scale beyond single-instance limits. The singleton constraint is a feature, not a bug.

## Projects

- **TelegramGroupsAdmin**: Main app, Blazor+API, Quartz.NET jobs
- **TelegramGroupsAdmin.Core**: Shared models, enums, interfaces (breaks circular dependencies between projects)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (DB-internal models)
- **TelegramGroupsAdmin.Telegram**: Bot services, commands, repos, orchestrators, DM notifications, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks Telegram → Main circular dep)
- **TelegramGroupsAdmin.ContentDetection**: 9 content detection algorithms, URL filtering, impersonation detection, file scanning (ClamAV+VirusTotal), self-contained, database-driven
- **TelegramGroupsAdmin.Tests**: Migration tests (NUnit + Testcontainers.PostgreSQL), validates against real PostgreSQL 18

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
- UI: Settings pages allow live editing without restart

**Background Services**: TelegramBotPollingHost (bot polling), MessageProcessingService (messages/edits/spam), ChatManagementService (admin cache), DetectionActionService (training QC, cross-chat bans)

## Configuration

**Pattern**: Database-first, all configuration via Settings UI (no env vars required)

**UI-Managed** (encrypted in database):
- **Telegram Bot Token**: Settings → Telegram → Bot Configuration → General
- **OpenAI API Key**: Settings → System → OpenAI
- **SendGrid Settings**: Settings → System → Email
- **VirusTotal API Key**: Settings → Content Detection → File Scanning
- **CAS Settings**: Settings → Welcome → Security on Join

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

**Quartz.NET Job Architecture**: Jobs in BackgroundJobs project, payloads in Abstractions (breaks circular deps). Jobs re-throw exceptions for retry/logging. Job configs stored in database (configs.background_jobs_config JSONB column).

**Quartz.NET Payload Access**: Always use `context.MergedJobDataMap` (not `JobDetail.JobDataMap`) to access trigger-level data. On-demand jobs triggered via `TriggerNowAsync` store payloads in the trigger's JobDataMap, which is only accessible via `MergedJobDataMap`. Use `JobPayloadHelper.TryGetPayloadAsync<T>()` for defensive payload extraction with automatic stale trigger cleanup.

**Translation Storage**: Exclusive arc pattern (message_id XOR edit_id) in message_translations table. MessageProcessingService translates before save (≥10 chars, <80% Latin script). Reused by spam detection.

**DM Notifications**: IBotDmService (Scoped), pending_notifications table (30d expiry), auto-delivery on `/start`. Account linking (`/link`) separate from DM setup.

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
- Rate limits: Check logs for VirusTotalService warnings
- Testing: Always use `--migrate-only` flag, never run app in normal mode (only one instance allowed)

### Quartz.NET Background Jobs

**Dashboard**: Settings → Background Jobs page (always available)

**Common Issues**:
- **Job not running**: Check: (1) Job enabled in database (configs.background_jobs_config), (2) Valid cron expression (6-part Quartz format), (3) QuartzSchedulingSyncService running
- **Schedule not updating**: Check NextRunAt field cleared when cron changes (BackgroundJobConfigService.UpdateJobConfigAsync)
- **Job Syntax**: Implement `IJob` interface, use `[DisallowConcurrentExecution]` for jobs that shouldn't overlap. Reference existing jobs in BackgroundJobs project for patterns.

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

**GitHub Issues**: All pending work tracked as GitHub issues (features, bugs, refactoring, tech debt). Issues are categorized with labels and milestones.
**Commit Messages**: All completed work documented with full technical details. Single source of truth for project history.
**PR Descriptions**: Link related issues using closing keywords (`Closes #123`) at the top of the PR body to auto-close issues when merged.

**Additional Documentation Files:**
- [E2E_TESTING.md](E2E_TESTING.md) - Playwright E2E testing patterns, .NET 10 `UseKestrel()` API usage, test infrastructure

### GitHub Issue Labels

**Type Labels:**
- `bug` - Something isn't working
- `enhancement` - New feature or request
- `documentation` - Improvements or additions to documentation
- `refactoring` - Code refactoring
- `tech-debt` - Technical debt
- `question` - Further information is requested
- `duplicate` - This issue or pull request already exists
- `invalid` - This doesn't seem right
- `wontfix` - This will not be worked on

**Priority Labels:**
- `priority-critical` - Critical priority work
- `priority-high` - High priority work
- `priority-medium` - Medium priority work
- `priority-low` - Low priority work

**Effort Labels:**
- `quick-win` - Can be completed in < 1 hour
- `small-task` - 1-2 hours of work
- `medium-task` - 2-4 hours of work
- `large-task` - 4+ hours of work

**Category Labels:**
- `ml` - Machine learning features
- `analytics` - Analytics and reporting
- `performance` - Performance optimization
- `deployment` - Deployment and infrastructure
- `backend` - Backend/API work
- `database` - Database schema or queries
- `frontend` - UI/Frontend work
- `ci-cd` - CI/CD pipeline work
- `testing` - Test coverage or testing tools
- `ux` - User experience improvements

**Helper Labels:**
- `good first issue` - Good for newcomers
- `help wanted` - Extra attention is needed

## Testing

**Migration Tests**: NUnit + Testcontainers.PostgreSQL, validates all migrations against real PostgreSQL 18 (tests always passing)

**E2E Tests**: Playwright + Testcontainers.PostgreSQL for browser-based UI testing. Uses .NET 10's `WebApplicationFactory.UseKestrel()` for real HTTP server. See [E2E_TESTING.md](E2E_TESTING.md) for full documentation.

**Telegram.Bot Type Mocking**: `Telegram.Bot.Types` classes (`Message`, `Animation`, `User`, etc.) are concrete types with non-virtual properties. **Do NOT use `Substitute.For<Message>()`** — NSubstitute cannot intercept non-virtual members. Instead, use direct object initialization: `new Message { Animation = new Animation { FileId = "test_id" } }`. See `MediaProcessingHandlerTests.cs` and `BanCelebrationServiceTests.cs` for examples.

**Test Execution Best Practices**:
- **ALWAYS run test commands without pipes initially** - pipes hide failures and require re-running to see errors
- ❌ Bad: `dotnet test --no-build | tail -20` (hides failures, requires second run to see errors)
- ✅ Good: `dotnet test --no-build` (see all output immediately, diagnose failures on first run)
- Only add pipes (`| tail`, `| grep`, etc.) when you need to filter known-passing output for readability

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

- Never include time estimates in documentation or GitHub issues
