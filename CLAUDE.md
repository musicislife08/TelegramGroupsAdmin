# TelegramGroupsAdmin - AI Reference

## Stack
.NET 10.0, Blazor Server, MudBlazor 8.13.0, PostgreSQL 17, EF Core 10, TickerQ 2.5.3, OpenAI API, VirusTotal, SendGrid

## Projects
- **TelegramGroupsAdmin**: Main app, Blazor+API, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob, FetchUserPhotoJob)
- **TelegramGroupsAdmin.Configuration**: Config IOptions classes, AddApplicationConfiguration()
- **TelegramGroupsAdmin.Data**: EF Core DbContext, migrations, Data Protection (internal to repos)
- **TelegramGroupsAdmin.Telegram**: Bot services, 9 commands, repos, orchestrators, AddTelegramServices()
- **TelegramGroupsAdmin.Telegram.Abstractions**: TelegramBotClientFactory, job payloads (breaks circular deps)
- **TelegramGroupsAdmin.SpamDetection**: 9 spam algorithms, self-contained, database-driven

## Architecture Patterns
**Extension Methods**: ServiceCollectionExtensions (AddBlazorServices, AddCookieAuthentication, AddApplicationServices, AddHttpClients, AddTelegramServices, AddRepositories, AddTgSpamWebDataServices, AddTickerQBackgroundJobs), WebApplicationExtensions (ConfigurePipeline, MapApiEndpoints, RunDatabaseMigrationsAsync), ConfigurationExtensions (AddApplicationConfiguration)

**Layered**: UI Models (Blazor DTOs) → Repositories (conversion) → Data Models (DB internal). ModelMappings.cs: .ToModel()/.ToDto() extensions. Repos return/accept UI models only.

**Spam Detection**: ISpamDetectorFactory orchestrates 9 checks (StopWords, CAS, Similarity/TF-IDF, Bayes, MultiLanguage, Spacing, OpenAI, ThreatIntel/VirusTotal, Image/Vision), confidence aggregation, OpenAI veto, self-learning

**Background Services** (composition pattern):
1. TelegramAdminBotService - Bot polling, update routing (5 types), command registration, IMessageHistoryService
2. MessageProcessingService - New messages, edits, image download, spam orchestration, URL extraction, user photo scheduling
3. ChatManagementService - MyChatMember, admin cache, health checks, chat names
4. SpamActionService - Training QC, auto-ban (cross-chat), borderline reports
5. CleanupBackgroundService - Message retention (keeps spam/ham samples)

## Database (PostgreSQL)
**DB**: telegram_groups_admin, 18 tables
**Migrations**: InitialSchema.cs (validated), Latest: AddUserPhotoPathToMessages

**Core Tables**:
- messages: message_id(PK), chat_id, user_id, user_name, user_photo_path, chat_name, chat_icon_path, timestamp, edit_date, message_text, photo*, urls, content_hash. Retention: 180d (except spam/ham refs)
- detection_results: id(PK), message_id(FK cascade), detected_at, detection_source(auto/manual), is_spam(computed: net_confidence>0), confidence, net_confidence, reason, detection_method, added_by, used_for_training, check_results_json, edit_version. Retention: permanent
- message_edits: id(PK), message_id(FK cascade), edit_date, old/new text/hash
- user_actions: id(PK), user_id, action_type(ban/warn/mute/trust/unban), message_id, issued_by, issued_at, expires_at, reason. Global cross-chat
- welcome_responses: id(PK), chat_id, user_id, username, welcome_message_id, response(accepted/denied/timeout/left), responded_at, dm_sent, dm_fallback

**Config Tables**:
- stop_words: id(PK), word, word_type(0=message/1=username/2=userID), added_date, source, enabled, added_by, detection_count, last_detected_date
- spam_detection_configs: chat_id(PK), min_confidence_threshold, enabled_checks, custom_prompt, auto_ban_threshold
- spam_check_configs: check_name(PK), enabled, confidence_weight, config_json
- configs: id(PK), chat_id(nullable for global), config_type, config_json(JSONB). Pattern: global (chat_id=NULL) + per-chat overrides

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
**2.1**: 9 spam algorithms, SpamDetectorFactory, normalized schema, self-improving, production-ready
**2.2**: Schema normalization, removed training_samples/spam_checks, created detection_results/user_actions, migrated data
**2.3**: Training data (191 spam + 26 ham, 11 stop words), Latin detection, OpenAI veto optimization, performance (<100ms cached)
**2.4**: Bot commands (9: /help /report /spam /ban /trust /unban /warn /delete /link), permissions, /reports UI, @admin mentions, message history+edit tracking
**2.5**: Backup/restore (gzip JSON 81% compression), all 18 tables, Data Protection for TOTP, CLI flags, FK-aware deletion
**2.6**: Weighted voting (net confidence), asymmetric confidence, two-tier decision (net>50 OR max>85→OpenAI veto, net 0-50→reports, <0→allow), training QC (OpenAI 85%+ or net>80), Messages UI (mark spam/ham, detection history)
**2.7**: Auto-reports (net +0 to +50), auto-ban (net>50 + OpenAI 85%+), unban logic, Telegram API actions, cross-chat enforcement, edit re-scanning, refactoring (1,243→4 services)
**2.8**: Photo caching - Chat icons (64x64), user photos (FetchUserPhotoJob via TickerQ, 64x64), MessageBubbleTelegram dual-avatar (large chat icon + small inline user photo), service message filtering, delete button. TickerQ exception pattern (all jobs re-throw)

### Phase 3: Advanced Multi-Chat ✅
Cross-chat spam detection (global bans), shared blacklist (user_actions, stop_words), global auto-apply
Not implemented: Chat delegation, templates, bulk UI (already automatic)

### Phase 4: Infrastructure & Configuration ⏳
**4.1** ✅: TickerQ PostgreSQL job queue
**4.2** ✅: Unified configs table (JSONB, chat_id NULL=global), IConfigService (Save/Get/GetEffective/Delete), auto-merging
**4.2.1** ✅: 36 timestamps bigint→timestamptz, DTOs→DateTimeOffset, TickerQ configs, 228 messages preserved
**4.3** ✅: TelegramGroupsAdmin.Telegram library, moved 6 models+13 repos+services+9 commands+4 background services, AddTelegramServices(), ModelMappings public, clean dependencies
**4.4** ✅: Welcome system - DB schema, WelcomeService, Repository, bot integration, config from DB, chat name caching, TickerQ jobs (WelcomeTimeoutJob, DeleteMessageJob), callback validation, user leaves, /settings#telegram. C1 RESOLVED: Fire-and-forget Task.Run→TickerQ. TickerQ arch: jobs in main app, payloads in Abstractions. Flow: restrict on join→welcome+buttons→timeout auto-kick (60s)→accept (restore+DM/fallback)→deny/timeout (kick). Templates: chat_welcome, dm_template, chat_fallback. Variables: {chat_name}, {username}, {rules_text}
**4.5** ✅: OpenAI tri-state (spam/clean/review), SpamCheckResultType enum, modular prompts (technical+custom rules+mode guidance), review queue integration. Prompt arch: GetBaseTechnicalPrompt (JSON, unchangeable), GetDefaultRulesPrompt (spam/legit indicators, user-overridable), GetModeGuidancePrompt (veto vs detection), BuildSystemPrompt factory
**4.8** ✅: Settings UI - All tabs implemented. General (App+MessageHistory env display), Integrations (OpenAI/SendGrid/VirusTotal/Telegram status+masked keys), Notifications (future features), Security (auth/password/audit/API/data protection), Logging (Phase 4.7 placeholder+troubleshooting). Read-only (env configured)

**Pending**:
**4.6**: /tempban (5min/1hr/24hr presets, Telegram until_date auto-unrestrict, user_actions audit, UI integration)
**4.7**: /settings#logging (dynamic log levels, configs JSONB, ILoggerFactory immediate application)
**4.9**: Bot connection management - Hot-reload bot, IBotLifecycleService, ReconnectAsync/DisconnectAsync, CancellationTokenSource, BotConnectionManager.razor, /settings#bot-connection, persist state in configs, Owner-only
**4.10**: Anti-Impersonation - Name similarity (Levenshtein+visual), photo hash (pHash/ImageSharp), admin/channel name protection, auto-restrict suspicious, review queue /reports, side-by-side comparison, scam patterns (_support, _admin, _official)
**4.11**: Warning/Points System - 0-100 scale, auto-escalation (20pts=5min mute, 50pts=1hr, 75pts=24hr, 100pts=ban), point decay (-10pts/week), multi-source (spam, manual /warn, reports), user DM notifications, /warnings command, UI in Messages/Reports
**4.12**: Admin Notes & Tags - Reply-based /note (bot deletes, stores admin_notes), /tag @user suspicious/verified, visible in Messages UI badges, tag filtering, confidence impact (verified=-20, suspicious=+10), audit trail, user modal history
**4.13**: Advanced Filter Engine - custom_filters table (pattern regex, action, enabled, hit_count), chat-specific/global, domain blacklist/whitelist, phrase normalization, URL patterns, 12th spam check in SpamDetectorFactory, /spam#filters UI CRUD, confidence weighting integration
**4.14**: Report Aggregation - Multi-report auto-escalation (3 unique in 1hr→action), reports tracking (message_id, reported_by, timestamp), confidence boost (+15/report), reporter accuracy scoring, false report protection (<60% accuracy→downweight, 10+ false→remove permission), /reports#analytics top reporters
**4.15**: Appeal System - Welcome requires bot DM start (Accept→bot DM→unrestrict), establishes DM channel, banned users submit appeals via DM, appeals queue /reports#appeals (user history, ban reason, detection, appeal text), approve/deny+reason, max 2 appeals/ban, 30-day expiration, appeals_history verdicts

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

## Current Status (Oct 2025)
**Completion**: ~72% overall, ~96% core features
- Phase 1: 100% ✅
- Phase 2: 100% ✅ (2.1-2.8 complete)
- Phase 3: 100% ✅
- Phase 4: 40% (4.1-4.5, 4.8 complete; 4.6-4.7, 4.9-4.15 pending)
- Phases 5-7: 0% (future/optional)

**Production Ready**: Migration+Backup✅ (InitialSchema consolidated, validated, backup/restore+Data Protection, topological sort, sequence resets, strict DTO validation), Build Quality✅ (0 errors/warnings), System Ready: Fresh DB init (`dotnet run --migrate-only`), cross-machine backup/restore+TOTP, production deployment

**Next Steps**:
Immediate (1-2 weeks): 4.10 Anti-Impersonation (3-4d, real attack vector), 4.11 Warning/Points (3-4d, foundational), 4.6 /tempban (2-3h, quick win)
Short Term (next month): 4.13 Filter Engine (3-4d, tg-spam Lua features), 4.12 Admin Notes (2-3d, surgical intelligence), 4.14 Report Aggregation (2d, enhances reports), 4.7 Runtime log config (4-6h, troubleshooting)
Medium Term (after Phase 4): 4.15 Appeal System (4-5d, completes moderation loop), Refactoring review (REFACTORING_BACKLOG.md), 5.5/5.6/5.10 (conditional/lower priority)
Long Term: Phase 5 Analytics, Phase 6 ML Insights (optional, value-add)

**Critical Issues**: C1 Fire-and-Forget✅ (Task.Run→TickerQ, WelcomeTimeoutJob+DeleteMessageJob+FetchUserPhotoJob, persistence/retry/logging), MH1+MH2✅ (GetStatsAsync 2→1 query 80% faster, CleanupExpiredAsync 3→1 query 50% faster, single-query GroupBy), H1+H2✅ (ChatPermissions static helpers, magic numbers→database config, no migration needed)

**Recommended Order**: Comprehensive review agent → Optional M1-M4 (readability) → Phase 4.6 (/tempban)

**CRITICAL RULES**:
- Never run app in normal mode (only one instance allowed, user runs in Rider for debugging)
- Testing: Use `dotnet run --migrate-only` to catch startup issues after building
- Always let user test manually - complex app, cannot validate runtime behavior automatically
