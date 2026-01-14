# TelegramGroupsAdmin

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)

**AI-powered Telegram group moderation with advanced spam detection, content filtering, and comprehensive analytics.**

A self-hosted Blazor Server application designed for homelab deployment, combining 9 spam detection algorithms, AI-powered content analysis, file scanning, and real-time moderation tools.

> **Inspired by [tg-spam](https://github.com/umputun/tg-spam)** - This project's anti-spam system is heavily based on the excellent work by [@umputun](https://github.com/umputun). If you're managing a single Telegram group, tg-spam is a fantastic lightweight alternative. TelegramGroupsAdmin extends the concept to multi-chat management with additional features like user management, analytics, and a comprehensive web UI.

---

## Features

### Spam Detection & Content Filtering

- **9-Algorithm Spam Detection** - Stopwords, CAS.chat, Similarity/TF-IDF, Naive Bayes, Multi-language, Spacing analysis, OpenAI GPT-4, Threat Intelligence, Image/Vision
- **Self-Learning System** - Training from spam/ham samples with quality control
- **AI-Powered Image Analysis** - OpenAI Vision API for image spam detection
- **URL Filtering** - Blocklists, domain filters, phishing detection
- **File Scanning** - ClamAV + VirusTotal integration with automatic quarantine
- **Impersonation Detection** - Photo hash comparison and username similarity detection
- **Edit Detection** - Re-scans edited messages for spam content

### Moderation Tools

- **Real-time Message Monitoring** - Infinite scroll interface for browsing message history
- **User Management** - Temp bans, permanent bans, cross-chat enforcement
- **Welcome System** - Auto-kick users who don't send first message within timeout
- **Spam Reports** - Borderline cases flagged for admin review
- **Audit Logging** - Complete action history tracking

### Multi-Chat Support

- **Automatic Discovery** - Bot auto-detects all groups it's added to
- **Chat-Scoped Permissions** - Admins see only their chats, GlobalAdmin/Owner see all
- **Invite System** - Secure invite links with permission inheritance

### Analytics & Insights

- **Message Trends** - Volume over time, spam/ham ratios
- **User Activity** - Top contributors, join/leave patterns
- **Spam Statistics** - Detection accuracy, algorithm performance
- **Export Capabilities** - CSV/JSON data exports

### AI-Powered Features

- **Prompt Builder** - Meta-AI tool to generate/improve custom spam detection prompts
- **Multi-Language Translation** - Automatic translation for non-Latin script messages
- **Confidence Aggregation** - Weighted scoring across all detection algorithms

### Security & Privacy

- **TOTP 2FA** - Mandatory for all accounts
- **Email Verification** - Required for new accounts
- **Encrypted API Keys** - Secure storage for sensitive configurations
- **Permission Hierarchy** - Admin (chat-scoped) → GlobalAdmin → Owner
- **Backup/Restore** - Encrypted backups with passphrase protection

### Notifications

- **DM Delivery System** - Queued notifications delivered when users enable bot DMs
- **Auto-delivery on /start** - Receive pending notifications when starting the bot
- **Spam Alerts** - Consolidated notifications with media support

---

## Tech Stack

**Core:**

- .NET web application with PostgreSQL database
- Docker containerized deployment

**Required Services (API keys needed):**

- **OpenAI** - GPT-4 for spam detection and Vision API for image analysis
- **VirusTotal** - File threat intelligence
- **SendGrid** - Email verification and notifications
- **CAS.chat** - Spam user database

**Security:**

- ClamAV for real-time virus scanning

---

## Quick Start

### Prerequisites

- Docker & Docker Compose
- API Keys:
  - [Telegram Bot Token](https://t.me/BotFather)
  - [OpenAI API Key](https://platform.openai.com/api-keys)
  - [VirusTotal API Key](https://www.virustotal.com/gui/my-apikey)
  - [CAS.chat API Key](https://cas.chat/)
  - [SendGrid API Key](https://app.sendgrid.com/settings/api_keys)

### Installation

1. **Clone the repository**

   ```bash
   git clone https://github.com/musicislife08/TelegramGroupsAdmin.git
   cd TelegramGroupsAdmin
   ```

2. **Choose deployment mode**

   **Production (pre-built image):**

   ```bash
   cp examples/compose.production.yml compose.yml
   ```

   **Development (build from source):**

   ```bash
   cp examples/compose.development.yml compose.yml
   ```

3. **Configure environment variables**

   ```bash
   nano compose.yml
   ```

   Replace all `CHANGE_ME` values with your actual API keys and passwords.

4. **Start services**

   ```bash
   docker compose up -d
   ```

5. **Check logs**

   ```bash
   docker compose logs -f app
   ```

6. **Verify health**

   ```bash
   curl http://localhost:8080/healthz/live   # Liveness check
   curl http://localhost:8080/healthz/ready  # Readiness check (includes DB)
   ```

7. **Access web UI and configure**
   - Navigate to: <http://localhost:8080>
   - Create first user (automatically becomes Owner)
   - Configure bot token at **Settings → Telegram → Bot Configuration**
   - Configure API keys at **Settings → System** (OpenAI, Email, etc.)
   - Add bot to Telegram groups
   - Enable spam detection at **Settings → Content Detection**

### Required Configuration

See [examples/README.md](examples/README.md) for detailed configuration guide including:

- API key setup
- Database passwords
- Data persistence
- Volume mounts
- Security best practices

---

## Documentation

- **[CLAUDE.md](CLAUDE.md)** - Comprehensive technical reference for AI agents and developers
- **[examples/README.md](examples/README.md)** - Docker Compose setup guide
- **[BACKLOG.md](BACKLOG.md)** - Development roadmap and pending features
- **[WTELEGRAM_INTEGRATION.md](WTELEGRAM_INTEGRATION.md)** - WTelegramClient integration notes

---

## Development

### Architecture

**Single-Instance Design** - Telegram Bot API enforces one active connection per bot token. All services (bot polling, web UI, background jobs) run in a single container by design.

```text
┌─────────────────────────────────────┐
│  TelegramGroupsAdmin Container      │
│  ├─ TelegramBotPollingHost          │ ← Telegram polling (singleton)
│  ├─ Blazor Server UI                │
│  ├─ API Endpoints                   │
│  └─ Quartz.NET Background Jobs      │
└─────────────────────────────────────┘
         ↓
    PostgreSQL
         ↓
    /data volume (media, keys)
```

**Design Philosophy:** Optimized for homelab deployment - operational simplicity over horizontal scalability. Handles 1000+ messages/day on single instance.

**Performance Benchmarks:**

- Spam detection: 255ms average, 821ms P95 (9 algorithms + OpenAI)
- Analytics queries: <100ms
- Message page load: 50+ messages without lag

**Project Structure:**

- **TelegramGroupsAdmin** - Main app, Blazor UI, API endpoints, Quartz.NET jobs
- **TelegramGroupsAdmin.Configuration** - IOptions configuration classes
- **TelegramGroupsAdmin.Data** - EF Core DbContext, migrations, Data Protection
- **TelegramGroupsAdmin.Telegram** - Bot services, bot commands, repositories
- **TelegramGroupsAdmin.Telegram.Abstractions** - TelegramBotClientFactory, job payloads
- **TelegramGroupsAdmin.SpamDetection** - 9 spam detection algorithms
- **TelegramGroupsAdmin.ContentDetection** - URL filtering, impersonation, file scanning
- **TelegramGroupsAdmin.Tests** - Migration tests (Testcontainers.PostgreSQL)

### Building from Source

```bash
# Clone repository
git clone https://github.com/musicislife08/TelegramGroupsAdmin.git
cd TelegramGroupsAdmin

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run migrations only (no bot instance)
dotnet run --project TelegramGroupsAdmin --migrate-only

# Run tests
dotnet test
```

### Testing

**CRITICAL:** Never run the app in normal mode during development - only one instance allowed (Telegram singleton constraint). Always use `--migrate-only` flag for validation.

**Migration Tests:**

```bash
dotnet test TelegramGroupsAdmin.Tests
```

Passing tests via Testcontainers.PostgreSQL, validates all migrations against real PostgreSQL.

### EF Core Migrations

```bash
# Add new migration
dotnet ef migrations add MigrationName --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin

# Validate without running bot
dotnet run --project TelegramGroupsAdmin --migrate-only
```

---

## Troubleshooting

### Container Health Checks

#### Check Service Status
```bash
docker compose ps

# Expected output:
# NAME                              STATUS
# telegram-groups-admin-app         Up (healthy)
# telegram-groups-admin-clamav      Up (healthy)
# telegram-groups-admin-db          Up (healthy)
```

#### Check Application Health
```bash
# Liveness check (is app alive?)
curl http://localhost:8080/healthz/live

# Readiness check (is app ready? includes DB connection)
curl http://localhost:8080/healthz/ready
```

### Common Issues

#### ClamAV Connection Issues

**Symptom:** Logs show "ClamAV connection failed" or health check failing
```
Error: Failed to connect to ClamAV at clamav:3310
```

**Diagnosis:**
```bash
# Check if ClamAV container is running
docker compose ps clamav

# Check ClamAV logs
docker compose logs clamav

# Test ClamAV connectivity from app container
docker compose exec app nc -zv clamav 3310
```

**Solution:**
1. **First-time startup**: ClamAV needs 5-10 minutes to download virus signatures (~200MB)
   ```bash
   # Wait and monitor progress
   docker compose logs -f clamav
   # Look for: "ClamAV update process started" and "Database updated"
   ```

2. **After signatures downloaded**: Verify ClamAV is listening
   ```bash
   docker compose exec clamav clamdscan --ping 1
   # Expected: "PONG"
   ```

3. **Still failing**: Restart ClamAV container
   ```bash
   docker compose restart clamav
   ```

#### Bot Not Responding in Telegram

**Symptom:** Bot appears offline or doesn't respond to commands in Telegram groups

**Diagnosis:**
```bash
# Check app logs for bot connection errors
docker compose logs app | grep -i "telegram\|bot"

# Look for:
# - "Successfully connected to Telegram"
# - "Bot token validation failed"
# - "401 Unauthorized"
```

**Solution:**
1. **Bot token not configured**: Configure in Settings UI
   - Navigate to: **Settings → Telegram → Bot Configuration → General**
   - Get token from: [@BotFather](https://t.me/BotFather)
   - Save and restart application

2. **Invalid token**: Verify token format
   - Format: `1234567890:ABCdefGHIjklMNOpqrsTUVwxyz`
   - No spaces or extra characters

3. **Bot privacy mode**: Disable privacy mode in @BotFather
   ```
   /setprivacy → Select your bot → Disable
   ```

4. **Bot not added to group**: Ensure bot is a member with admin privileges

#### Database Connection Problems

**Symptom:** App crashes on startup with database errors
```
Error: Connection refused (postgres:5432)
Npgsql.NpgsqlException: Failed to connect to postgres:5432
```

**Diagnosis:**
```bash
# Check if PostgreSQL is running
docker compose ps postgres

# Check PostgreSQL logs
docker compose logs postgres

# Test PostgreSQL connectivity
docker compose exec postgres pg_isready -U tgadmin -d telegram_groups_admin
```

**Solution:**
1. **Password mismatch**: Verify passwords match in `compose.yml`
   ```yaml
   # These must be IDENTICAL:
   POSTGRES_PASSWORD: "your-strong-password"
   ConnectionStrings__PostgreSQL: "...Password=your-strong-password"
   ```

2. **PostgreSQL not ready**: Wait for health check to pass
   ```bash
   # Health check takes ~10-30 seconds
   docker compose logs -f postgres
   # Look for: "database system is ready to accept connections"
   ```

3. **Port conflict**: Check if port 5432 is already in use
   ```bash
   lsof -i :5432  # macOS/Linux
   netstat -ano | findstr :5432  # Windows

   # Solution: Change port in compose.yml
   ports:
     - "5433:5432"  # Use different host port
   ```

#### Permission Errors on /data Volume

**Symptom:** App crashes with "Permission denied" errors
```
System.UnauthorizedAccessException: Access to the path '/data/keys' is denied
```

**Diagnosis:**
```bash
# Check volume mount permissions
ls -la ./data

# Check app container user
docker compose exec app id
# Expected: uid=1654 gid=1654
```

**Solution:**
1. **Fix ownership**: Ensure host directory is writable
   ```bash
   # Option 1: Grant write permissions
   chmod -R 755 ./data

   # Option 2: Match container UID/GID
   sudo chown -R 1654:1654 ./data
   ```

2. **SELinux issues** (RHEL/CentOS/Fedora):
   ```bash
   # Add SELinux label
   chcon -Rt svirt_sandbox_file_t ./data
   ```

#### Rate Limiting Errors

**Symptom:** Logs show HTTP 429 errors from external APIs
```
OpenAI API: Rate limit exceeded
VirusTotal API: Too many requests
```

**Diagnosis:**
```bash
# Check logs for rate limit patterns
docker compose logs app | grep -i "rate limit\|429\|quota"
```

**Solution:**
1. **OpenAI rate limits**:
   - Reduce concurrent spam checks in **Settings → Content Detection → Spam Detection**
   - Consider upgrading OpenAI tier at https://platform.openai.com/settings/organization/billing
   - Disable image spam detection temporarily

2. **VirusTotal rate limits** (Free tier: 500 requests/day):
   - Reduce file scanning frequency
   - Upgrade to premium API key
   - Files >2GB skip VirusTotal automatically

3. **Telegram rate limits**:
   - Bot API has built-in rate limiting (30 messages/second per chat)
   - App automatically retries with exponential backoff
   - Check for loops or excessive message sending

#### Application Logs

**View real-time logs:**
```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f app
docker compose logs -f postgres
docker compose logs -f clamav

# Filter for errors
docker compose logs app | grep -i error

# Filter for specific feature
docker compose logs app | grep -i "spam\|detection"
```

**Log rotation:** Logs automatically rotate at 10MB (keeps 15 files = 150MB total)

#### Database Migrations Failing

**Symptom:** App crashes on startup with migration errors
```
Error applying migration '20250108123456_MigrationName'
```

**Diagnosis:**
```bash
# Check app logs for specific migration error
docker compose logs app | grep -i migration

# Connect to database to inspect
docker compose exec postgres psql -U tgadmin -d telegram_groups_admin
# \dt  -- List tables
# SELECT * FROM __EFMigrationsHistory;  -- Check applied migrations
# \q   -- Exit
```

**Solution:**
1. **Corrupted migration**: Restore from backup
   ```bash
   # Stop app
   docker compose stop app

   # Restore backup (if available)
   docker compose exec postgres pg_restore -U tgadmin -d telegram_groups_admin /backups/backup.sql

   # Restart app
   docker compose start app
   ```

2. **Clean database** (⚠️ DELETES ALL DATA):
   ```bash
   docker compose down
   rm -rf ./data/postgres/*
   docker compose up -d
   ```

#### Upgrading PostgreSQL (17 to 18)

**Symptom:** After updating to a newer image, PostgreSQL 18 fails to start with:
```
Error: in 18+, these Docker images are configured to store database data in a
       format which is compatible with "pg_ctlcluster"...
```

**Cause:** PostgreSQL 18's Docker image changed the data directory structure. It now uses version-specific subdirectories (`/var/lib/postgresql/18/data` instead of `/var/lib/postgresql/data`).

**Solution:** Use the automated upgrade script:
```bash
# From your deployment directory (where compose.yml is located)
./scripts/upgrade-postgres-17-to-18.sh

# Or skip confirmation prompt
./scripts/upgrade-postgres-17-to-18.sh -y
```

**What the script does:**
1. Backs up your database using `pg_dumpall`
2. Stops the PostgreSQL container
3. Updates compose.yml (image version + volume mount path)
4. Moves old data directory (preserved as backup)
5. Starts PostgreSQL 18 with fresh data directory
6. Restores your database from backup
7. Verifies the upgrade succeeded

**After successful upgrade**, you can clean up:
```bash
rm pg17_backup_*.sql        # SQL backup (keep if paranoid)
rm compose.yml.backup       # Old compose file
rm -rf ./data/postgres_pg17_*  # Old data directory
```

#### Build Failures (Development Mode)

**Symptom:** Build fails during `docker compose up --build`
```
Error: Cannot find project file
Error: Restore failed
```

**Diagnosis:**
```bash
# Check build context
docker compose config | grep context
# Expected: context: ..
```

**Solution:**
1. **Wrong compose file**: Ensure using `compose.development.yml`
   ```bash
   cp examples/compose.development.yml compose.yml
   ```

2. **Build context incorrect**: Verify context points to repository root
   ```yaml
   # In compose.yml:
   build:
     context: ..  # Parent directory (repository root)
     dockerfile: TelegramGroupsAdmin/Dockerfile
   ```

3. **Clean build**:
   ```bash
   docker compose build --no-cache app
   docker compose up -d app
   ```

### Getting Help

If you're still experiencing issues:

1. **Enable debug logging**: Set `ASPNETCORE_ENVIRONMENT=Development` in compose.yml (⚠️ verbose logs)
2. **Check diagnostics**: Review logs with `docker compose logs app --tail=500`
3. **Verify configuration**: Ensure all CHANGE_ME values replaced in compose.yml
4. **Review documentation**: See [examples/README.md](examples/README.md) for detailed setup
5. **Report issue**: Create [GitHub Issue](https://github.com/musicislife08/TelegramGroupsAdmin/issues) with:
   - Docker Compose logs (`docker compose logs`)
   - System info (`docker version`, `docker compose version`)
   - Steps to reproduce

---

## Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes following existing code patterns
4. Ensure 0 errors, 0 warnings (`dotnet build`)
5. Run tests (`dotnet test`)
6. Commit with descriptive messages
7. Push to your branch
8. Open a Pull Request

For detailed architecture patterns, coding standards, and critical rules, see [CLAUDE.md](CLAUDE.md).

---

## Roadmap

See [BACKLOG.md](BACKLOG.md) for complete list of planned features including:

- Notification preferences UI
- Advanced analytics dashboards
- Webhook support
- Multi-bot management
- Custom rule engine

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Third-Party Components

TelegramGroupsAdmin includes and depends on various open-source libraries and tools. See [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md) for complete licensing information and attributions for:

- **Bundled Components:** FFmpeg (LGPL 2.1+), Tesseract OCR (Apache 2.0), EFF Wordlist (CC/Public Domain)
- **Major Dependencies:** Telegram.Bot, MudBlazor, Entity Framework Core, Quartz.NET, and more
- **External Services:** OpenAI, VirusTotal, CAS.chat, SendGrid

---

## Acknowledgments

### Special Thanks

- **[tg-spam](https://github.com/umputun/tg-spam)** by [@umputun](https://github.com/umputun) - The primary inspiration for this project's anti-spam system. Many of the spam detection algorithms, patterns, and approaches are derived from or inspired by tg-spam's excellent implementation. Highly recommended for single-group deployments!

### Technology & Services

- [Telegram Bot API](https://core.telegram.org/bots/api)
- [MudBlazor](https://mudblazor.com/) - Material Design components for Blazor
- [Quartz.NET](https://www.quartz-scheduler.net/) - PostgreSQL-based background job scheduler
- [OpenAI](https://platform.openai.com/) - GPT-4 and Vision API
- [VirusTotal](https://www.virustotal.com/) - File threat intelligence
- [CAS.chat](https://cas.chat/) - Spam user database

---

## Support

- **Issues:** [GitHub Issues](https://github.com/musicislife08/TelegramGroupsAdmin/issues)
- **Discussions:** [GitHub Discussions](https://github.com/musicislife08/TelegramGroupsAdmin/discussions)
