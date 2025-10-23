# Pre-Deployment Checklist - TelegramGroupsAdmin

**Last Updated:** 2025-10-23
**Target Environment:** Production / Small Group Testing
**Status:** ✅ Ready for Testing

---

## ✅ Infrastructure Readiness

### Database Connection Resilience
- ✅ EF Core `EnableRetryOnFailure` configured (6 retries, 30s max delay)
- ✅ Both scoped and pooled DbContext have retry logic
- ✅ Transient PostgreSQL failures handled automatically

### Health Check Endpoints
- ✅ Kubernetes-style liveness probe: `/healthz/live` (no DB checks)
- ✅ Readiness probe: `/healthz/ready` (includes PostgreSQL check)
- ✅ Legacy endpoint: `/health` (for backward compatibility)
- ✅ Docker health checks use liveness probe (prevent restart on DB failures)
- ✅ Proper separation: App restarts only on app failures, not external dependency failures

### Container Security
- ✅ Ubuntu Chiseled runtime (minimal attack surface, no shell, no package manager)
- ✅ Non-root user (UID 1654)
- ✅ Multi-stage build (SDK for build, minimal runtime for production)
- ✅ wget-based health checks (smaller than curl)

### Logging Configuration
- ✅ Production-ready log levels (compose file updated):
  - Default: Information
  - Microsoft.AspNetCore: Warning
  - Microsoft.EntityFrameworkCore: Warning
  - TelegramGroupsAdmin: Information
- ✅ Docker log rotation (10MB max, 15 files, compressed)
- ✅ Structured logging with timestamps

---

## ✅ Background Services

### TickerQ Job System
- ✅ `TickerQInstanceFactory.Initialize()` called before `UseTickerQ()` (Program.cs:73)
- ✅ 5 background jobs configured:
  1. `WelcomeTimeoutJob` - New user welcome timeout enforcement
  2. `DeleteMessageJob` - Scheduled message deletion
  3. `FetchUserPhotoJob` - User profile photo fetching
  4. `TempbanExpiryJob` - Temporary ban expiry handling
  5. `FileScanJob` - Malware scanning for file attachments
- ✅ Polling interval: 5 seconds (optimized from 60s default)
- ✅ All jobs re-throw exceptions for proper retry/logging

### Background Services
- ✅ `TelegramAdminBotService` - Bot polling, update routing, command registration
- ✅ `MessageProcessingService` - Message handling, spam detection, media download
- ✅ `ChatManagementService` - Admin cache, health checks, permissions validation
- ✅ `SpamActionService` - Training QC, auto-ban, borderline reports
- ✅ `CleanupBackgroundService` - Message retention enforcement

---

## ✅ External Service Error Handling

### Fail-Open Pattern (All Content Checks)
- ✅ All spam/malware checks fail open (return Clean on error)
- ✅ `ContentCheckHelpers.CreateFailureResponse()` standardizes error handling
- ✅ Prevents false positives when services unavailable

### ClamAV Integration
- ✅ Connection resilience (retries on daemon restart)
- ✅ Graceful degradation when daemon unavailable
- ✅ File size limit: 2GB (ClamAV hard limit)
- ✅ Timeout: 5 minutes max scan time

### VirusTotal Integration
- ✅ Polly PartitionedRateLimiter (4 requests/minute)
- ✅ Daily quota tracking (500 lookups/day for free tier)
- ✅ Graceful quota exhaustion handling
- ✅ Fail-open on rate limit or errors

### OpenAI API
- ✅ 429 detection and retry logic
- ✅ Fail-open on API errors
- ✅ Configurable model and token limits
- ✅ Vision API for image spam detection

### SendGrid Email
- ✅ Error handling for email delivery failures
- ✅ Email verification system (24h tokens)
- ✅ Resend verification endpoint

### CAS (Combot Anti-Spam)
- ✅ API error handling
- ✅ Fail-open on service unavailability

---

## ✅ Bot Permissions & Health Checks

### Required Bot Permissions (Per Chat)
The bot MUST be an **Administrator** with these permissions:
- ✅ **Delete Messages** (`can_delete_messages`) - Critical for spam removal
- ✅ **Ban Users** (`can_restrict_members`) - Critical for spam user bans
- ✅ **Invite Users** (`can_invite_users`) - For invite link management
- ⚠️ **Promote Members** (`can_promote_members`) - Optional, for admin cache sync

### Health Check Validation
ChatManagementService validates permissions every minute:
- ✅ Bot admin status check
- ✅ Permission validation (delete, restrict, invite)
- ✅ Warnings logged if permissions missing
- ✅ Health status cached and exposed via UI
- ✅ Invite link validation (private groups)

### Admin Cache
- ✅ Refreshed on startup for all managed chats
- ✅ Real-time updates via ChatMember events
- ✅ Used for permission checks in commands
- ✅ Syncs with Telegram every health check cycle

---

## 🔧 Configuration Checklist

### Required Environment Variables
```bash
# Database
ConnectionStrings__PostgreSQL="Host=postgres;Port=5432;Database=telegram_groups_admin;Username=tgadmin;Password=CHANGE_ME"

# Telegram Bot
TELEGRAM__BOTTOKEN="1234567890:ABCdefGHIjklMNOpqrsTUVwxyz-1234567890"

# OpenAI API
OPENAI__APIKEY="sk-proj-..."
OPENAI__MODEL="gpt-4o-mini"
OPENAI__MAXTOKENS="150"

# VirusTotal
VIRUSTOTAL__APIKEY="..."

# CAS (Combot Anti-Spam)
SPAMDETECTION__APIKEY="..."

# SendGrid
SENDGRID__APIKEY="SG...."
SENDGRID__FROMEMAIL="noreply@yourdomain.com"
SENDGRID__FROMNAME="Telegram Groups Admin"

# ClamAV
CLAMAV__HOST="clamav"
CLAMAV__PORT="3310"
```

### Optional Environment Variables
```bash
# Application
APP__BASEURL="http://localhost:8080"  # Change to your domain
APP__DATAPATH="/data"

# Message History
MESSAGEHISTORY__ENABLED="true"
MESSAGEHISTORY__RETENTIONHOURS="720"  # 30 days

# Timezone
TZ="America/Phoenix"
```

---

## 📋 Pre-Testing Verification

### Before Adding Bot to Groups

1. **Environment Setup**
   - [ ] All required API keys configured
   - [ ] PostgreSQL database accessible
   - [ ] ClamAV daemon running and responsive
   - [ ] Data directories writable (`/data/keys`, `/data/media`)

2. **Initial Application Test**
   ```bash
   docker compose up -d
   docker compose logs -f app
   ```
   - [ ] Migrations run successfully
   - [ ] TelegramAdminBotService starts polling
   - [ ] TickerQ shows "5 Active Functions" in logs
   - [ ] Health checks passing (`/healthz/live`, `/healthz/ready`)

3. **Web UI Verification**
   - [ ] Access http://localhost:8080
   - [ ] Register first user (auto-promoted to Owner)
   - [ ] Email verification works
   - [ ] TOTP setup works
   - [ ] Login successful

4. **Bot Commands Test** (DM to bot)
   ```
   /start    - Bot responds, DM enabled
   /help     - Shows command list
   /status   - Shows bot status
   ```

### Adding Bot to Test Groups

1. **Bot Setup in Telegram**
   - [ ] Add bot to small test group (< 50 members)
   - [ ] Promote bot to Administrator
   - [ ] Enable ALL admin permissions:
     - [x] Delete messages
     - [x] Ban users
     - [x] Invite users via link
     - [x] Promote members (optional)
   - [ ] Verify bot responds to `/ping` in group

2. **Permission Validation**
   ```bash
   docker compose logs app | grep "Health check"
   ```
   - [ ] Health status shows "Healthy"
   - [ ] No warnings about missing permissions
   - [ ] Admin cache refreshed successfully

3. **Welcome System Test**
   - [ ] Configure welcome message in Settings UI
   - [ ] Add test user to group
   - [ ] Verify user restricted on join
   - [ ] Verify welcome message with button
   - [ ] Click "Accept Rules" → user unrestricted

4. **Spam Detection Test**
   - [ ] Send normal message → passes
   - [ ] Send test spam message → detected (if trained)
   - [ ] Send URL → blocklist check works
   - [ ] Upload image → Vision API check works

5. **File Scanning Test**
   - [ ] Upload EICAR test file → detected and deleted
   - [ ] Upload clean file → passes scan
   - [ ] Check audit log for file scan results
   - [ ] Verify DM notification sent to user

---

## ⚠️ Known Limitations & Gotchas

### TickerQ Dashboard
- ⚠️ Only available in Development mode (disabled in Production)
- Access: http://localhost:8080/tickerq-dashboard (dev only)
- Shows job history, active functions, failed jobs

### ClamAV First Start
- ⚠️ First startup takes ~5 minutes to download signatures (~200MB)
- Health checks allow up to 5 minutes of retries
- Subsequent starts: ~30-60 seconds

### VirusTotal Quotas
- ⚠️ Free tier: 500 lookups/day, 4 requests/minute
- Rate limit enforced by Polly
- Quota exhaustion logged as Warning, fails open

### Message History
- ⚠️ Default retention: 30 days (720 hours)
- Cleanup runs in background
- Spam/ham samples preserved for training

### Bot Privacy Mode
- ⚠️ Must be DISABLED for bot to see all messages
- Check with @BotFather: `/setprivacy` → Disable
- Otherwise bot only sees commands and mentions

---

## 🎯 Testing Scenarios

### Spam Detection Workflow
1. Send normal message → passes all checks
2. Send message with blocked URL → URL check triggers
3. Upload test image → Vision API scans
4. Upload EICAR file → ClamAV + VirusTotal detect, delete message
5. Verify audit log shows all detection attempts

### Welcome System Workflow
1. New user joins → immediately restricted
2. Welcome message with "Accept Rules" button appears
3. User clicks button → unrestricted
4. If timeout (default 5 min) → user banned
5. Admin can use `/tempban` for time-limited bans

### Admin Commands
```
/ban @user [reason]           - Ban user from all managed chats
/unban @user                  - Unban user from all managed chats
/tempban @user <duration>     - Temporary ban (e.g., "2h", "1d", "30m")
/trust @user                  - Mark user as trusted (bypass spam checks)
/untrust @user                - Remove trusted status
/ping                         - Check bot responsiveness
/status                       - Show bot + chat health status
/export                       - Export database backup
```

---

## 🚨 Troubleshooting Guide

### Bot Not Receiving Messages
1. Check bot privacy mode: @BotFather → `/setprivacy` → Disable
2. Verify bot is admin: Check group → Administrators list
3. Check logs: `docker compose logs app | grep "TelegramAdminBotService"`

### Spam Detection Not Working
1. Verify OpenAI API key: Check logs for "OpenAI" errors
2. Check training data: Settings → Spam Detection → Training Samples
3. Verify algorithms enabled: Settings → Content Detection

### File Scanning Failures
1. Check ClamAV health: `docker compose logs clamav | grep "ping"`
2. Verify VirusTotal quota: Check logs for "quota exhausted"
3. Test EICAR file: https://www.eicar.org/download-anti-malware-testfile/

### TickerQ Jobs Not Running
1. Check initialization: Logs should show "5 Active Functions"
2. Verify tables exist: `SELECT * FROM ticker.jobs LIMIT 10;`
3. Check for errors: `docker compose logs app | grep "TickerQ"`

### Database Connection Issues
1. Verify PostgreSQL health: `docker compose ps postgres`
2. Check connection string in compose file
3. Look for retry logs: Logs should show "Retrying database operation"

---

## ✅ Success Criteria

Before deploying to larger groups, verify:

- ✅ Bot responds to commands in test group
- ✅ Welcome system works (restrict → accept → unrestrict)
- ✅ Spam detection catches test spam
- ✅ File scanning detects EICAR test file
- ✅ Health checks passing in logs
- ✅ No errors in logs during 1 hour of operation
- ✅ Admin UI accessible and functional
- ✅ All 5 TickerQ jobs active and processing
- ✅ Message history retention working
- ✅ Audit logging capturing actions

---

## 📊 Monitoring After Deployment

### Key Metrics to Watch
- **Bot uptime**: Health check status in logs
- **Message processing rate**: `docker compose logs app | grep "Processing message"`
- **Spam detection rate**: Analytics page in UI
- **Error rate**: `docker compose logs app | grep "ERROR"`
- **TickerQ job failures**: Check job_history table

### Log Commands
```bash
# Follow all logs
docker compose logs -f

# Follow app logs only
docker compose logs -f app

# Search for errors
docker compose logs app | grep -i error

# Check health status
docker compose ps

# Check TickerQ jobs
docker compose exec -T postgres psql -U tgadmin -d telegram_groups_admin -c "SELECT * FROM ticker.jobs ORDER BY created_at DESC LIMIT 10;"
```

---

## 🎉 Ready for Testing!

All production-readiness checks passed:
- ✅ Database connection resilience
- ✅ Health check endpoints (Kubernetes-style)
- ✅ Background job system
- ✅ External service error handling (fail-open)
- ✅ Bot permission validation
- ✅ Logging configuration
- ✅ Container security (Chiseled runtime)

**Next Step:** Start with small test group (< 50 members), monitor for 24-48 hours, then gradually expand.
