# Docker Compose Examples

This directory contains example Docker Compose configurations for deploying TelegramGroupsAdmin.

## Files

### `compose.production.yml` - Pull from Docker Hub
**Use this when:** You want to deploy quickly without building from source.

- Pulls pre-built image from Docker Hub
- Faster deployment (no build step)
- Ideal for production servers
- Image size: ~200MB (Ubuntu Chiseled runtime)

**Setup:**
```bash
# 1. Copy to root directory
cp examples/compose.production.yml compose.yml

# 2. Edit compose.yml - set PostgreSQL password only
nano compose.yml

# 3. Start services
docker compose up -d

# 4. Wait for database migrations
docker compose logs -f app

# 5. Open web UI and configure API keys (see Configuration section below)
open http://localhost:8080
```

**Image location:** `your-dockerhub-username/telegramgroupsadmin:latest`
*(Update this in compose.yml when the image is published)*

---

### `compose.development.yml` - Build from Source
**Use this when:** You're developing, testing, or want to customize the code.

- Builds application from local source code
- Includes all build dependencies
- Slower first deployment (~2-5 minutes build time)
- Ideal for development and customization

**Setup:**
```bash
# 1. Copy to root directory
cp examples/compose.development.yml compose.yml

# 2. Edit compose.yml - set PostgreSQL password only
nano compose.yml

# 3. Build and start services
docker compose up -d --build

# 4. Wait for database migrations
docker compose logs -f app

# 5. Open web UI and configure API keys (see Configuration section below)
open http://localhost:8080
```

**Build context:** `../` (repository root)
**Dockerfile:** `../TelegramGroupsAdmin/Dockerfile`

---

## Key Differences

| Feature | Production | Development |
|---------|-----------|-------------|
| **Image Source** | Docker Hub (pre-built) | Local build |
| **Build Time** | None (just pull) | 2-5 minutes |
| **Deployment Speed** | ⚡ Fast | 🐢 Slower (first time) |
| **Customization** | No source changes | Full source access |
| **Image Tag** | `your-dockerhub-username/telegramgroupsadmin:latest` | `telegramgroupsadmin:local` |
| **Best For** | Production servers | Development, testing, customization |

---

## Configuration

### Step 1: Database Password (Required in compose.yml)

Set a strong PostgreSQL password in `compose.yml`:

```yaml
POSTGRES_PASSWORD: "your-strong-password-here"
ConnectionStrings__PostgreSQL: "Host=postgres;Port=5432;Database=telegram_groups_admin;Username=tgadmin;Password=your-strong-password-here"
```

**⚠️ Important:** Use the same password in both places!

### Step 2: Service Configuration (Web UI)

After starting the application, all service API keys are configured through the **Settings UI**:

#### 🎯 First Login Setup
1. Open http://localhost:8080 (or your domain)
2. Create your first admin account
3. Navigate to **Settings** in the sidebar

#### 🔑 Configure API Keys

**Settings > Infrastructure:**
- **Telegram Bot Configuration**
  - Get token from: [@BotFather](https://t.me/BotFather)
  - Format: `1234567890:ABCdefGHIjklMNOpqrsTUVwxyz`

- **OpenAI Configuration**
  - Get key from: [OpenAI Platform](https://platform.openai.com/api-keys)
  - Set model (recommended: `gpt-4o-mini`)

- **SendGrid Configuration**
  - Get key from: [SendGrid](https://app.sendgrid.com/settings/api_keys)
  - Set from email and name

**Settings > Features > Spam Detection:**
- **VirusTotal API Key** - Get from: [VirusTotal](https://www.virustotal.com/gui/my-apikey)
- **CAS API Key** - Get from: [CAS.chat](https://cas.chat/)

**✅ All API keys are encrypted and stored in the database** - no environment variables needed!

---

## Data Persistence

All data is stored in `./data/` directory (relative to compose.yml location):

```
./data/
├── postgres/     # PostgreSQL database files
├── clamav/       # ClamAV virus signatures (~200MB)
├── app/          # Application data:
│   ├── keys/     #   Data Protection encryption keys (NEVER DELETE!)
│   ├── images/   #   Downloaded message images
│   └── media/    #   Downloaded media files
└── backups/      # Database backups (from --export command)
```

**⚠️ Important:** Never delete `./data/app/keys/` - contains encryption keys!

---

## Common Commands

```bash
# Start all services
docker compose up -d

# Stop all services
docker compose down

# View logs
docker compose logs -f app
docker compose logs -f postgres
docker compose logs -f clamav

# Restart app only
docker compose restart app

# Rebuild app (development mode)
docker compose build --no-cache app
docker compose up -d app

# Check health
docker compose ps
curl http://localhost:8080/healthz/live   # Liveness check
curl http://localhost:8080/healthz/ready  # Readiness check (includes DB)

# Update to latest image (production mode)
docker compose pull app
docker compose up -d app
```

---

## Security Notes

- ✅ Application runs as non-root user (UID 1654)
- ✅ Uses Ubuntu Chiseled runtime (minimal attack surface)
- ✅ No shell or package manager in app container
- ✅ Data Protection keys persist in volume
- ✅ All API keys encrypted and stored in database (not environment variables)
- ⚠️ HTTPS should be handled by reverse proxy (Traefik, Nginx, Caddy)
- ⚠️ Never commit compose.yml with database password to git!
- ⚠️ Change default PostgreSQL password!

---

## Troubleshooting

**Problem:** ClamAV health check failing
**Solution:** Wait 5 minutes for virus signature download on first start

**Problem:** App can't connect to PostgreSQL
**Solution:** Check passwords match in both `POSTGRES_PASSWORD` and connection string

**Problem:** Bot not responding in Telegram
**Solution:** Configure bot token in Settings > Infrastructure > Telegram Bot Configuration

**Problem:** Spam detection not working
**Solution:** Configure API keys in Settings UI (OpenAI, VirusTotal, CAS)

**Problem:** Build fails with "project not found"
**Solution:** Make sure you're using development compose and context is set to `..`

**Problem:** Permission denied on /app/data
**Solution:** Check volume mount paths are relative (`./data/app` not `/data/app`)

---

## Next Steps

1. Choose production or development compose file
2. Copy to root: `cp examples/compose.*.yml compose.yml`
3. Edit `compose.yml` - set PostgreSQL password only
4. Start: `docker compose up -d`
5. Check logs: `docker compose logs -f app`
6. Access: http://localhost:8080
7. Create first user account (becomes Owner automatically)
8. Configure API keys in Settings UI (Infrastructure & Features sections)

For more information, see main repository [README.md](../README.md) and [CLAUDE.md](../CLAUDE.md) documentation.
