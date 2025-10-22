# TelegramGroupsAdmin - AI Reference

## Stack
.NET 10.0 RC2 (10.0.100-rc.2.25502.107), Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 10.0 RC2, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

## Projects
- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob, TempbanExpiryJob, FileScanJob)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (internal to repos)
- **TelegramGroupsAdmin.Telegram**: Bot services, 14 commands, repos, orchestrators, DM notifications, TelegramMediaService, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks circular deps)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven
- **TelegramGroupsAdmin.ContentDetection**: URL filtering (blocklists, domain filters), impersonation detection (photo hash, Levenshtein), file scanning (ClamAV+VirusTotal), AddContentDetectionServices()

## Architecture Patterns
**Extension Methods**: ServiceCollectionExtensions (AddBlazorServices, AddCookieAuthentication, AddApplicationServices, AddHttpClients, AddTelegramServices, AddRepositories, AddTgSpamWebDataServices, AddTickerQBackgroundJobs), WebApplicationExtensions (ConfigurePipeline, MapApiEndpoints, RunDatabaseMigrationsAsync), ConfigurationExtensions (AddApplicationConfiguration)

**Layered**: UI Models (Blazor DTOs) → Repositories (conversion) → Data Models (DB internal). ModelMappings.cs: .ToModel()/.ToDto() extensions. Repos return/accept UI models only.

**Spam Detection**: ISpamDetectorFactory orchestrates 9 checks (StopWords, CAS, Similarity/TF-IDF, Bayes, MultiLanguage, Spacing, OpenAI, ThreatIntel/VirusTotal, Image/Vision), confidence aggregation, OpenAI veto, self-learning

**Background Services** (composition pattern):
1. TelegramAdminBotService - Bot polling, update routing (5 types), command registration, health checks every 1 min
2. MessageProcessingService - New messages, edits, media download (Animation/Video/Audio/Voice/Sticker/VideoNote), spam orchestration, URL extraction, user photo scheduling, file scan scheduling
3. ChatManagementService - MyChatMember, admin cache, health checks (permissions + invite link validation), chat names
4. SpamActionService - Training QC, auto-ban (cross-chat), borderline reports
5. CleanupBackgroundService - Message retention (keeps spam/ham samples)

## Configuration (Env Vars)
**Required**: VIRUSTOTAL__APIKEY, OPENAI__APIKEY, TELEGRAM__BOTTOKEN, TELEGRAM__CHATID, SPAMDETECTION__APIKEY, SENDGRID__APIKEY/FROMEMAIL/FROMNAME
**Optional**: APP__BASEURL, OPENAI__MODEL/MAXTOKENS, MESSAGEHISTORY__*, IDENTITY__DATABASEPATH, DATAPROTECTION__KEYSPATH

## Key Implementations
- **Rate Limiting**: VirusTotal (Polly PartitionedRateLimiter, 4/min), OpenAI (429 detection), both fail-open
- **Edit Detection**: On edit → message_edits, update messages, re-run spam detection, action if spam
- **Email Verification**: 24h token (32 random bytes), login blocked until verified (except first Owner)
- **TOTP Security**: IntermediateAuthService (5min tokens after password), 15min expiry for abandoned setups
- **TickerQ Jobs**: All jobs re-throw exceptions for proper retry/logging. WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob, TempbanExpiryJob, FileScanJob. Jobs in main app for source generator, payloads in Abstractions. Polling interval: 5s (default 60s) via UpdateMissedJobCheckDelay().
- **Infinite Scroll**: IntersectionObserver on scroll sentinel, timestamp-based pagination (`beforeTimestamp`), MudVirtualize, loads 50 messages/page
- **Scroll Preservation**: Handles negative scrollTop (Chrome/Edge flex-reverse), captures state before DOM update, double requestAnimationFrame for layout completion, polarity-aware adjustment formula, 5px bottom threshold. TODO: Remove debug console.logs after production verification (app.js:318-440)
- **DM Notifications**: IDmDeliveryService (Singleton, creates scopes), pending_notifications (30d expiry), auto-delivery on `/start`. Account linking (`/link`) separate from DM setup. Future: Notification preferences UI with deep link to enable bot DMs.
- **Media Attachments**: TelegramMediaService downloads and saves media (Animation/Video/Audio/Voice/Sticker/VideoNote) to /data/media, stored in messages table. Documents metadata-only (no download for display, file scanner handles temp download). MediaType enum duplicated in Data/Telegram layers (architectural boundary). UI displays media with HTML5 elements (video/audio controls, autoplay for GIFs).

## API Endpoints
- GET /health
- POST /api/auth/login → {requiresTotp, userId, intermediateToken}
- POST /api/auth/register, /api/auth/logout, /api/auth/verify-totp
- GET /verify-email?token=X
- POST /resend-verification, /forgot-password, /reset-password

## Blazor Pages
**Public**: /login, /login/verify, /login/setup-2fa, /register
**Authenticated**: / (dashboard), /analytics (Admin+), /messages, /users (Admin+), /reports (Admin+), /audit (Admin+), /settings (Admin+), /profile
**Features**: URL fragment nav, nested sidebar navigation in Settings, component reuse

## Permissions
**Levels**: 0=ReadOnly, 1=Admin, 2=Owner (hierarchy, cannot escalate above own)
**User Status**: 0=Pending, 1=Active, 2=Disabled, 3=Deleted
**Invites**: 7-day expiry, first user auto-Owner, permission inheritance

## Build Quality
**Standard**: 0 errors, 0 warnings (production-ready)
**History**: 158+ errors→0, 62+ warnings→0, MudBlazor v8.13.0, records→classes for binding, null safety, type safety

## Troubleshooting
- Bot not caching: Check TELEGRAM__BOTTOKEN, bot in chat, privacy mode off
- Image spam failing: Check OPENAI__APIKEY, /data mounted
- DB growing: Check retention (720h default), cleanup running
- Rate limits: Check logs for VirusTotalService/OpenAIVisionSpamDetectionService warnings
- TickerQ: `using TickerQ.Utilities.Base;` (TickerFunctionAttribute), `using TickerQ.Utilities.Models;` (TickerFunctionContext<T>)
- Testing: Always use `--migrate-only` flag, never run app in normal mode (only one instance allowed)

## Development Roadmap

### Complete ✅
**Phase 1-3**: Foundation (Blazor UI, auth+TOTP, user mgmt, audit), 9 spam algorithms (CAS, Bayes, TF-IDF, OpenAI, VirusTotal, Vision), cross-chat bans
**Phase 4** (17 items): TickerQ jobs, unified configs (JSONB), Telegram library, welcome system, /tempban, logging UI, settings UI, anti-impersonation (pHash), warning system, URL filtering (540K domains, 6 blocklists), file scanning (ClamAV+VirusTotal 96-98%, 16K files/month), file scan UI, DM notifications, media attachments (7 types), bot auto-ban

### Pending ⏳
**4.9**: Bot hot-reload, **4.12**: Tag mgmt UI (Settings page - 90% complete), **4.15**: Report aggregation, **4.16**: Appeal system, **4.17.3**: Windows AMSI (deferred), **4.18**: Forum/topics support

### Future 🔮
**Phase 5**: Analytics (time-series, auto-trust, forwarded spam, multi-language DMs)
**Phase 6**: ML insights (pattern detection, OpenAI recommendations), raid detection, bio spam, scheduled messages
**Phase 7**: ML-based spam (10th algorithm)
**Phase 8**: WTelegram integration (see WTELEGRAM_INTEGRATION.md)
**Phase 9**: Mobile web support

## Code Quality
88/100 score. See TECHNICAL_DEBT.md for DI audit (partial) + 14 performance optimizations

## Next Steps
MVP Complete ✅ - Pending: 4.9, 4.15-4.18

## CRITICAL RULES
- Never run app in normal mode (only one instance allowed, user runs in Rider for debugging)
- Testing: Use `dotnet run --migrate-only` to catch startup issues after building
- Always let user test manually - complex app, cannot validate runtime behavior automatically
