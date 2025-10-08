# CLAUDE.md - TelegramGroupsAdmin

ASP.NET Core 10.0 Blazor Server + Minimal API. Telegram spam detection (text + image). SQLite databases.

## Tech Stack
- .NET 10.0 (preview)
- Blazor Server (MudBlazor UI)
- Dapper + FluentMigrator
- Cookie auth + TOTP 2FA
- VirusTotal API, OpenAI Vision API
- SendGrid email service

## Architecture

### Spam Detection
**Text**: Blocklists (Block List Project) → SEO scraping → VirusTotal
**Image**: HistoryBot caches Telegram messages → OpenAI Vision OCR/analysis
**Endpoint**: `POST /check` (auth: `X-API-Key` header or `api_key` query)

### Services
- `SpamCheckService` - URL extraction, blocklist, SEO, VirusTotal orchestration
- `IThreatIntelService` - VirusTotal integration with rate limiting
- `IVisionSpamDetectionService` - OpenAI Vision spam detection with rate limiting
- `ITelegramImageService` - Download images from Telegram
- `IAuthService` - Login, TOTP, password reset
- `IIntermediateAuthService` - Temp tokens for 2FA flow (5min expiry)
- `IInviteService` - Invite token management
- `IUserManagementService` - User CRUD, 2FA reset
- `IMessageHistoryService` - Real-time message updates via events
- `IMessageExportService` - CSV/JSON export
- `IEmailService` - SendGrid email abstraction

### Databases (SQLite)
**identity.db**: users, invites, audit_log, verification_tokens
**message_history.db**: messages (30d retention), message_edits, spam_checks

### Background Services
- `HistoryBotService` - Telegram message caching, real-time events
- `CleanupBackgroundService` - Message retention cleanup (5min interval)

## Configuration (Env Vars)

### Required
```
VIRUSTOTAL__APIKEY
OPENAI__APIKEY
TELEGRAM__HISTORYBOTTOKEN
TELEGRAM__CHATID
SPAMDETECTION__APIKEY
SENDGRID__APIKEY
SENDGRID__FROMEMAIL
SENDGRID__FROMNAME
```

### Optional
```
APP__BASEURL=http://localhost:5161
OPENAI__MODEL=gpt-4o-mini
OPENAI__MAXTOKENS=500
MESSAGEHISTORY__ENABLED=true
MESSAGEHISTORY__DATABASEPATH=/data/message_history.db
MESSAGEHISTORY__RETENTIONHOURS=720
MESSAGEHISTORY__CLEANUPINTERVALMINUTES=1440
SPAMDETECTION__TIMEOUTSECONDS=30
SPAMDETECTION__IMAGELOOKUPRETRYDELAYMS=100
SPAMDETECTION__MINCONFIDENCETHRESHOLD=85
IDENTITY__DATABASEPATH=/data/identity.db
DATAPROTECTION__KEYSPATH=/data/keys
```

## Logging

### Configuration (Program.cs:26-31)
```csharp
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Logging.AddFilter("TelegramGroupsAdmin", LogLevel.Information);
```

### Log Levels
- **Error**: Unexpected application errors (exceptions)
- **Warning**: User errors, rate limits, expected failures
- **Information**: Important operational events (permissions, setup, stats)

### Rate Limit Logging
- **VirusTotal**: LogWarning on `RateLimiterRejectedException` (4 req/min)
- **OpenAI**: LogWarning on HTTP 429 with RetryAfter header
- Both services fail open (return non-spam) during rate limits

## Key Implementations

### Rate Limiting
**VirusTotal**: Polly PartitionedRateLimiter, 4 req/min sliding window, immediate rejection
**OpenAI**: HTTP 429 detection, retry-after header parsing

### Race Condition (Image Spam)
tg-spam bot calls `/check` before HistoryBot caches message
**Solution**: Retry after 100ms if not found (success rate >95%)

### Email Verification
1. Registration generates 24h token (32 random bytes, base64)
2. Email sent with `/verify-email?token=X` link
3. Login blocked until verified (except first Owner user)

### TOTP Authentication Security ✅
**Implementation**: IntermediateAuthService issues 5min tokens after password verification
**Prevents**: Direct access to `/login/verify` or `/login/setup-2fa` without password
**Expiry**: 15min for abandoned TOTP setups (security best practice)

### 2FA Reset for Owners ✅
**Feature**: Owners can reset any user's TOTP to allow re-setup
**Security**: Clears totp_secret, totp_enabled, totp_setup_started_at
**Audit**: Logged to audit_log table

## API Endpoints

### POST /check
```json
Request: {"message": "...", "user_id": "...", "user_name": "...", "image_count": 0}
Response: {"spam": true, "reason": "...", "confidence": 92}
Auth: X-API-Key header OR api_key query param
```

### GET /health
```json
{"status": "healthy", "historyBot": {...stats...}}
```

### Auth Endpoints
- POST /api/auth/login - Returns {requiresTotp, userId, intermediateToken} if 2FA enabled
- POST /api/auth/register - Auto-login after registration
- POST /api/auth/logout
- POST /api/auth/verify-totp - Requires intermediateToken

### Email Verification Endpoints
- GET /verify-email?token=X - Verify email
- POST /resend-verification - Resend verification email
- POST /forgot-password - Send password reset email
- POST /reset-password - Process password reset

## Blazor Pages
- `/` - Dashboard (stats, requires auth)
- `/login` - Login form (generates intermediate token)
- `/login/verify` - TOTP verification (requires intermediate token)
- `/login/setup-2fa` - Mandatory 2FA setup (requires intermediate token)
- `/register` - Registration with invite token
- `/profile` - Password change, TOTP enable/reset
- `/users` - User management, invite system, 2FA reset (Admin/Owner only)
- `/messages` - Message viewer, filters, CSV/JSON export, real-time updates
- `/audit` - Audit log viewer (Admin/Owner only)

## Permission Levels
0=ReadOnly, 1=Admin, 2=Owner
**Hierarchy**: Owner > Admin > ReadOnly (cannot escalate permissions above own level)

## User Statuses
0=Pending, 1=Active, 2=Disabled, 3=Deleted (soft delete)

## Invite System
- Status: 0=Pending, 1=Used, 2=Revoked
- First user auto-Owner (no invite needed)
- Invites expire after 7 days
- Permission level inheritance (cannot exceed creator's level)
- Audit trail for create/use/revoke

## Build/Run
```bash
dotnet build TelegramGroupsAdmin.sln
dotnet run --project TelegramGroupsAdmin/TelegramGroupsAdmin.csproj
docker build -t telegram-groups-admin .
docker run -p 8080:8080 -e VIRUSTOTAL__APIKEY=X telegram-groups-admin
```

## Current Architecture Limitations

**tg-spam (Go bot) is the primary bot**:
- **Single group limitation** - can only manage ONE Telegram group
- Has its own spam detection logic in Go
- Calls TelegramGroupsAdmin `/check` endpoint for supplemental checks (VirusTotal, blocklists, SEO)
- Uses OpenAI as final veto
- Enforces actions (delete, ban, restrict)

**TelegramGroupsAdmin (current role)**:
- Provides `/check` endpoint as detection service TO tg-spam
- Runs HistoryBot for message caching (already multi-group capable)
- Blazor UI for viewing messages and managing users
- Does NOT enforce spam actions - just provides detection services

**Pain points**:
1. tg-spam limited to single group (our app already supports multi-group)
2. Split detection logic across two codebases (Go + C#)
3. Two bots running (tg-spam primary + HistoryBot caching)
4. Limited visibility into tg-spam's decisions in our UI
5. Cannot customize enforcement actions without modifying Go code

## Troubleshooting
**CS8669 warnings**: Razor compiler bug in .NET 10 RC1 - doesn't emit `#nullable enable` for nullable generics (`T="string?"`). Suppressed at project level, tracked in dotnet/razor#7286. Re-test after GA (Nov 2025).
**HistoryBot not caching**: Check TELEGRAM__HISTORYBOTTOKEN, bot added to chat, privacy mode off
**Image spam failing**: Check OPENAI__APIKEY, /data volume mounted
**DB growing**: Check retention (720h default), cleanup service running
**Rate limits**: Check logs for LogWarning messages from VirusTotalService or OpenAIVisionSpamDetectionService

## Roadmap

### Phase 1: Foundation ✅ COMPLETE
- [x] Blazor Server UI with MudBlazor
- [x] Cookie authentication + TOTP 2FA
- [x] User management with invite system
- [x] Audit logging for security events
- [x] Message history viewer with filters and export
- [x] Email verification via SendGrid
- [x] Image spam detection (OpenAI Vision)
- [x] Text spam detection (blocklists, SEO, VirusTotal)

### Phase 2: Native Telegram Bot (PLANNED)
**Goal**: Merge HistoryBot + tg-spam functionality into single multi-group bot

**Why**: TelegramGroupsAdmin already has multi-group message caching via HistoryBot. By adding tg-spam's detection algorithms and enforcement actions, we eliminate the single-group limitation and unify all logic in C#.

**Step 1: Analyze tg-spam Source**
- [ ] Clone tg-spam repository (https://github.com/mr-karan/tg-spam)
- [ ] Document Go detection algorithms to port:
  - Text pattern matching (spam keywords, regex)
  - User behavior heuristics (new join spam, message frequency)
  - Link analysis
  - Media file patterns
- [ ] Map tg-spam features to existing TelegramGroupsAdmin architecture
- [ ] Identify improvements over tg-spam approach

**Step 2: Port Detection Logic to C#**
- [ ] Create unified spam scoring system (0-100 confidence)
- [ ] Port tg-spam's text analysis algorithms from Go to C#
- [ ] Integrate with existing detection pipeline:
  - tg-spam ported logic → blocklists → SEO scraping → VirusTotal → OpenAI Vision (final veto)
- [ ] Add user behavior tracking (join time, message count, patterns)
- [ ] Create `ISpamScorer` abstraction for pluggable detectors

**Step 3: Add Enforcement Actions**
- [ ] Extend `HistoryBotService` with Telegram Bot API admin methods:
  - `DeleteMessageAsync(chatId, messageId)` - remove spam message
  - `BanUserAsync(chatId, userId, until)` - permanent or temporary ban
  - `RestrictUserAsync(chatId, userId, permissions)` - mute/read-only
  - `WarnUserAsync(userId, chatId, reason)` - track warnings in DB
- [ ] Implement action decision logic based on spam score and history
- [ ] Add spam action audit logging

**Step 4: Configuration UI**
- [ ] Per-group settings page in Blazor:
  - Spam score thresholds (0-50 ignore, 50-75 warn, 75+ ban)
  - Action policies (immediate ban vs 3-strike system)
  - Detection toggles (enable/disable specific checks)
  - Whitelist management (verified users, admins immune)
- [ ] Database schema for group-specific configuration
- [ ] Migration from global config to per-group config

**Step 5: Parallel Testing & Migration**
- [ ] Add "monitor mode" - log decisions without taking action
- [ ] Run both bots side-by-side, compare decisions
- [ ] Dashboard showing detection comparison and false positive rates
- [ ] Tune thresholds to match or exceed tg-spam accuracy
- [ ] Switch to enforcement mode when confident
- [ ] Deprecate tg-spam, remove `/check` endpoint (or make optional)

### Phase 3: Advanced Multi-Group Features (FUTURE)
- [ ] Group owner delegation (non-platform admins can manage their groups)
- [ ] Cross-group spam pattern detection (spammer detected in Group A → auto-ban in Groups B, C)
- [ ] Shared/global blacklist across all managed groups
- [ ] Group templates (apply settings from one group to others)
- [ ] Bulk operations (ban user from all groups, global whitelist)

### Phase 4: Advanced Features (FUTURE)
- [ ] ML-based spam detection (train on historical data)
- [ ] Sentiment analysis for toxicity detection
- [ ] Automated report generation
- [ ] API for third-party integrations
