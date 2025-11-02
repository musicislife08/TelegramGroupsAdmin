# TelegramGroupsAdmin

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
   git clone https://github.com/weekenders/TelegramGroupsAdmin.git
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

6. **Access web UI**
   - Navigate to: <http://localhost:8080>
   - Create first user (automatically becomes Owner)
   - Add bot to Telegram groups
   - Configure spam detection in Settings

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
│  ├─ TelegramAdminBotService         │ ← Telegram polling (singleton)
│  ├─ Blazor Server UI                │
│  ├─ API Endpoints                   │
│  └─ TickerQ Background Jobs         │
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

- **TelegramGroupsAdmin** - Main app, Blazor UI, API endpoints, TickerQ jobs
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
git clone https://github.com/weekenders/TelegramGroupsAdmin.git
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

---

## Acknowledgments

### Special Thanks

- **[tg-spam](https://github.com/umputun/tg-spam)** by [@umputun](https://github.com/umputun) - The primary inspiration for this project's anti-spam system. Many of the spam detection algorithms, patterns, and approaches are derived from or inspired by tg-spam's excellent implementation. Highly recommended for single-group deployments!

### Technology & Services

- [Telegram Bot API](https://core.telegram.org/bots/api)
- [MudBlazor](https://mudblazor.com/) - Material Design components for Blazor
- [TickerQ](https://github.com/Salgat/TickerQ) - PostgreSQL-based background jobs
- [OpenAI](https://platform.openai.com/) - GPT-4 and Vision API
- [VirusTotal](https://www.virustotal.com/) - File threat intelligence
- [CAS.chat](https://cas.chat/) - Spam user database

---

## Support

- **Issues:** [GitHub Issues](https://github.com/weekenders/TelegramGroupsAdmin/issues)
- **Discussions:** [GitHub Discussions](https://github.com/weekenders/TelegramGroupsAdmin/discussions)

---

Built with love by Weekenders
