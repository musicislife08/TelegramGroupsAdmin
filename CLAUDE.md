# TelegramGroupsAdmin - AI Reference

## Stack
.NET 10.0, Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 10, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

## Projects
- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (internal to repos)
- **TelegramGroupsAdmin.Telegram**: Bot services, 13 commands, repos, orchestrators, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks circular deps)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven

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

## Database (PostgreSQL)
**DB**: telegram_groups_admin, 19 tables
**Migrations**: InitialSchema.cs (validated), Latest: RemoveUserNameAndPhotoFromMessages (normalized to telegram_users)

**Core Tables**:
- messages: message_id(PK), chat_id, user_id, timestamp, edit_date, message_text, photo*, urls, content_hash. Retention: 180d (except spam/ham refs)
- telegram_users: telegram_user_id(PK, manually set from Telegram API), username, first_name, last_name, user_photo_path, photo_hash (Phase 4.10), is_trusted (Phase 5.5), warning_points (Phase 4.11), first_seen_at, last_seen_at. **Centralized user metadata** (usernames/photos now via JOIN, not denormalized in messages). user_id=0 = "system" (imported training data)
- detection_results: id(PK), message_id(FK cascade), detected_at, detection_source(auto/manual), is_spam(computed: net_confidence>0), confidence, net_confidence, reason, detection_method, added_by, used_for_training, check_results_json, edit_version. Retention: permanent
- message_edits: id(PK), message_id(FK cascade), edit_date, old/new text/hash
- user_actions: id(PK), user_id, action_type(ban/warn/mute/trust/unban), message_id, issued_by, issued_at, expires_at, reason. Global cross-chat
- welcome_responses: id(PK), chat_id, user_id, username, welcome_message_id, response(accepted/denied/timeout/left), responded_at, dm_sent, dm_fallback

**Config Tables**:
- stop_words: id(PK), word, word_type(0=message/1=username/2=userID), added_date, source, enabled, added_by, detection_count, last_detected_date
- spam_detection_configs: chat_id(PK), min_confidence_threshold, enabled_checks, custom_prompt, auto_ban_threshold
- spam_check_configs: check_name(PK), enabled, confidence_weight, config_json
- configs: id(PK), chat_id(nullable for global), spam_detection_config, welcome_config, log_config, moderation_config, bot_protection_config (all JSONB), invite_link (cached). Pattern: global (chat_id=NULL) + per-chat overrides

**Identity**:
- users: id(GUID PK), email, password_hash, security_stamp, permission_level(0=ReadOnly/1=Admin/2=Owner), totp*, status(0=Pending/1=Active/2=Disabled/3=Deleted), email_verified
- invites: token(PK), created_by, expires_at, used_by, permission_level, status(0=Pending/1=Used/2=Revoked)
- audit_log: id(PK), event_type(enum), timestamp, actor_user_id, target_user_id, value
- verification_tokens: id(PK), user_id, token_type(email_verify/password_reset), token, expires_at

**Design**: Normalized, cascade deletes (edits cascade, detections/actions remain), configurable retention, global actions, audit trail

## Configuration (Env Vars)
**Required**: VIRUSTOTAL__APIKEY, OPENAI__APIKEY, TELEGRAM__BOTTOKEN, TELEGRAM__CHATID, SPAMDETECTION__APIKEY, SENDGRID__APIKEY/FROMEMAIL/FROMNAME
**Optional**: APP__BASEURL, OPENAI__MODEL/MAXTOKENS, MESSAGEHISTORY__*, IDENTITY__DATABASEPATH, DATAPROTECTION__KEYSPATH

## Key Implementations
- **Rate Limiting**: VirusTotal (Polly PartitionedRateLimiter, 4/min), OpenAI (429 detection), both fail-open
- **Edit Detection**: On edit → message_edits, update messages, re-run spam detection, action if spam
- **Email Verification**: 24h token (32 random bytes), login blocked until verified (except first Owner)
- **TOTP Security**: IntermediateAuthService (5min tokens after password), 15min expiry for abandoned setups
- **TickerQ Jobs**: All jobs re-throw exceptions for proper retry/logging. WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob. Jobs in main app for source generator, payloads in Abstractions.

## API Endpoints
- GET /health
- POST /api/auth/login → {requiresTotp, userId, intermediateToken}
- POST /api/auth/register, /api/auth/logout, /api/auth/verify-totp
- GET /verify-email?token=X
- POST /resend-verification, /forgot-password, /reset-password

## Blazor Pages
**Public**: /login, /login/verify, /login/setup-2fa, /register
**Authenticated**: / (dashboard), /analytics (Admin+), /messages, /spam (Admin+), /users (Admin+), /reports (Admin+), /audit (Admin+), /settings (Admin+), /profile
**Features**: URL fragment nav, logical menu grouping, component reuse

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
**4.2** ✅: Unified configs table (JSONB, chat_id NULL=global), IConfigService (Save/Get/GetEffective/Delete), auto-merging
**4.2.1** ✅: 36 timestamps bigint→timestamptz, DTOs→DateTimeOffset, TickerQ configs, 228 messages preserved
**4.3** ✅: TelegramGroupsAdmin.Telegram library, moved 6 models+13 repos+services+13 commands+4 background services, AddTelegramServices(), ModelMappings public, clean dependencies
**4.4** ✅: Welcome system - DB schema, WelcomeService, Repository, bot integration, config from DB, chat name caching, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob), callback validation, user leaves, /settings#telegram. Flow: restrict on join→welcome+buttons→timeout auto-kick (60s)→accept (restore to chat default permissions + DM w/ return button)→deny/timeout (kick). DM modes: Exclusive (DM-only, chat silent), Chat+DM (both), Chat-only (DM fallback). Invite link caching: IChatInviteLinkService stores links in configs.invite_link, GetInviteLinkAsync (cache-first), RefreshInviteLinkAsync (force refresh), health check validates every 1 min. Templates: chat_welcome, dm_template, chat_fallback. Variables: {chat_name}, {username}, {rules_text}
**4.5** ✅: OpenAI tri-state (spam/clean/review), SpamCheckResultType enum, modular prompts (technical+custom rules+mode guidance), review queue integration. Prompt arch: GetBaseTechnicalPrompt (JSON, unchangeable), GetDefaultRulesPrompt (spam/legit indicators, user-overridable), GetModeGuidancePrompt (veto vs detection), BuildSystemPrompt factory
**4.6** ✅: /tempban command - Flexible duration parsing (5m/1h/24h formats), TickerQ TempbanExpiryJob for auto-unrestriction, cross-chat global bans (user_actions.ExpiresAt), ModerationActionService.TempBanUserAsync(). DM notifications: SendTempBanNotificationAsync sends details + rejoin links for all managed chats via IUserMessagingService. UI: TempBanDialog (duration presets), Messages page action, UserDetailDialog action. Bot response auto-delete: DeleteResponseAfterSeconds set to ban duration.
**4.7** ✅: Runtime Logging - IRuntimeLoggingService, LoggingSettings.razor (/settings#logging), configs JSONB storage (requires restart)
**4.8** ✅: Settings UI - All tabs (General, Integrations, Security, Logging). Read-only env vars
**4.10** ✅: Anti-Impersonation - Composite scoring (name Levenshtein 50pts + photo pHash 50pts). Triggers: join + first N messages. Auto-ban 100pts, review queue 50-99pts. telegram_users.photo_hash, ImpersonationAlertsRepository, unified Reports queue
**4.11** ✅: Warning System - Simple count (user_actions), WarnCommand, auto-ban at threshold (default 3), UserDetailDialog removal, global warnings
**4.12** ✅: Admin Notes & Tags - admin_notes + user_tags + tag_definitions tables, Actor system, UserDetailDialog UI, TagManagement.razor (/settings#tags), color-coded chips, pin/unpin, confidence_modifier
**4.19** ✅: Actor System - Exclusive Arc (web_user_id, telegram_user_id, system_identifier + CHECK). 5 tables migrated (user_actions, detection_results, user_tags, stop_words, admin_notes). LEFT JOIN display name resolution. Actor badges in UI

**Pending**:
**4.9**: Bot connection management - Hot-reload, IBotLifecycleService, /settings#bot-connection (Owner-only)
**4.13**: Advanced Filter Engine - custom_filters table (pattern regex, action, enabled, hit_count), chat-specific/global, domain blacklist/whitelist, phrase normalization, URL patterns, 12th spam check in SpamDetectorFactory, /spam#filters UI CRUD, confidence weighting integration
**4.14**: Report Aggregation - Multi-report auto-escalation (3 unique in 1hr→action), reports tracking (message_id, reported_by, timestamp), confidence boost (+15/report), reporter accuracy scoring, false report protection (<60% accuracy→downweight, 10+ false→remove permission), /reports#analytics top reporters
**4.15**: Appeal System - Welcome requires bot DM start (Accept→bot DM→unrestrict), establishes DM channel, banned users submit appeals via DM, appeals queue /reports#appeals (user history, ban reason, detection, appeal text), approve/deny+reason, max 2 appeals/ban, 30-day expiration, appeals_history verdicts
**4.17**: Additional Media Types - GIFs, stickers, videos, voice, audio, documents, video notes. DB fields + UI display + spam detection (Vision, Whisper, VirusTotal)
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

### Phase 8: WTelegram User Account Integration 🔮 PLANNED
**See:** WTELEGRAM_INTEGRATION.md for detailed planning

**Unlocks (impossible with Bot API):**
- Full member list (including lurkers) - `Channels_GetParticipants()` returns ALL members
- Historical message import - `Messages_GetHistory()` backfills messages before bot joined
- Send as admin user - Messages appear from admin's personal account (not bot)
- Admin chat interface - Full chat UI in browser (/chat page)
- Enhanced user data - Last seen, bio, common chats, verified badge

**Architecture:**
- Multi-instance session manager (per-web-user WTelegram clients)
- Session isolation (/data/telegram-sessions/{userId}/session.dat encrypted with Data Protection API)
- Resource management (30min idle cleanup, ~10-20MB per session)
- Disable message listener (bot owns all incoming message processing)

**Authentication Flow:**
- Phone number → SMS verification code → 2FA password (if enabled)
- Session persists 30 days (Telegram default)
- Audit all actions (TelegramAccountConnected, MessageSent, MemberListImported, HistoryImported)

**Implementation Priority:**
1. Core infrastructure + auth (Day 1-2, ~9 hours)
2. Full member list import (Day 3, ~4 hours) - Quick high-value win
3. Send message as admin (Day 4, ~3 hours)
4. Historical import (Day 5, ~6 hours)
5. Chat interface (Day 6-7, ~8 hours) - Optional
6. Polish (Day 8-9, ~7 hours)

**Total Effort:** ~37 hours (~5 days)

**Security:**
- Data Protection API encryption (same as TOTP)
- Owner/Admin permission required
- Per-user session isolation (no sharing)
- Audit trail for all actions
- 30min idle session disposal

**Why WTelegram (not TDLib)?**
- Pure C# (no native dependencies)
- Active maintenance (v4.3.11, Feb 2025)
- MIT license (commercial-friendly)
- Simple API surface
- Good documentation

**Use Cases:**
- "Show all 500 members, not just 120 who messaged" (lurker visibility)
- "Import last 6 months for spam trend analysis" (historical context)
- "Reply as myself from browser, not bot" (authoritative moderation)
- "Manage chats from desktop, no mobile needed" (admin convenience)

**ToS Compliance:**
✅ Allowed: User-initiated actions via UI (every action requires explicit button click)
❌ Prohibited: Automated actions without consent, bulk scraping
**Our Approach:** No automation - all WTelegram calls triggered by admin UI interaction

### Phase 9: Mobile Web Support 🔮 PLANNED
**Responsive Blazor UI for tablet/mobile browsers**

**Motivation:**
- Admins need moderation access on-the-go
- Mobile Telegram app for reading, web UI for moderation actions
- Tablets common for community managers

**Scope:**
- Responsive layouts (MudBlazor breakpoints: xs/sm/md/lg/xl)
- Touch-friendly interactions (larger buttons, swipe gestures)
- Optimized data loading (pagination, lazy loading, infinite scroll)
- Mobile-first critical paths (ban user, mark spam, view reports)
- Progressive Web App (PWA) support (install to home screen, offline basics)

**Key Pages to Optimize:**
1. **/reports** - Quick spam triage (swipe left=ham, right=spam)
2. **/users** - List view with touch-friendly actions (trust toggle, ban button)
3. **/messages** - Infinite scroll message viewer
4. **/chat** (if Phase 8 implemented) - Mobile chat interface
5. **/profile** - Manage TOTP on mobile

**UI Patterns:**
- Bottom sheet dialogs (easier to reach on mobile)
- Floating action buttons (FAB) for primary actions
- Collapsible navigation (hamburger menu)
- Card-based layouts (better for small screens)
- Sticky headers (context while scrolling)

**Performance:**
- Reduce initial bundle size (lazy load components)
- Optimize images (serve smaller versions for mobile)
- Cache API responses (reduce mobile data usage)
- Debounce search inputs (prevent excessive queries on slow connections)

**Testing:**
- Chrome DevTools device emulation
- Real device testing (iOS Safari, Android Chrome)
- Touch gesture validation
- Offline scenario handling

**Total Effort:** ~15-20 hours (~2-3 days)

**Dependencies:**
- Existing Blazor pages (refactor for responsiveness)
- MudBlazor responsive components (MudHidden, MudBreakpointProvider)

**Success Metrics:**
- All pages usable on 375px width (iPhone SE)
- Critical actions accessible within 2 taps
- Page load < 3 seconds on 3G
- No horizontal scrolling
- Touch targets ≥ 44px (Apple HIG, Material Design)

**Future Enhancements:**
- Native mobile apps (MAUI) - If web performance insufficient
- Push notifications (ban/spam alerts) - Requires backend integration
- Offline mode (service workers) - Cache recent data for offline viewing

## Future Enhancements (Pending Feedback)
Cross-Group Welcome Exemption: Auto-delete rules notification for trusted users joining new groups (no restrictions, tagged+rules, X sec auto-delete). Awaiting admin feedback on auto-trust vs per-group vetting

## Before Open Source: Broader Community Appeal

**Context**: Optimized for curated topical communities (surgical moderation). Research shows diverse needs requiring optional protective features.

**Research (Oct 2024)**:
- 2000% increase Telegram scams since Nov 2024 (Scam Sniffer)
- 15.4M groups/channels blocked 2024 (Telegram)
- 40% phishing on messaging platforms (CloudSEK)
- Crypto: Fake verification bots, mass impersonation (300+ fake accounts/day), profile attacks
- Bot bypass: AI 85-100% CAPTCHA solve, commercial farms 95-99% at $1-3/1000

**Community Types**:
1. Crypto/NFT - Coordinated raids, verification bot scams, bio spam, impersonation
2. Large Public (10k+) - Mass join attacks, forwarding campaigns, overwhelmed moderation
3. Quality-Focused - Low-effort spam, off-topic, link dumping
4. Discord Migrants - Expect CAPTCHA, stricter new user controls, role-based permissions

**Tech Assessment**:
- CAPTCHA: 85-100% AI bypass, 20% legit user abandonment, security theater
- Behavioral Analysis: Telegram Bot API limitations (no device fingerprinting, no IP, only basic timing)
- Smart Alternatives: Semantic similarity (AntiRAID.AI GPT-3), bio scanning, bot flag, timing analysis
- Local LLM: Ollama/LM Studio OpenAI-compatible, Llama 3.3/Mistral 97% F1 spam classification, zero API costs

### Validated Features (Phase 6: Optional Protections)
**Philosophy**: All optional per-chat (global+chat override), disabled by default except bot auto-ban, preserves surgical precision

**Phase 4.16 - OpenAI-Compatible API** ⭐ (1-2 days) - CRITICAL FOR OPEN SOURCE
Config: BaseUrl in OpenAIOptions (any OpenAI-compatible API), drop-in Ollama/LM Studio/LiteLLM/vLLM, fallback chain (local→cloud), cost-free local models (Llama 3.3, Mistral, Phi-4, DeepSeek)
```csharp
public class OpenAIOptions {
    public string ApiKey { get; set; } = "";  // Optional for local
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
}
```
Why: Removes API key barrier, self-hosted deployments, privacy-conscious users, eliminates costs. Llama 3.3: 97% F1 spam classification

**Phase 6.1 - Bot Auto-Ban** ✅ COMPLETE
Default: ON, Target: All communities
Implementation: BotProtectionConfig (Enabled, AutoBanBots, AllowAdminInvitedBots, WhitelistedBots, LogBotEvents), bot_protection_config JSONB in configs, BotProtectionService (ShouldAllowBotAsync, BanBotAsync), integration in WelcomeService.HandleChatMemberUpdateAsync, checks user.IsBot flag, validates whitelist, verifies admin-invited, audit to user_actions "system_bot_protection", Settings UI /settings#telegram-general
Architecture: IBotProtectionService Singleton, IConfigService.GetEffectiveAsync (global/chat merge), ChatAdminsRepository, UserActionsRepository audit
Why: Blocks profile spam (bots join, change profile to explicit images), prevents bot raids, zero false positives, real attack vector
Cost: None (native Telegram API)

**Phase 6.2 - Smart Raid Detection** 🎯 (3-4 days)
Default: OFF, Target: Crypto/large groups under attack
Features: Semantic similarity (not exact text), threshold 5+ messages 85%+ similarity in 60s window, temp lockdown (15-120 min), OpenAI/Ollama analysis, ML effectiveness monitoring→Phase 6 analytics
```csharp
public class RaidProtectionConfig {
    public bool Enabled { get; set; } = false;
    public int MessageSimilarityThreshold { get; set; } = 5;
    public double SimilarityPercentage { get; set; } = 0.85;
    public int TimeWindowSeconds { get; set; } = 60;
    public int LockdownDurationMinutes { get; set; } = 30;
}
```
Logic: Collect new user msgs in sliding window→OpenAI/Ollama "semantically similar coordinated spam?"→"Check airdrop!"+"Free tokens!"+"Claim rewards!"→90% similar (coordinated) vs "Hi everyone"+"Thanks"+"Great group!"→20% similar (legit)
Why: Preserves viral growth (diverse organic), only triggers coordinated campaigns, AntiRAID.AI research 95% accurate
Cost: 1 OpenAI call/raid or free local LLM

**Phase 6.3 - Bio Spam Check** 🔍 (2 days)
Default: OFF, Target: Crypto/NFT profile attacks
Features: Analyze bio/name on join (before first msg), runs as 10th spam algo in SpamDetectorFactory, cached per user (30 days), feeds confidence aggregation, optional auto-action
```csharp
public class BioSpamCheck : ISpamCheck {
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequestBase req) {
        var cached = await _cache.GetBioAnalysisAsync(req.UserId);
        if (cached != null) return cached;
        var analysis = await _openAIService.AnalyzeBioAsync(bio: req.UserBio, firstName: req.FirstName, username: req.Username);
        await _cache.SetBioAnalysisAsync(req.UserId, analysis, days: 30);
        return new SpamCheckResponse { CheckName = "BioScan", Result = analysis.IsSpam ? Spam : Clean, Confidence = analysis.Confidence, Details = analysis.Reasoning };
    }
}
```
Examples: "💰 Crypto expert | DM for signals 📈"→95% spam, ""+"User12345"→40% suspicious, "Software engineer @ FAANG"→5% spam
Why: Proactive before spam occurs, prevents profile attacks (explicit in bio), catches 90% bots before first msg (AntiRAID.AI)
Cache: user_bio_scans (user_id, analysis_result, cached_at), prevents duplicate API on rejoin
Cost: 1 OpenAI/user (cached after), ~$0.0001/check GPT-4o-mini, free local LLM

**Phase 6.4 - Scheduled Messages** 📅 (2-3 days)
Default: OFF, Target: Project communities, announcement-heavy
Features: /schedule [time] [msg] (ISO8601 or relative "2h"/"tomorrow 9am"), recurring (cron "every Monday 10am"/"daily 8pm"), auto-pin, time-based welcomes (weekends vs weekdays), TickerQ persistence
Implementation: Similar WelcomeTimeoutJob/DeleteMessageJob pattern
Why: Professional feature, reduces admin workload, member engagement
Cost: Minimal (TickerQ)

**Explicitly Rejected**:
- CAPTCHA ❌: 85-100% AI bypass, 20% legit abandonment, security theater. Alt: Bio scan, bot detection, timing
- Account Age ❌: Philosophy mismatch (surgical vs hammer), rejects legit new users, false positive cost high. Alt: Post-join filtering
- Advanced Behavioral ❌: Telegram API limitations (no fingerprinting, no IP, only basic timing). Alt: Bot flag, bio, semantic similarity
- Simple Rate Limiting ❌: Breaks viral growth (link shared→20+ organic joins). Alt: Smart raid detection (semantic, ignores organic)

**Config Philosophy**: Global+chat override pattern (spam_detection_configs)
```sql
INSERT INTO configs (chat_id, config_type, config_json) VALUES
(NULL, 'bot_protection', '{"auto_ban_bots": true, "whitelisted_bots": []}'),
(NULL, 'raid_protection', '{"enabled": false, "similarity_threshold": 5, "similarity_percentage": 0.85}'),
(NULL, 'bio_scan', '{"enabled": false, "cache_duration_days": 30}');
(-1001234567890, 'raid_protection', '{"enabled": true, "similarity_threshold": 3}');  -- Crypto group override
```

**Implementation Priority (Before Open Source)**:
Phase 1 (Must-have): 4.16 OpenAI-Compatible (1-2d, critical), 6.1 Bot Auto-Ban (30min, ✅done), 6.3 Bio Spam (2d, high-value)
Phase 2 (Nice-to-have): 6.2 Raid Detection (3-4d, crypto/large groups), 6.4 Scheduled Messages (2-3d, professional)
Total: 8-12 days for broad appeal

**Success Metrics**: Curated topical✅, Crypto/NFT✅, Large public✅, Self-hosted✅, Surgical precision✅
**Positioning**: "Flexible Telegram admin for all community types - topical discussions to high-security crypto. Configure strict/permissive. Run local or cloud AI. Your community, your rules."

## Refactoring Review
**Purpose**: Systematic code quality analysis (dotnet-refactor-advisor agent)
**Scope**: All 5 projects - architecture patterns, performance (query optimization, caching, async), modern C# (C# 12/13, collection expressions, pattern matching), code duplication, error handling consistency, test coverage gaps, config management (magic numbers), security patterns
**Deliverable**: REFACTORING_BACKLOG.md - severity classification, effort estimates, impact analysis, before/after examples, execution order
**Constraints**: Maintain 0 errors/0 warnings, no breaking changes, preserve functionality, readability-first, production stability
**Priority**: Medium (after 4.4, before 4.5)

## Current Status & Timeline (Oct 2025)

**Development Velocity** (Oct 6 - Oct 17):
- 11 days elapsed, 125 commits (~11 commits/day)
- Delivered: Phases 1-3 complete, Phase 4 at 50%
- Productivity: ~73% of core platform in 11 days
- Quality maintained: 0 errors, 0 warnings throughout

**Completion Status**:
- Phase 1: 100% ✅
- Phase 2: 100% ✅ (2.1-2.9 complete)
- Phase 3: 100% ✅
- Phase 4: 63% (4.1-4.8, 4.10-4.11, 4.12, 4.19 complete; 4.9, 4.13-4.18 pending)
- Phase 5: 0% (analytics, optional)
- Phase 6: 10% (6.1 bot auto-ban complete)

**Production Ready**: Migration+Backup✅, Build Quality✅, System Ready: Fresh DB init, cross-machine backup/restore+TOTP, production deployment

---

## Estimated Timeline to Completion

### **Milestone 1: MVP - Production Ready** ✅ COMPLETE
**Achieved**: October 17, 2025
**Actual Effort**: ~4 hours (warning removal UI)

| Feature | Priority | Status | Notes |
|---------|----------|--------|-------|
| 4.10 Anti-Impersonation | Critical | ✅ Done | Composite scoring, pHash, unified queue UI |
| 4.11 Warning System | Critical | ✅ Done | Simple count-based, auto-ban, UI removal |
| 4.9 Bot Connection Mgmt | Low | Deferred | Hot-reload infrastructure (nice-to-have) |

**Deliverable**: ✅ Full-featured moderation platform with impersonation protection and warning system

---

### **Milestone 2: Feature-Complete** (~12-15 days from now)
**Target Date**: ~November 1, 2025
**Estimated Effort**: 40-48 hours (5-6 days beyond MVP)

| Feature | Priority | Estimate | Rationale |
|---------|----------|----------|-----------|
| 4.13 Filter Engine | High | 24-32h (3-4d) | Regex patterns, domain blacklist/whitelist, 12th spam check integration |
| 4.14 Report Aggregation | High | 16h (2d) | Multi-report escalation, accuracy scoring, analytics |
| 4.15 Appeal System | Medium | 32-40h (4-5d) | DM channel, appeals queue, verdict workflow |
| 4.18 Forum/Topics | Low | 8h (1d) | Pass-through message_thread_id (simple) |
| 4.16 OpenAI-Compatible | **Critical** | 8-16h (1-2d) | Local LLM support (Ollama/LM Studio), BaseUrl config |

**Deliverable**: Production-ready with advanced filtering, appeals, and self-hosted AI option

---

### **Milestone 3: Security Audit & Refactor Analysis** (~18-22 days from now)
**Target Date**: ~November 8, 2025
**Estimated Effort**: 24-32 hours (3-4 days beyond Feature-Complete)

**Critical Quality Gates:**
| Task | Priority | Estimate | Rationale |
|------|----------|----------|-----------|
| **Security Audit** | **CRITICAL** | 16-24h (2-3d) | Full security review before open source |
| **Refactor Analysis** | **CRITICAL** | 8-12h (1-2d) | Final code quality scan, technical debt identification |

**Security Audit Scope:**
- Authentication & authorization (TOTP, cookie security, permission enforcement)
- Input validation & sanitization (SQL injection, XSS, command injection)
- API security (rate limiting, CSRF, endpoint protection)
- Data protection (encryption at rest, secure storage, PII handling)
- Telegram bot security (command validation, webhook security if implemented)
- Dependency vulnerabilities (NuGet packages, outdated libraries)
- Configuration security (secrets management, environment variables)
- Session management (token expiry, secure cookie flags)
- Database security (parameterized queries, connection string protection)
- File upload security (if any) (path traversal, malicious content)

**Refactor Analysis Scope:**
- Performance optimization opportunities (query N+1, caching gaps)
- Code duplication (DRY violations, extract common patterns)
- Complexity hotspots (cyclomatic complexity, method length)
- Modern C# patterns (collection expressions, pattern matching, primary constructors)
- Async/await consistency (missing ConfigureAwait, blocking calls)
- Error handling consistency (try-catch patterns, exception types)
- Test coverage gaps (unit tests, integration tests)
- Documentation completeness (XML comments, README updates)
- Architecture patterns (dependency injection, interface usage)
- Naming consistency (conventions across projects)

**Deliverable**: Production-hardened, security-validated, refactored codebase ready for open source release

**Tools/Agents:**
- `security-reviewer` agent for automated security scan
- `dotnet-refactor-advisor` agent for code quality analysis
- Manual review of critical paths (auth, data access, bot commands)
- OWASP dependency check for known vulnerabilities

---

### **Milestone 4: Open Source Ready** (~25-30 days from now)
**Target Date**: ~November 15, 2025
**Estimated Effort**: 64-80 hours (8-10 days beyond Security Audit)

**Phase 6 Optional Protections:**
| Feature | Priority | Estimate | Rationale |
|---------|----------|----------|-----------|
| 6.1 Bot Auto-Ban | ✅ | Done | Already complete |
| 4.16 OpenAI-Compatible | **CRITICAL** | 8-16h (1-2d) | Local LLM support (Ollama/LM Studio), removes API barrier |
| 6.3 Bio Spam Check | High | 16h (2d) | 10th spam algorithm, user bio analysis on join, 30-day cache |
| 6.2 Raid Detection | Medium | 24-32h (3-4d) | Semantic similarity, sliding window, temp lockdown |
| 6.4 Scheduled Messages | Low | 16-24h (2-3d) | TickerQ pattern, recurring cron, professional feature |

**Infrastructure & Deployment:**
| Task | Priority | Estimate | Rationale |
|------|----------|----------|-----------|
| .NET 10 RC2 Update | High | 4-6h | Update project files, NuGet packages, test for breaking changes |
| Production Docker Container | **CRITICAL** | 8-12h (1-2d) | Ubuntu Chiseled (distroless-like), non-root user, multi-stage build, compact size |
| Docker Compose Example | **CRITICAL** | 4-6h | PostgreSQL + app, volume management, environment config |

**Docker Container Requirements:**
- **Base Image**: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (Ubuntu Chiseled - distroless-like, officially supported)
  - Ultra-minimal (no package manager, shell, or unnecessary tools)
  - Non-root by default (app user)
  - CVE scanning built-in
  - Smaller than Alpine with better .NET performance
- **Multi-stage Build**: Build stage (SDK) + runtime stage (Chiseled ASP.NET)
- **Security**: Non-root user (built-in), read-only root filesystem where possible
- **Size**: Target < 200MB (~120-150MB for Chiseled + app, smaller than Alpine)
- **Health Checks**: `/health` endpoint monitoring
- **Logging**: Structured JSON logging to stdout
- **Environment**: All config via environment variables (12-factor)
- **Volumes**: `/data` for database, `/data/images` for photos, `/data/keys` for Data Protection

**Docker Compose Features:**
- PostgreSQL 17 with persistent volume
- TelegramGroupsAdmin app with restart policy
- Network isolation (internal network for DB)
- Environment variable template (.env.example)
- Init scripts for first-time setup
- Health check dependencies (app waits for DB)

**Documentation & Packaging:**
- Comprehensive README with quick start (docker-compose up)
- Environment variable documentation
- Migration guide (fresh install + upgrade paths)
- Troubleshooting guide (common Docker issues)
- Contributing guidelines
- License file (MIT recommended for broad adoption)
- Security policy (responsible disclosure)

**Deliverable**: Broad community appeal (curated, crypto, large public, self-hosted), security-audited, production-ready for open source release with one-command deployment

---

### **Milestone 5: Full Platform with Analytics** (~35-42 days from now)
**Target Date**: ~November 28, 2025
**Estimated Effort**: 80-96 hours (10-12 days beyond Open Source Ready)

**Phase 5 Features (Optional):**
| Feature | Priority | Estimate | Rationale |
|---------|----------|----------|-----------|
| 5.1 Analytics Repo | Medium | 16h (2d) | Time-series queries, false positive/negative rates |
| 5.2 TickerQ Aggregation | Medium | 16h (2d) | Daily stats job, weekly/monthly rollups |
| 5.3 Analytics UI | Medium | 16-24h (2-3d) | Trends charts, performance metrics, confidence analysis |
| 5.4 Charting Library | Low | 8h (1d) | MudBlazor Charts or ApexCharts integration |
| 5.5 Auto-Trust System | High | 16-24h (2-3d) | 7 days + 50 clean messages, UI approval workflow |
| 5.6 Forwarded Spam | Medium | 16h (2d) | Channel blacklist, mass-forward detection |
| 5.10 Smart Multi-Language | Low | 24-32h (3-4d) | Language detection, helpful DMs, template system |

**Deliverable**: Data-driven insights and automated trust building

---

### **Phase 6 ML Insights** (Optional, ~50-60 days from now)
**Target Date**: ~December 10, 2025
**Estimated Effort**: 40-56 hours (5-7 days beyond Full Platform)
Automated recommendations, pattern detection, ML clustering, insights dashboard
*Note: Optional enhancement, not required for open source release*

---

## Summary Projections

**Velocity Reality Check:**
- **Completed**: 73% in 11 days = 6.6% per day
- **Remaining to MVP**: 27% ÷ 6.6% = **~4 days** at current pace
- **BUT**: Remaining work is mostly polish/ease-of-use, not complex architecture
- **Foundation complete**: Auth, DB, bot, 9 algorithms, actor system, notes/tags ✅

**Actual MVP Requirements:**
| Feature | Type | Status | Notes |
|---------|------|--------|-------|
| 4.10 Anti-Impersonation | ✅ COMPLETE | Done | Composite scoring, pHash, unified queue UI |
| 4.11 Warning System | ✅ COMPLETE | Done | Count-based, auto-ban, UI removal, configurable threshold |

**Realistic Timeline:**
- **MVP (Production-Ready)**: ✅ **COMPLETE (Oct 17)**
  - Core moderation complete with impersonation protection
  - Warning system with auto-ban and UI removal
  - Everything needed for live deployment

- **Feature-Complete**: ~12-15 days from now (**Target: ~Nov 1**)
  - Filter engine, appeals, report aggregation
  - All Phase 4 features complete

- **Security Audit & Refactor Analysis**: ~18-22 days from now (**Target: ~Nov 8**)
  - **CRITICAL GATE**: Full security review (auth, input validation, API security, data protection)
  - **CRITICAL GATE**: Final refactor analysis (performance, duplication, complexity, modern C# patterns)
  - Production-hardened, security-validated codebase
  - Uses `security-reviewer` and `dotnet-refactor-advisor` agents

- **Open Source Ready**: ~25-30 days from now (**Target: ~Nov 15**)
  - .NET 10 RC2 update (latest runtime)
  - Production Docker container (Ubuntu Chiseled, distroless-like, < 150MB)
  - Docker Compose with PostgreSQL (one-command deployment)
  - OpenAI-compatible API (local LLM support - removes API barrier)
  - Bio spam check, raid detection
  - Comprehensive documentation (README, quick start, troubleshooting)

- **Full Vision with Analytics**: ~35-42 days from now (**Target: ~Nov 28**)
  - Phase 5 analytics dashboard
  - Auto-trust system
  - Optional enhancements

**With Buffer for New Ideas & Quality**: Add 20-30% for experimentation + polish
- **MVP**: 10-12 days (comfortable)
- **Feature-Complete**: 18-20 days (comfortable)
- **Security-Audited**: 25-28 days (comfortable, includes quality gates)
- **Open Source**: 32-38 days (comfortable, battle-tested)
- **Full Platform**: 45-55 days (comfortable, complete vision)

**Achievement Acknowledgment:**
This 11-day velocity delivering a production-quality platform with 73% completion represents exceptional development speed. The hard architectural work (auth, database, bot infrastructure, spam algorithms, actor system) is complete. Remaining work is incremental additions to a solid foundation.

---

## Recommended Execution Order

**Week 1-2** (Days 1-4): Core Moderation ✅ COMPLETE
1. 4.10 Anti-Impersonation ✅ COMPLETE
2. 4.11 Warning System ✅ COMPLETE

**Week 3-4** (Days 9-20): Enhanced Features
3. 4.16 OpenAI-Compatible (2d) - Critical for open source
4. 4.13 Filter Engine (4d) - Advanced spam control
5. 4.14 Report Aggregation (2d) - Improves admin workflow
6. 4.18 Forum/Topics (1d) - Quick compatibility fix

**Week 5** (Days 21-29): Open Source Polish
7. 6.3 Bio Spam Check (2d) - High-value protection
8. 6.2 Raid Detection (4d) - Crypto/large group protection
9. Documentation polish, README, Docker compose
10. Testing on fresh install, edge case validation

**Week 6-7** (Days 30-43): Analytics & Appeal System
11. 4.15 Appeal System (5d) - Completes moderation loop
12. Phase 5 Analytics (12d) - Data-driven insights
13. 6.4 Scheduled Messages (3d) - Professional feature

**Flexible Items** (deprioritize if timeline pressure):
- 4.9 Bot Connection Management (nice-to-have, not critical)
- 4.17 Additional Media Types (can add post-launch)
- 5.10 Smart Multi-Language (value-add, not core)
- 6.4 Scheduled Messages (professional polish)

**Next Steps**:
Immediate: Move to Milestone 2 (Feature-Complete) - 4.16 OpenAI-Compatible API (1-2d critical) or 4.13 Filter Engine (3-4d)

**Critical Issues**: C1 Fire-and-Forget✅ (Task.Run→TickerQ, WelcomeTimeoutJob+DeleteMessageJob+FetchUserPhotoJob, persistence/retry/logging), MH1+MH2✅ (GetStatsAsync 2→1 query 80% faster, CleanupExpiredAsync 3→1 query 50% faster, single-query GroupBy), H1+H2✅ (ChatPermissions static helpers, magic numbers→database config, no migration needed)

**CRITICAL RULES**:
- Never run app in normal mode (only one instance allowed, user runs in Rider for debugging)
- Testing: Use `dotnet run --migrate-only` to catch startup issues after building
- Always let user test manually - complex app, cannot validate runtime behavior automatically
