# Technology Stack

**Analysis Date:** 2026-03-15

## Languages

**Primary:**
- C# 14 - Main application language, server-side logic
- HTML/CSS/JavaScript - MudBlazor UI components (Blazor Server)

**Secondary:**
- SQL - PostgreSQL database queries (Dapper, EF Core)
- PowerShell/Bash - Deployment scripts, Docker build

## Runtime

**Environment:**
- .NET 10.0 (10.0.100) - ASP.NET Core 10.0 runtime
- Razor Components - Blazor Server with interactive server rendering
- SignalR - Real-time communication for Blazor circuits

**Package Manager:**
- NuGet (Central Package Management via `Directory.Packages.props`)
- Lockfile: Not applicable (NuGet uses `.csproj` package references)

## Frameworks

**Core:**
- ASP.NET Core 10.0 - Web framework
- Blazor Server 10.0 - Server-side UI rendering with SignalR
- Entity Framework Core 10.0 - ORM for data access
- MudBlazor 9.1.0 - Blazor component library (Material Design)

**AI/ML:**
- Microsoft Semantic Kernel 1.73.0 - LLM orchestration framework
  - Supports OpenAI, Azure OpenAI, OpenAI-compatible endpoints
  - Multi-provider connector pattern with feature-based routing
- Microsoft.ML 5.0.0 - ML.NET text classification for spam detection
- Panlingo.LanguageIdentification.FastText 0.7.2 - FastText language identification (176 languages)

**Testing:**
- NUnit 4.5.1 - Unit test framework
- NSubstitute 5.3.0 - Mocking and test doubles
- Testcontainers.PostgreSQL 4.10.0 - PostgreSQL container for integration tests
- Microsoft.Playwright 1.58.0 + NUnit plugin - E2E browser testing

**Build/Dev:**
- Quartz.NET 3.16.1 - Background job scheduler
  - Persistent job store in PostgreSQL
  - AspNetCore integration with Dashboard UI
- AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL 0.5.1 - Quartz schema management

**Observability:**
- Serilog 10.0.0 - Structured logging framework
  - Serilog.Sinks.Console - Console output
  - Serilog.Sinks.Seq 9.0.0 - Structured log persistence (optional)
  - Serilog.Sinks.OpenTelemetry 4.2.0 - OpenTelemetry sink
- OpenTelemetry 1.15.0 - Distributed tracing and metrics
  - OpenTelemetry.Instrumentation.AspNetCore - HTTP request tracing
  - OpenTelemetry.Instrumentation.Http - HTTP client tracing
  - OpenTelemetry.Instrumentation.Runtime - Runtime metrics (GC, threads, memory)
  - OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0 - OTLP exporter
  - OpenTelemetry.Exporter.Prometheus.AspNetCore 1.11.0-beta.1 - Prometheus metrics endpoint

## Key Dependencies

**Critical:**
- Npgsql 10.0.1 - PostgreSQL data provider
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - EF Core adapter
  - Npgsql.OpenTelemetry 10.0.1 - Automatic database query tracing
  - AspNetCore.HealthChecks.NpgSql 9.0.0 - Database health checks
- Telegram.Bot 22.9.5.3 - Telegram Bot API client (official)
- WTelegramClient 4.4.2 - Telegram User API client (TDLib alternative)

**Infrastructure:**
- Dapper 2.1.72 - Micro-ORM for PostgreSQL advanced features (UPSERT, etc.)
- Microsoft.Extensions.Caching.Hybrid 10.3.0 - Hybrid in-memory + distributed caching
- Polly + Microsoft.Extensions.Http.Resilience 10.3.0 - Rate limiting and resilience policies
- HumanCron 0.5.0 + HumanCron.Quartz 0.5.0 - Human-readable cron expression parser

**Content Detection:**
- SixLabors.ImageSharp 3.1.12 - Image processing and analysis
- nClam 9.0.0 - ClamAV antivirus client (local file scanning)
- Panlingo.LanguageIdentification.FastText 0.7.2 - Language detection
- System.IO.Hashing 10.0.3 - Hash computation (SimHash)

**Utilities:**
- SendGrid 9.29.3 - Email service SDK
- Otp.NET 1.4.1 - TOTP/2FA code generation
- QRCoder 1.7.0 - QR code generation
- TimeZoneConverter 7.2.0 - Cross-platform timezone handling
- Humanizer 3.0.10 - Human-readable string formatting
- Markdig 1.1.1 + Markdown.ColorCode 3.0.1 - Markdown parsing with syntax highlighting
- AngleSharp 1.4.0 - HTML parsing (SEO preview scraping)
- DiffPlex 1.9.0 - Text diff computation
- Lib.Net.Http.WebPush 3.3.1 - Web Push notification client (VAPID)
- CsvHelper 33.1.0 - CSV export/import
- ByteSize 2.1.2 - Human-readable byte size formatting

## Configuration

**Environment Variables (Optional - observability only):**
- `SEQ_URL` - Seq server URL for structured log persistence (e.g., `http://seq:5341`)
- `SEQ_API_KEY` - Seq API key for authentication (optional)
- `OTEL_EXPORTER_OTLP_ENDPOINT` - OpenTelemetry collector endpoint (e.g., Aspire Dashboard)
- `OTEL_SERVICE_NAME` - Service identifier for traces/metrics (default: `TelegramGroupsAdmin`)
- `SKIP_ML_TRAINING` - Skip ML classifier training on startup (tests only: `true` or unset)
- `TESSDATA_PREFIX` - Path to Tesseract language data files (default: `/tessdata`)
- `TZ` - Timezone (default: `UTC`)

**Database Configuration:**
- `ConnectionStrings:PostgreSQL` - PostgreSQL connection string
  - Format: `Server=localhost;Port=5432;Database=telegram_groups_admin;User Id=tgadmin;Password=...`
  - Required for migrations and EF Core

**Application Configuration:**
- `App:DataPath` - Persistent data directory (default: `/data`)
  - `/data/media` - Message media files
  - `/data/keys` - Data Protection keys
  - `/data/ml-models` - ML.NET trained classifiers
  - Mounted as volume in Docker

**All Service Credentials (Database-First):**
- Telegram Bot Token
- OpenAI API Key
- SendGrid API Key
- VirusTotal API Key
- These are configured via Settings UI in the application (no env vars required)

**Build:**
- `Directory.Packages.props` - Central NuGet version management at repo root
- `global.json` - .NET SDK version pinning (10.0.100)

## Platform Requirements

**Development:**
- .NET 10.0 SDK (10.0.100+)
- PostgreSQL 18 (minimum: 14)
- Docker/Docker Compose (for containerized dev environment)
- Rider/Visual Studio 2022+ or VS Code with C# extensions

**Production:**
- Docker container (multi-arch: linux/amd64, linux/arm64)
- PostgreSQL 18 database
- ClamAV daemon (optional - for file scanning)
- Reverse proxy with HTTPS (NGINX, Caddy)
- 512MB+ RAM, 2GB+ storage (varies by message volume)

**External Services (Optional):**
- OpenAI API (gpt-4o-mini or gpt-4o for AI features)
- SendGrid (email notifications)
- VirusTotal (file reputation scanning)
- Seq (structured log storage and visualization)
- OpenTelemetry collector (traces/metrics)

---

*Stack analysis: 2026-03-15*
