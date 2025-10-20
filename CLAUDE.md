# TelegramGroupsAdmin - AI Reference

## Stack
.NET 10.0 RC2 (10.0.100-rc.2.25502.107), Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 10.0 RC2, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

## Projects
- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob, TempbanExpiryJob)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (internal to repos)
- **TelegramGroupsAdmin.Telegram**: Bot services, 13 commands, repos, orchestrators, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks circular deps)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven
- **TelegramGroupsAdmin.ContentDetection**: URL filtering (blocklists, domain filters), impersonation detection (photo hash, Levenshtein), AddContentDetectionServices()

## Architecture Patterns
**Extension Methods**: ServiceCollectionExtensions (AddBlazorServices, AddCookieAuthentication, AddApplicationServices, AddHttpClients, AddTelegramServices, AddRepositories, AddTgSpamWebDataServices, AddTickerQBackgroundJobs), WebApplicationExtensions (ConfigurePipeline, MapApiEndpoints, RunDatabaseMigrationsAsync), ConfigurationExtensions (AddApplicationConfiguration)

**Layered**: UI Models (Blazor DTOs) → Repositories (conversion) → Data Models (DB internal). ModelMappings.cs: .ToModel()/.ToDto() extensions. Repos return/accept UI models only.

**Spam Detection**: ISpamDetectorFactory orchestrates 9 checks (StopWords, CAS, Similarity/TF-IDF, Bayes, MultiLanguage, Spacing, OpenAI, ThreatIntel/VirusTotal, Image/Vision), confidence aggregation, OpenAI veto, self-learning

**Background Services** (composition pattern):
1. TelegramAdminBotService - Bot polling, update routing (5 types), command registration, health checks every 1 min
2. MessageProcessingService - New messages, edits, image download, spam orchestration, URL extraction, user photo scheduling
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
- **TickerQ Jobs**: All jobs re-throw exceptions for proper retry/logging. WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob. Jobs in main app for source generator, payloads in Abstractions.
- **Infinite Scroll**: IntersectionObserver on scroll sentinel, timestamp-based pagination (`beforeTimestamp`), MudVirtualize, loads 50 messages/page
- **Scroll Preservation**: Handles negative scrollTop (Chrome/Edge flex-reverse), captures state before DOM update, double requestAnimationFrame for layout completion, polarity-aware adjustment formula, 5px bottom threshold. TODO: Remove debug console.logs after production verification (app.js:318-440)

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

### Phase 1: Foundation ✅
Blazor UI, auth+TOTP, user mgmt, invite system, audit, message history+export, email verification, spam detection

### Phase 2: Unified Telegram Bot ✅
9 spam algorithms (StopWords, CAS, Similarity, Bayes, MultiLanguage, Spacing, OpenAI, VirusTotal, Vision), weighted confidence aggregation, two-tier decision (net>50 OR max>85→OpenAI veto), auto-ban/reports, 9 bot commands, backup/restore (gzip 81%), photo caching (chat icons + user photos via TickerQ), telegram_users normalization (LEFT JOIN), training data (191 spam + 26 ham)

### Phase 3: Advanced Multi-Chat ✅
Cross-chat spam detection (global bans), shared blacklist (user_actions, stop_words), global auto-apply
Not implemented: Chat delegation, templates, bulk UI (already automatic)

### Phase 4: Infrastructure & Configuration ⏳
**4.1** ✅: TickerQ PostgreSQL job queue
**4.2** ✅: Unified configs (JSONB, global+chat), IConfigService, auto-merging
**4.2.1** ✅: Timestamps bigint→timestamptz, DTOs→DateTimeOffset
**4.3** ✅: TelegramGroupsAdmin.Telegram library extraction, AddTelegramServices()
**4.4** ✅: Welcome system - TickerQ jobs, DM modes, invite caching, templates
**4.5** ✅: OpenAI tri-state (spam/clean/review), modular prompts
**4.6** ✅: /tempban - Duration parsing, TempbanExpiryJob, DM notifications
**4.7** ✅: Runtime Logging - /settings#logging, configs JSONB
**4.8** ✅: Settings UI - All tabs, nested sidebar nav
**4.10** ✅: Anti-Impersonation - Composite scoring (name+photo), pHash, Reports queue
**4.11** ✅: Warning System - Count-based, auto-ban threshold, UI removal
**4.12** ✅: Admin Notes & Tags - Actor system, TagManagement UI, color-coded chips
**4.13** ✅: URL Filtering - 540K domains, 6 blocklists, hard/soft modes, <1ms lookups
**4.19** ✅: Actor System - Exclusive Arc (web/telegram/system), 5 tables, LEFT JOIN

**Pending**:
**4.9**: Bot connection management - Hot-reload, IBotLifecycleService, /settings#bot-connection (Owner-only)
**4.14**: Critical Checks Infrastructure - Configurable always-run checks (bypass trust/admin status), content_check_configs.always_run column, ContentCheckCoordinator refactor (filter by always_run), ContentActionService.HandleCriticalCheckViolationAsync (delete+DM notice, NO ban/warn for trusted/admin), /settings#critical-checks UI (per-check toggles), DM fallback to chat reply (bot_dm_enabled). Policy: URL filtering + file scanning always run for ALL users
**4.15**: Report Aggregation - Multi-report auto-escalation (3 unique in 1hr→action), confidence boost (+15/report), reporter accuracy scoring, false report protection, /reports#analytics
**4.16**: Appeal System - DM channel establishment, appeals queue /reports#appeals, approve/deny workflow, max 2 appeals/ban, 30-day expiration
**4.17**: File Scanning - Two-tier architecture: Tier 1 (local parallel voting: ClamAV + YARA + optional Windows AMSI multi-engine with 4-5 free AVs), Tier 2 (cloud queue: VT/MetaDefender/HybridAnalysis/Intezer, user-ordered, fail-open). FileScanningCheck always_run=true. See FILE_SCANNING.md for complete architecture, Windows setup automation, and deployment scenarios
**4.18**: Forum/Topics Support - Pass message_thread_id in bot replies, store in messages table, update MessageProcessingService

### Phase 5: Analytics & Data Aggregation 🔮
**5.1**: Analytics repo (time-series queries, false positive/negative rates, per-check performance)
**5.2**: TickerQ daily aggregation (analytics_daily_stats, api_usage_stats, check_performance_stats, weekly/monthly rollups)
**5.3**: Analytics UI (/analytics#trends - volume/ratios/patterns, /analytics#performance - accuracy/check perf/confidence)
**5.4**: Charting library (MudBlazor Charts or ApexCharts.Blazor)
**5.5**: User Reputation/Auto-Trust - Auto-whitelist (7 days+50 clean msgs+0 warnings), UI approval, per-chat thresholds, warning system integration, builds on manual whitelist
**5.6**: Forwarded Message Spam - Track spam channels, mass-forward detection (same forward 5+ users), channel blacklist (CAS+custom spam_sources), auto-delete blacklisted forwards
**5.10**: Smart Multi-Language - Non-English detection, whitelist bypass trusted users, spam detection first, helpful DM in user's language, warning integration (3+ violations→mute), database templates+OpenAI translation, cached common (Spanish/Russian/Chinese)

### Phase 6: ML-Powered Insights 🔮
**6.1**: Insights service (manual override analysis, check performance, stop word suggestions, pattern detection/ML clustering, auto-trust effectiveness)
**6.2**: OpenAI recommendations (ML→natural language, Apply buttons, priority levels)
**6.3**: Insights dashboard (/analytics#insights - config/performance/cost/patterns/auto-trust cards, historical tracking)
**6.4**: Background insights (TickerQ daily job, high-priority notifications)

### Phase 7: Advanced Features 🔮
ML-based spam (10th algorithm using historical), sentiment analysis (shelved - false positives), API for third-party (not needed)

### Phase 8: WTelegram User Account Integration 🔮
**See:** WTELEGRAM_INTEGRATION.md for detailed planning

### Phase 9: Mobile Web Support 🔮
Responsive Blazor UI, touch-friendly interactions, PWA support, mobile-first critical paths (reports/users/messages)

## Phase 6: Optional Protections (Open Source)
**6.1** ✅: Bot Auto-Ban - BotProtectionService, /settings#telegram-general
**6.2**: Raid Detection - Semantic similarity, sliding window, temp lockdown, OpenAI/Ollama
**6.3**: Bio Spam Check - Analyze bio/name on join, 30-day cache, 10th spam algo
**6.4**: Scheduled Messages - /schedule command, recurring cron, TickerQ pattern

**Open Source Prep**:
**4.16**: OpenAI-Compatible API - BaseUrl config, Ollama/LM Studio, local LLM support (removes API barrier)

## Code Quality
**See:** REFACTORING_BACKLOG.md (88/100 score)

## Next Steps
MVP Complete ✅ (Oct 17) - Phase 4.14 Critical Checks Infrastructure, Phase 4.15-4.18 pending

## CRITICAL RULES
- Never run app in normal mode (only one instance allowed, user runs in Rider for debugging)
- Testing: Use `dotnet run --migrate-only` to catch startup issues after building
- Always let user test manually - complex app, cannot validate runtime behavior automatically
