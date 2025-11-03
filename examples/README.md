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

# 2. Edit compose.yml with your API keys
nano compose.yml

# 3. Start services
docker compose up -d

# 4. Check logs
docker compose logs -f app
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

# 2. Edit compose.yml with your API keys
nano compose.yml

# 3. Build and start services
docker compose up -d --build

# 4. Check logs
docker compose logs -f app
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

## Required Configuration

Both files require you to configure these environment variables:

### 🔑 Required API Keys

1. **Telegram Bot Token** (`TELEGRAM__BOTTOKEN`)
   - Get from: [@BotFather](https://t.me/BotFather)
   - Format: `1234567890:ABCdefGHIjklMNOpqrsTUVwxyz`
   - The bot automatically discovers all groups it's added to

2. **OpenAI API Key** (`OPENAI__APIKEY`)
   - Get from: [OpenAI Platform](https://platform.openai.com/api-keys)
   - Format: `sk-proj-xxxxx...`

3. **VirusTotal API Key** (`VIRUSTOTAL__APIKEY`)
   - Get from: [VirusTotal](https://www.virustotal.com/gui/my-apikey)

4. **CAS API Key** (`SPAMDETECTION__APIKEY`)
   - Get from: [CAS.chat](https://cas.chat/)

5. **SendGrid API Key** (`SENDGRID__APIKEY`)
   - Get from: [SendGrid](https://app.sendgrid.com/settings/api_keys)
   - Also set `SENDGRID__FROMEMAIL` and `SENDGRID__FROMNAME`

6. **Database Password** (`POSTGRES_PASSWORD` and in connection string)
   - Change from default `CHANGE_ME_STRONG_PASSWORD`
   - Use same password in both places!

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
- ⚠️ HTTPS should be handled by reverse proxy (Traefik, Nginx, Caddy)
- ⚠️ Never commit compose.yml with real API keys to git!
- ⚠️ Change default PostgreSQL password!

---

## Troubleshooting

**Problem:** ClamAV health check failing
**Solution:** Wait 5 minutes for virus signature download on first start

**Problem:** App can't connect to PostgreSQL
**Solution:** Check passwords match in both `POSTGRES_PASSWORD` and connection string

**Problem:** "TELEGRAM__BOTTOKEN is required" error
**Solution:** Make sure all required environment variables are set (not commented out)

**Problem:** Build fails with "project not found"
**Solution:** Make sure you're using development compose and context is set to `..`

**Problem:** Permission denied on /app/data
**Solution:** Check volume mount paths are relative (`./data/app` not `/data/app`)

---

## Next Steps

1. Choose production or development compose file
2. Copy to root: `cp examples/compose.*.yml compose.yml`
3. Edit `compose.yml` with your API keys (replace all `CHANGE_ME` values)
4. Start: `docker compose up -d`
5. Check logs: `docker compose logs -f app`
6. Access: http://localhost:8080
7. Create first user (becomes Owner automatically)

For more information, see main repository [CLAUDE.md](../CLAUDE.md) documentation.
