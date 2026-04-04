# External Integrations

**Analysis Date:** 2026-03-15

## APIs & External Services

**Telegram Bot API:**
- Service: Telegram Bot API (api.telegram.org)
- What it's used for: Bot polling, message sending, command processing, group management
- SDK/Client: Telegram.Bot 22.9.5.3 (official)
- Auth: Bot token (stored in database via Settings UI)
- Implementation: `TelegramGroupsAdmin.Telegram.Services.TelegramBotClientFactory`
  - Caches clients by bot token
  - Single-instance architecture enforced by Telegram (one active connection per token)
  - TelegramBotPollingHost background service handles long polling

**Telegram User API:**
- Service: Telegram User Client (TDLib protocol)
- What it's used for: User profile scanning, account reputation checks, linked channel detection
- SDK/Client: WTelegramClient 4.4.2 (TDLib alternative)
- Auth: Phone number + verification code (session tokens stored in database)
- Implementation: `TelegramGroupsAdmin.Telegram.Services.UserApi.WTelegramClientFactory`
  - Multi-user session support (different user accounts)
  - Profile scanning: `IProfileScanService` in `TelegramGroupsAdmin.Telegram.Services`
  - Sessions persisted: `TelegramSessionRepository` via `TelegramSessionDto` table
  - OAuth-like flow: `ITelegramAuthService` with user interaction

**OpenAI API:**
- Service: OpenAI Chat Completion API (api.openai.com or Azure/compatible)
- What it's used for: Image analysis, prompt generation, exam criteria building, content detection reasoning
- SDK/Client: Microsoft Semantic Kernel 1.73.0 (multi-provider abstraction)
  - Supports: OpenAI, Azure OpenAI, OpenAI-compatible local endpoints
- Auth: API key (stored in database via Settings UI)
- Connection: `TelegramGroupsAdmin.Core.Services.AI.SemanticKernelChatService`
  - Feature-based routing (AIFeatureType enum)
  - Kernel cache per model/endpoint combination
  - Error handling: Returns null if feature unconfigured
- Usage:
  - Image spam detection: `OpenAI-Vision` feature
  - Prompt builder: `PromptGeneration` feature
  - Exam criteria generation: `ExamCriteriaBuilding` feature

**VirusTotal API:**
- Service: VirusTotal File Scanner API (www.virustotal.com/api/v3/)
- What it's used for: File reputation scanning, malware detection
- SDK/Client: HttpClient (named "VirusTotal" with custom handler)
- Auth: API key header (`x-apikey`)
- Connection: `TelegramGroupsAdmin.ContentDetection.Services.VirusTotalScannerService`
- Implementation details:
  - Rate limiting: 4 requests/minute (free tier)
  - Polly resilience policy with sliding window rate limiter
  - Queue limit: 10 requests to handle burst during upload + polling
  - File size limit: 20MB (Telegram limit enforced in FileScanJob)
  - Polling: Async job status check for scan results
- Files: `TelegramGroupsAdmin.ServiceCollectionExtensions` (HTTP client setup)

**ClamAV Antivirus:**
- Service: ClamAV daemon (local network service)
- What it's used for: Local file malware scanning (primary scanner before VirusTotal)
- SDK/Client: nClam 9.0.0 NuGet package
- Connection: `TelegramGroupsAdmin.ContentDetection.Services.ClamAVScannerService`
- Implementation:
  - Validates file size <20MB
  - Streaming architecture: passes file paths (not loaded to memory)
  - Configuration: `CLAMD_CONF_StreamMaxLength: 2000M` in Docker
  - Network: localhost via clam socket
- Fallback: Returns null result if ClamAV unavailable (non-critical)

**SendGrid Email Service:**
- Service: SendGrid Email API (api.sendgrid.com)
- What it's used for: Email notifications (account verification, password reset, admin alerts)
- SDK/Client: SendGrid 9.29.3 NuGet package
- Auth: API key (stored in database via Settings UI)
- Connection: `TelegramGroupsAdmin.Services.Email.SendGridEmailService`
- Configuration:
  - Enabled: Configurable via database Settings UI
  - From address: Must be verified in SendGrid dashboard
  - From name: Customizable (default: "TelegramGroupsAdmin")
- Files: `TelegramGroupsAdmin.Configuration.SendGridOptions`

## Data Storage

**Databases:**
- PostgreSQL 18
  - Connection: Via Npgsql 10.0.1
  - Client: Entity Framework Core 10.0.0 (primary) + Dapper 2.1.72 (advanced queries)
  - EF Core Adapter: Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
  - Health checks: AspNetCore.HealthChecks.NpgSql 9.0.0 at `/healthz/ready`
  - DbContext: `AppDbContext` in `TelegramGroupsAdmin.Data`
  - Tables: ~40 core tables + 11 Quartz.NET job tables + database views

**File Storage:**
- Local filesystem (single-instance design)
  - Location: `/data` volume (configurable via `App:DataPath`)
  - Subdirectories:
    - `/data/media` - Message media (photos, documents, videos)
    - `/data/keys` - Data Protection key ring (read-only after setup)
    - `/data/ml-models` - ML.NET trained classifiers (trained on startup)
  - Media path encoding: Relative paths stored in DB, converted to absolute paths via `MediaPathUtilities`

**In-Memory Caching:**
- Microsoft.Extensions.Caching.Hybrid 10.3.0
  - Purpose: Blocklist caching (domain filters)
  - No external cache service (Redis not needed for single instance)
  - L1: In-memory + L2: database backing

**Session Storage:**
- PostgreSQL (TelegramSessionDto table)
  - Purpose: WTelegram session persistence across container restarts
  - Encryption: Built-in TelegramClient session encryption

## Authentication & Identity

**Web UI Authentication:**
- Type: Cookie-based (custom implementation)
- Mechanism: `CookieAuthenticationDefaults.AuthenticationScheme`
- Implementation: `TelegramGroupsAdmin.Services.Auth.AuthCookieService`
- Features:
  - 2FA via TOTP (Otp.NET 1.4.1)
  - Recovery codes for account lockout
  - Account lockout after failed attempts
  - 30-day session expiration with sliding window (resets on activity)
- Cookie settings:
  - Name: `TgSpam.Auth`
  - HttpOnly: true
  - Secure: Development=false, Production=true
  - SameSite: Lax

**Telegram Bot Token:**
- Auth method: Single bot token per managed chat
- Storage: Encrypted in database (Configs table)
- Loader: `ITelegramConfigLoader` (replaces legacy IOptions)
- Retrieved: Runtime via database, not env vars

**Telegram User (WTelegram) Sessions:**
- Auth flow: Phone → SMS verification → Session token
- Storage: TelegramSessionDto table (encrypted)
- Multi-user: Multiple users can authenticate for profile scanning

**API Keys (Secrets Management):**
- OpenAI, SendGrid, VirusTotal API keys
- Storage: Encrypted in database (Configs table)
- UI: Settings pages allow live configuration changes
- No env var dependency (database-first design)

## Monitoring & Observability

**Error Tracking:**
- Service: None (dedicated error tracking service not integrated)
- Approach: Logs in Seq + OpenTelemetry if configured
- Exception handling: Try-catch with logging at service layer

**Logging:**
- Framework: Serilog 10.0.0 with structured logging
- Console sink: Always enabled (formatted output)
- Seq sink (optional): If `SEQ_URL` env var configured
  - Persistence: Structured logs with full context
  - Query: Full-text search in Seq UI
- OpenTelemetry sink (optional): If `OTEL_EXPORTER_OTLP_ENDPOINT` configured
- Log levels configured per namespace (dynamic via database in ConfigService)
- Named loggers: One per service/handler for context

**Distributed Tracing:**
- Framework: OpenTelemetry 1.15.0 + OTLP exporter
- Enabled: If `OTEL_EXPORTER_OTLP_ENDPOINT` configured
- Instrumentations:
  - AspNetCore: HTTP request tracing (Blazor circuits, SignalR)
  - Http: Outbound HTTP client calls (OpenAI, VirusTotal, Telegram)
  - Runtime: GC, threads, memory metrics
  - Npgsql: Database query tracing (via Npgsql.OpenTelemetry)
- Sources: Custom `TelegramGroupsAdmin.*` ActivitySource per project
- Export: OTLP/gRPC to Aspire Dashboard or Seq

**Metrics:**
- Prometheus endpoint: `/metrics` (only if OTLP configured)
- Providers:
  - AspNetCore: Request rate, latency, active connections
  - Http: Client success/failure rates, latencies
  - Runtime: GC collections, CPU, memory, thread pool
  - Npgsql: Connection pool, command execution, bytes transferred
- Export: OpenTelemetry Prometheus exporter

**Health Checks:**
- Liveness: `/healthz/live` (app responsiveness, no dependencies)
- Readiness: `/healthz/ready` (PostgreSQL connectivity check)
- Purpose: Docker healthcheck + Kubernetes probes

## CI/CD & Deployment

**Hosting:**
- Docker (single container, all-in-one)
- Supported platforms: linux/amd64, linux/arm64
- Base image: mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
  - Ubuntu Chiseled runtime (minimal, no shell)
  - Non-root user (UID 1654)
  - FFmpeg pre-installed, Tesseract OCR still in image (DEAD — cleanup needed, OCR now via OpenAI Vision)

**CI Pipeline:**
- GitHub Actions (inferred from CLAUDE.md)
- Builds: Multi-arch Docker images (BuildKit cross-compilation)
- Publishes: ghcr.io/musicislife08/telegramgroupsadmin:latest/semver
- Workflow: develop → PR → CI runs → merge → Docker publish

**Build Configuration:**
- Dockerfile: Multi-stage optimized build
  - Stage 1: Dependencies (FFmpeg, Tesseract [DEAD — cleanup needed], FastText model)
  - Stage 2: Tesseract environment [DEAD — cleanup needed]
  - Stage 3: NuGet restore (cached)
  - Stage 4: Build & publish (cross-compilation)
  - Stage 5: Runtime (Chiseled image)
- Build time: ~2-3 minutes (cached stages)
- Image size: ~500MB (runtime + models)

**Startup Procedures:**
- Database migrations: Automatic on first run (EF Core)
- ML model training: On every startup (3-5 seconds)
  - Can be skipped with `SKIP_ML_TRAINING=true`
  - Ensures fresh data for spam detection
- Quartz.NET initialization: Loads job configs from database
- Serilog bootstrap: Defaults to console, loads DB config after startup

**Special Startup Modes:**
- `--migrate-only`: Run migrations and exit (health checks)
- `--backup [path] --passphrase [key]`: Create encrypted backup
- `--restore [path] --passphrase [key]`: Restore encrypted backup (wipes data)

## Environment Configuration

**Required Environment Variables:**
- `ConnectionStrings:PostgreSQL` - Database connection (CRITICAL)

**Optional Environment Variables (Observability):**
- `SEQ_URL` - Seq server (e.g., `http://seq:5341`)
- `SEQ_API_KEY` - Seq authentication (optional)
- `OTEL_EXPORTER_OTLP_ENDPOINT` - OpenTelemetry collector
- `OTEL_SERVICE_NAME` - Service name for traces (default: TelegramGroupsAdmin)
- `TZ` - Timezone (default: UTC)

**Optional Feature Flags:**
- `SKIP_ML_TRAINING` - Skip ML training on startup (tests: `true`)
- `TESSDATA_PREFIX` - DEAD — Tesseract data path, no longer used (OCR now via OpenAI Vision, cleanup needed)

**Database Configuration:**
- Host: Configurable
- Port: 5432 (PostgreSQL default)
- Database: telegram_groups_admin
- User: tgadmin
- Password: Included in connection string

**No Configuration Files:**
- Not using appsettings*.json (security risk per CLAUDE.md)
- All config: Env vars + database
- Settings pages: Live UI-based configuration

## Webhooks & Callbacks

**Incoming:**
- Telegram Bot API: Long polling (TelegramBotPollingHost)
  - No webhook URL required (polling-based)
  - Polling interval: 30s default (Quartz.NET BackgroundJobs project)
  - Update types: messages, edits, callbacks, reports

**Outgoing:**
- Report action callbacks: DM notification with inline buttons
  - Storage: ReportCallbackContextDto table
  - Callbacks: Approve/Reject/Skip moderation actions
- No external webhooks configured (single-instance architecture)

**Message Processing Flow:**
1. TelegramBotPollingHost polls api.telegram.org
2. Updates routed to ContentDetectionOrchestrator
3. Handlers process: media, text, spam detection
4. Results stored in database
5. Notifications sent: Telegram DM, Email, Web Push
6. Background jobs: File scanning, training, cleanup

---

*Integration audit: 2026-03-15*
