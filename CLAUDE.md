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
- **AI Prompt Builder**: Meta-AI feature using OpenAI to generate/improve custom spam detection prompts. Two workflows: (1) Generate from scratch via form (group topic, rules, strictness, training samples), (2) Improve existing version with feedback. prompt_versions table tracks history with versioning, metadata (generation params), created_by. Shared PromptImprovementDialog reused by generation flow and version history. Auto-grow textarea shows full prompt. Settings → Content Detection → OpenAI Integration shows version history with View/Restore/Improve buttons.

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
- Testing: Always use `--migrate-only` flag, never run app in normal mode (only one instance allowed)

### TickerQ Background Jobs
**Dashboard**: `/tickerq-dashboard` (development mode only, disabled in production)

**0 Active Functions** (source generator not discovering jobs):
1. Verify explicit analyzer reference in .csproj: `<Analyzer Include="$(NuGetPackageRoot)tickerq/2.5.3/analyzers/dotnet/cs/TickerQ.SourceGenerator.dll" />`
2. Build with `/p:EmitCompilerGeneratedFiles=true` to check `obj/generated/TickerQ.SourceGenerator/*/TickerQInstanceFactory.g.cs` exists
3. Confirm `TelegramGroupsAdmin.TickerQInstanceFactory.Initialize()` is called in Program.cs **BEFORE** `app.UseTickerQ()` (timing critical for .NET 10 RC2)

**0 Active Threads** (workers not starting):
1. Check `TickerQInstanceFactory.Initialize()` runs BEFORE `app.UseTickerQ()` in pipeline (Program.cs line ~59)
2. Enable debug logging: `builder.Logging.SetMinimumLevel(LogLevel.Debug)` and search for "TickerQ" or "Hosting" messages
3. Verify tables exist: `SELECT table_name FROM information_schema.tables WHERE table_schema = 'ticker';` (NOT public schema!)

**Job Syntax**: `using TickerQ.Utilities.Base;` (TickerFunctionAttribute), `using TickerQ.Utilities.Models;` (TickerFunctionContext<T>)

## Development Roadmap

### Complete ✅
**Phase 1-3**: Foundation (Blazor UI, auth+TOTP, user mgmt, audit), 9 spam algorithms (CAS, Bayes, TF-IDF, OpenAI, VirusTotal, Vision), cross-chat bans
**Phase 4** (18/18 items - COMPLETE): TickerQ jobs, unified configs (JSONB), Telegram library, welcome system, /tempban, logging UI, settings UI, anti-impersonation (pHash), warning system, URL filtering (540K domains, 6 blocklists), file scanning (ClamAV+VirusTotal 96-98%, 16K files/month), file scan UI, DM notifications, media attachments (7 types), bot auto-ban, **AI prompt builder** (meta-AI feature, version history, iterative improvement), **tag management** (7 colors, CRUD UI, usage tracking, admin notes)
**Phase 5** (partial): Analytics enhancements (false positive tracking, response time metrics, detection method comparison, daily trends)

### Backlog 📋
**4.9**: Bot hot-reload (IOptionsMonitor pattern), **4.15**: Report aggregation, **4.16**: Appeal system, **4.17.3**: Windows AMSI (deferred), **4.18**: Forum/topics support

### Future 🔮
**Phase 5**: Analytics (time-series, auto-trust, forwarded spam, multi-language DMs)
**Phase 6**: ML insights (pattern detection, OpenAI recommendations), raid detection, bio spam, scheduled messages
**Phase 7**: ML-based spam (10th algorithm)
**Phase 8**: WTelegram integration (see WTELEGRAM_INTEGRATION.md)
**Phase 9**: Mobile web support

## Code Quality
88/100 score. See BACKLOG.md for deferred features, DI audit (partial), and completed optimizations

## Status
**Phase 4 Complete** ✅ - All 18 core features shipped
**Ready for MVP Deployment Testing** - See deployment testing checklist below

## Pre-MVP Deployment Testing Checklist

### Critical Path Testing
1. **Database Setup** - Run all migrations on clean PostgreSQL 17 instance
2. **First User Setup** - Owner creation, email verification, TOTP enrollment
3. **Bot Connection** - Verify TelegramAdminBotService starts, polls, processes messages
4. **Environment Variables** - All required vars set and validated
5. **TickerQ Jobs** - All 5 background jobs discovered (0 Active Functions = source generator issue)
6. **Spam Detection** - Test all 9 algorithms, OpenAI API connectivity, training data
7. **File Scanning** - ClamAV connection (port 3310), VirusTotal API, FileScanJob execution
8. **DM Notifications** - pending_notifications table, auto-delivery on /start
9. **Media Download** - /data/media writable, all 7 types download correctly
10. **Settings UI** - All configuration pages load, save, validation works

### Feature Validation
- AI Prompt Builder: Generate prompt, improve with feedback, version history
- Tag Management: Create tags, assign to users, color display
- File Scanner: Upload infected file, verify ClamAV+VirusTotal detection
- Welcome System: New user joins, timeout job fires, welcome message sent
- Anti-Impersonation: Duplicate photo detection, Levenshtein name matching
- URL Filtering: Blocked domain detection (540K domains, 6 blocklists)

### Performance Validation
- Messages page: 50+ messages load without lag (infinite scroll)
- Analytics queries: Response time under 100ms (optimized queries)
- Spam detection: < 2s per message (9 algorithms + OpenAI)
- File scanning: VirusTotal rate limit respected (4/min)

### Security Validation
- TOTP required for all users (except first Owner)
- Email verification blocks login
- Cookie authentication secure
- Data Protection keys persistent (/data/keys)
- SQL injection protection (parameterized queries)
- XSS protection (Blazor auto-escaping)

## Next Steps After MVP Testing
Backlog items (4.9, 4.15-4.18) deferred to post-MVP

## CRITICAL RULES
- Never run app in normal mode (only one instance allowed, user runs in Rider for debugging)
- Testing: Use `dotnet run --migrate-only` to catch startup issues after building
- Always let user test manually - complex app, cannot validate runtime behavior automatically
