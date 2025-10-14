# CLAUDE.md - TelegramGroupsAdmin

ASP.NET Core 10.0 Blazor Server + Minimal API. Telegram spam detection (text + image). PostgreSQL database.

## Tech Stack
- .NET 10.0 (preview)
- Blazor Server (MudBlazor v8.13.0 - latest 2025)
- PostgreSQL 17 + Npgsql
- Dapper + FluentMigrator
- Cookie auth + TOTP 2FA
- VirusTotal API, OpenAI Vision API
- SendGrid email service

## Solution Structure

### Projects

**TelegramGroupsAdmin** (main application)
- Blazor Server UI + Minimal API endpoints
- 108-line Program.cs with extension method architecture
- Service registrations via `ServiceCollectionExtensions`
- Pipeline configuration via `WebApplicationExtensions`

**TelegramGroupsAdmin.Configuration** (shared configuration library)
- All configuration option classes (`AppOptions`, `OpenAIOptions`, `TelegramOptions`, etc.)
- `AddApplicationConfiguration()` extension method
- Shared across all projects for consistent configuration

**TelegramGroupsAdmin.Data** (data access layer)
- Database models and DTOs
- FluentMigrator migrations
- Data Protection services
- Internal to repositories - UI uses UI models instead

**TelegramGroupsAdmin.SpamDetection** (spam detection library)
- 9 spam detection algorithms
- Self-contained, reusable library
- Database-driven configuration

### Extension Method Architecture

**ServiceCollectionExtensions.cs** - Service registration:
- `AddBlazorServices()` - Blazor Server + MudBlazor + HttpClient
- `AddCookieAuthentication()` - Cookie auth with security settings
- `AddApplicationServices()` - Auth, users, messages, email
- `AddHttpClients()` - HTTP clients with rate limiting
- `AddTelegramServices()` - Bot commands and background services
- `AddRepositories()` - All repositories and orchestrators
- `AddTgSpamWebDataServices()` - Data Protection + Identity repos

**WebApplicationExtensions.cs** - Pipeline configuration:
- `ConfigurePipeline()` - Standard middleware setup
- `MapApiEndpoints()` - API endpoint mapping
- `RunDatabaseMigrationsAsync()` - Database migrations

**ConfigurationExtensions.cs** - Configuration binding:
- `AddApplicationConfiguration()` - Binds all IOptions from environment variables

## Architecture

### Spam Detection Library (Enhanced) ✅

**Comprehensive multi-algorithm system** based on proven tg-spam implementation with modern enhancements:

#### **Core Architecture**
- **`ISpamDetectorFactory`** - Central orchestration with confidence aggregation
- **9 specialized spam checks** - Database-driven, self-improving algorithms
- **OpenAI veto system** - AI-powered false positive prevention
- **Continuous learning** - Automatic pattern updates and training sample collection

#### **Detection Algorithms**
1. **StopWords** - Database-driven keyword detection (username/userID/message)
2. **CAS** - Combot Anti-Spam global database with caching
3. **Similarity** - TF-IDF vectorization with early exit optimization
4. **Bayes** - Self-learning Naive Bayes with certainty scoring
5. **MultiLanguage** - OpenAI translation-based foreign language detection
6. **Spacing** - Artificial spacing pattern detection (core ratios + invisible chars)
7. **OpenAI** - GPT-powered veto with message history context + JSON responses
8. **ThreatIntel** - VirusTotal + Google Safe Browsing URL analysis
9. **Image** - OpenAI Vision spam detection for images

#### **Database Schema (PostgreSQL)** - See detailed schema section below

#### **Key Features**
- **Self-improving**: Learns from detections to improve accuracy
- **Database-driven**: All patterns and settings manageable via UI
- **Performance optimized**: Caching, early exit, efficient queries
- **Fail-open design**: Prevents false positives, maintains reliability
- **Multi-chat support**: Per-chat configurations and custom prompts
- **Telegram API alignment**: Uses "chat" terminology consistently (chats, groups, supergroups)

### Services

#### **Spam Detection Services**
- `ISpamDetectorFactory` - Main spam detection orchestration and result aggregation
- `ITokenizerService` - Shared text preprocessing (emoji removal, tokenization)
- `IOpenAITranslationService` - Foreign language detection and translation
- `IMessageHistoryService` - Message context retrieval for enhanced AI analysis
- `IStopWordsRepository` - Database management for stop words with UI support
- `ISpamSamplesRepository` - Similarity pattern storage with usage tracking
- `ITrainingSamplesRepository` - Bayes training data with continuous learning
- `ISpamCheck` implementations - 9 specialized spam detection algorithms

#### **Core Application Services**
- `IThreatIntelService` - VirusTotal integration with rate limiting
- `IVisionSpamDetectionService` - OpenAI Vision spam detection with rate limiting
- `ITelegramImageService` - Download images from Telegram
- `IAuthService` - Login, TOTP, password reset
- `IIntermediateAuthService` - Temp tokens for 2FA flow (5min expiry)
- `IInviteService` - Invite token management
- `IUserManagementService` - User CRUD, 2FA reset
- `IMessageExportService` - CSV/JSON export
- `IEmailService` - SendGrid email abstraction
- `IReportActionsService` - Handle admin actions on user reports (spam/ban/warn/dismiss)
- `AdminMentionHandler` - Detect and notify admins when @admin is mentioned

### Layered Architecture & Data Model Separation ✅

**Modern 3-tier architecture** with complete UI/Data separation:

#### **Architecture Layers**

1. **UI Models** (`TelegramGroupsAdmin/Models/`) - Clean DTOs for Blazor components
2. **Repositories** (`TelegramGroupsAdmin/Repositories/`) - Data access with conversion layer
3. **Data Models** (`TelegramGroupsAdmin.Data/Models/`) - Database DTOs (internal to Data layer)

#### **Key Benefits**

- ✅ **Database Independence** - UI never references database structure directly
- ✅ **Type Safety** - Compile-time checking prevents Data/UI model confusion
- ✅ **Single Responsibility** - Repositories handle all Data ↔ UI conversion
- ✅ **Maintainability** - Database changes only require updating DTOs, repositories, and mappings

#### **Conversion Layer**

- **ModelMappings.cs** - Extension methods for bidirectional conversion
  - `.ToUiModel()` - Converts Data models → UI models
  - `.ToDataModel()` - Converts UI models → Data models
- **Repository Pattern** - All repos return/accept UI models, convert internally
- **Enum Alignment** - UI and Data enums share same values for simple casting

#### **File Organization**

```
TelegramGroupsAdmin/
├── Models/                          # UI Models (what Blazor uses)
│   ├── UserModels.cs               # Users, Invites, Audit, Enums
│   ├── MessageModels.cs            # Messages, Edits, History
│   ├── SpamDetectionModels.cs      # Spam samples, training data
│   └── VerificationModels.cs       # Email/password tokens
├── Repositories/                    # Data access layer
│   ├── ModelMappings.cs            # Data ↔ UI conversions
│   ├── UserRepository.cs           # Returns UI.UserRecord
│   ├── MessageHistoryRepository.cs # Returns UI.MessageRecord
│   └── ...                         # All repos return UI models
└── Services/                        # Business logic
    └── ...                         # Use UI models exclusively

TelegramGroupsAdmin.Data/
└── Models/                          # Data Models (database DTOs)
    ├── UserRecord.cs               # Database DTOs + Dapper mappings
    ├── MessageRecord.cs            # Snake_case → PascalCase conversion
    └── ...                         # Internal to Data layer only
```

## Database Schema (PostgreSQL)

**Single PostgreSQL database:** `telegram_groups_admin`
**Single consolidated migration:** `202601100_InitialSchema.cs` (18 tables, validated against known good schema)

### Core Tables (Normalized Design)

#### **messages** - Central message storage
```sql
CREATE TABLE messages (
    message_id BIGINT PRIMARY KEY,           -- Telegram message ID
    chat_id BIGINT NOT NULL,                 -- Telegram chat ID
    user_id BIGINT NOT NULL,                 -- Telegram user ID
    user_name TEXT,                          -- Username (cached)
    timestamp BIGINT NOT NULL,               -- Unix timestamp
    message_text TEXT,                       -- Message content
    photo_file_id TEXT,                      -- Telegram file ID
    photo_file_size INT,                     -- Photo size in bytes
    photo_local_path TEXT,                   -- Downloaded photo path
    photo_thumbnail_path TEXT,               -- Thumbnail path
    urls TEXT,                               -- Extracted URLs
    content_hash VARCHAR(64),                -- MD5 hash for deduplication
    chat_name TEXT,                          -- Chat name (cached)
    edit_date BIGINT                         -- Last edit timestamp (NULL if never edited)
);
```
**Retention:** Configurable (default 180 days), except messages referenced by `detection_results` or `user_actions`

#### **detection_results** - Spam/ham classifications
```sql
CREATE TABLE detection_results (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES messages(message_id) ON DELETE CASCADE,
    detected_at BIGINT NOT NULL,             -- When detection occurred
    detection_source TEXT NOT NULL,          -- 'auto' | 'manual'
    is_spam BOOLEAN NOT NULL,                -- true=spam, false=ham (unban/false positive)
    confidence INT,                          -- 0-100 confidence score
    reason TEXT,                             -- Human-readable detection reason
    detection_method TEXT,                   -- 'StopWords' | 'Bayes' | 'Manual' | etc
    added_by TEXT REFERENCES users(id)       -- Who classified it (NULL for auto)
);
```
**Purpose:**
- Spam detection history (for analytics)
- Bayes training data (bounded query: recent 10k + all manual)
- False positive tracking (is_spam=false)
**Retention:** Permanent (never cleaned up)

#### **message_edits** - Edit history audit trail
```sql
CREATE TABLE message_edits (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES messages(message_id) ON DELETE CASCADE,
    edit_date BIGINT NOT NULL,               -- When edit occurred
    previous_text TEXT,                      -- Text before edit
    previous_content_hash VARCHAR(64)        -- Hash before edit
);
```
**Purpose:** Track message edits (spam tactic: post innocent message, edit to spam later)
**Retention:** Cascades with messages table

#### **user_actions** - Moderation actions (bans, warns, mutes)
```sql
CREATE TABLE user_actions (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL,                 -- Telegram user ID
    chat_ids BIGINT[],                       -- NULL=all chats, []=specific chats
    action_type TEXT NOT NULL,               -- 'ban' | 'warn' | 'mute' | 'trust' | 'unban'
    message_id BIGINT REFERENCES messages(message_id) ON DELETE SET NULL,
    issued_by TEXT REFERENCES users(id),     -- Admin who issued action
    issued_at BIGINT NOT NULL,               -- When action was taken
    expires_at BIGINT,                       -- NULL=permanent, else temp ban/mute
    reason TEXT                              -- Why action was taken
);
```
**Purpose:** Cross-chat moderation actions
**Retention:** Permanent

**User Whitelisting (Trust Action):**
- Action type `trust` marks user as trusted (bypasses all spam checks)
- Applied per-chat or globally (NULL chat_ids = all chats)
- Manual: Admin uses `/trust` command or UI
- Auto-trust (future): After X non-spam messages in Y days (configurable threshold)
  - Suggestion: 10 messages over 7 days with 0 spam flags
  - Revocable if spam detected after trust granted
  - Analytics: Track trust accuracy (% of trusted users who later spam)

### Configuration Tables

#### **stop_words** - Keyword blocklist
```sql
CREATE TABLE stop_words (
    id BIGSERIAL PRIMARY KEY,
    word TEXT NOT NULL,
    word_type INT NOT NULL,                  -- 0=message, 1=username, 2=userID
    added_date BIGINT NOT NULL,
    source TEXT NOT NULL,                    -- 'manual' | 'auto' | 'imported'
    enabled BOOLEAN DEFAULT true,
    added_by TEXT REFERENCES users(id),
    detection_count INT DEFAULT 0,           -- Usage tracking
    last_detected_date BIGINT
);
```

#### **spam_detection_configs** - Per-chat detection settings
```sql
CREATE TABLE spam_detection_configs (
    chat_id TEXT PRIMARY KEY,
    min_confidence_threshold INT DEFAULT 85,
    enabled_checks TEXT[],                   -- Which algorithms to run
    custom_prompt TEXT,                      -- OpenAI custom instructions
    auto_ban_threshold INT DEFAULT 95,       -- Auto-ban at this confidence
    created_at BIGINT NOT NULL,
    updated_at BIGINT
);
```

#### **spam_check_configs** - Algorithm-specific settings
```sql
CREATE TABLE spam_check_configs (
    check_name TEXT PRIMARY KEY,
    enabled BOOLEAN DEFAULT true,
    confidence_weight INT DEFAULT 100,       -- Confidence multiplier
    config_json TEXT,                        -- Algorithm-specific settings
    updated_at BIGINT
);
```

### Identity & Auth Tables

#### **users** - Web UI users (not Telegram users)
```sql
CREATE TABLE users (
    id TEXT PRIMARY KEY,                     -- GUID
    email TEXT NOT NULL UNIQUE,
    normalized_email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    security_stamp TEXT NOT NULL,
    permission_level INT NOT NULL,           -- 0=ReadOnly, 1=Admin, 2=Owner
    invited_by TEXT REFERENCES users(id),
    is_active BOOLEAN DEFAULT true,
    totp_secret TEXT,
    totp_enabled BOOLEAN DEFAULT false,
    totp_setup_started_at BIGINT,
    created_at BIGINT NOT NULL,
    last_login_at BIGINT,
    status INT NOT NULL,                     -- 0=Pending, 1=Active, 2=Disabled, 3=Deleted
    modified_by TEXT,
    modified_at BIGINT,
    email_verified BOOLEAN DEFAULT false,
    email_verification_token TEXT,
    email_verification_token_expires_at BIGINT,
    password_reset_token TEXT,
    password_reset_token_expires_at BIGINT
);
```

#### **invites** - Invite token system
```sql
CREATE TABLE invites (
    token TEXT PRIMARY KEY,
    created_by TEXT NOT NULL REFERENCES users(id),
    created_at BIGINT NOT NULL,
    expires_at BIGINT NOT NULL,
    used_by TEXT REFERENCES users(id),
    permission_level INT NOT NULL,
    status INT NOT NULL,                     -- 0=Pending, 1=Used, 2=Revoked
    modified_at BIGINT
);
```

#### **audit_log** - Security audit trail
```sql
CREATE TABLE audit_log (
    id BIGSERIAL PRIMARY KEY,
    event_type INT NOT NULL,                 -- Enum: Login, Logout, UserCreated, etc
    timestamp BIGINT NOT NULL,
    actor_user_id TEXT REFERENCES users(id), -- Who did it
    target_user_id TEXT REFERENCES users(id),-- Who was affected
    value TEXT                               -- Additional context
);
```

#### **verification_tokens** - Email/password reset tokens
```sql
CREATE TABLE verification_tokens (
    id BIGSERIAL PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id),
    token_type TEXT NOT NULL,                -- 'email_verify' | 'password_reset' | 'email_change'
    token TEXT NOT NULL UNIQUE,
    value TEXT,                              -- New email for email_change
    expires_at BIGINT NOT NULL,
    created_at BIGINT NOT NULL,
    used_at BIGINT
);
```

### Design Principles

1. **Normalized storage** - Message content stored once, referenced by detections/actions
2. **Cascade deletes** - When message deleted, edits cascade; detections/actions remain for analytics
3. **Configurable retention** - Messages cleaned up after N days, unless flagged as spam
4. **Cross-chat support** - All tables support multiple chat_ids
5. **Audit trail** - Complete history of who did what, when

### Background Services
- `TelegramAdminBotService` - Unified Telegram bot (renamed from HistoryBotService)
  - Currently: Message history caching, real-time events
  - Future: Bot commands, spam detection, moderation actions
- `SpamCheckQueueWorker` (planned) - Async spam detection processing
- `CleanupBackgroundService` - Message retention cleanup with smart retention (keeps spam/ham samples)

## Configuration (Env Vars)

### Required
```
VIRUSTOTAL__APIKEY
OPENAI__APIKEY
TELEGRAM__BOTTOKEN
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

### Message Edit Spam Detection
**Tactic**: Users post innocent message, edit to spam hours later when mods offline
**Solution**:
1. On edit event → save current text to `message_edits`
2. Update `messages` table with new text
3. Re-run spam detection on edited content
4. Take action if spam detected

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

### GET /health
```json
{"status": "healthy", "bot": {...stats...}}
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

### Public Pages
- `/login` - Login form (generates intermediate token)
- `/login/verify` - TOTP verification (requires intermediate token)
- `/login/setup-2fa` - Mandatory 2FA setup (requires intermediate token)
- `/register` - Registration with invite token

### Authenticated Pages
- `/` (Home) - Chat health dashboard with daily stats (all users)
- `/analytics` - Deep-dive analytics with tabs (Admin/Owner only):
  - `#spam` - Spam detection statistics and trends (SpamAnalytics component)
  - `#trends` - Message volume trends (stub)
  - `#performance` - Detection accuracy metrics (stub)
- `/messages` - Message viewer, filters, CSV/JSON export, real-time updates (all users)
- `/spam` - Spam management with tabs (Admin/Owner only):
  - `#stopwords` - Stop words management (StopWords component)
  - `#training` - Bayes training data management (TrainingData component)
- `/users` - User management, invite system, 2FA reset (Admin/Owner only)
- `/reports` - User-submitted reports queue with action buttons (Admin/Owner only)
- `/audit` - Audit log viewer (Admin/Owner only)
- `/settings` - Application settings with tabs (Admin/Owner only):
  - `#spam` - Spam detection configuration (SpamDetectionConfig component)
  - `#general` - General settings (stub)
  - `#telegram` - Telegram bot settings (stub)
  - `#notifications` - Notification settings (stub)
  - `#security` - Security settings (stub)
  - `#integrations` - Third-party integrations (stub)
- `/profile` - Password change, TOTP enable/reset, Telegram account linking (all users)

### UI Features
- **URL Fragment Navigation** - All tabbed pages support direct linking (e.g., `/spam#training`)
- **Navigation Menu** - Logical grouping with user section at bottom (Profile/Logout)
- **Top Bar** - Displays logged-in user email in top-right corner
- **Component Reuse** - Spam pages converted to reusable components in `Components/Shared/SpamManagement/`

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

## Architecture Evolution ✅

### **Current State: Comprehensive Spam Detection Library**
**TelegramGroupsAdmin now includes**:
- ✅ **Complete spam detection library** - 9 algorithms based on proven tg-spam implementation
- ✅ **Self-improving system** - Continuous learning with database-driven patterns
- ✅ **Multi-group support** - Per-group configurations and custom rules
- ✅ **Advanced AI integration** - OpenAI veto system with message history context
- ✅ **Production-ready** - Comprehensive error handling, caching, performance optimization

### **Integration Options**

**Option 1: Enhanced Service Mode (Current)**
- TelegramGroupsAdmin provides advanced `/check` endpoint
- tg-spam calls our enhanced API for superior detection
- Benefit: Immediate upgrade with minimal tg-spam changes

**Option 2: Native Telegram Bot (Future)**
- Replace tg-spam entirely with native C# Telegram bot
- Direct multi-group spam enforcement
- Full UI integration and customization
- Unified codebase and consistent experience

### **Advantages Over Original tg-spam**

1. ✅ **Multi-chat support** - Manage unlimited Telegram chats (private, groups, supergroups)
2. ✅ **Database-driven configuration** - Runtime updates without code changes
3. ✅ **Self-improving detection** - Automatic pattern learning and updates
4. ✅ **Advanced AI integration** - Context-aware OpenAI with fallback systems
5. ✅ **Comprehensive UI** - Full visibility and control over spam decisions
6. ✅ **Performance optimizations** - Caching, early exit, efficient algorithms
7. ✅ **Enterprise features** - Audit trails, user management, role-based access
8. ✅ **Consistent terminology** - Aligned with Telegram Bot API (chat_id everywhere)

## Build Quality ✅

### **Perfect Build Achievement (January 2025)**
The codebase has achieved **0 errors, 0 warnings** through systematic modernization:

- ✅ **158+ build errors** → **0 errors** (all compilation issues resolved)
- ✅ **62+ warnings** → **0 warnings** (all async, nullable, MudBlazor analyzer warnings fixed)
- ✅ **MudBlazor v8.13.0** - Updated to latest 2025 API standards
- ✅ **Modern patterns** - Records converted to mutable classes for Blazor binding
- ✅ **Triple-verified** - Multiple clean + rebuild cycles confirm no hidden cache issues
- ✅ **Production ready** - Code follows latest C# and Blazor best practices

### **Key Modernizations Applied**
1. **MudBlazor API Updates** - `@bind-SelectedOption` → `@bind-Value` (v8 standards)
2. **Configuration System** - Records → classes for proper two-way binding
3. **Async Patterns** - Removed unnecessary async/await for synchronous operations
4. **Null Safety** - Added proper null checking for all nullable references
5. **Type Safety** - Fixed all generic type inference issues
6. **Telegram API Alignment** - Refactored all "group" terminology to "chat" for consistency with Telegram Bot API
7. **Enum Cleanup** - Removed 13 duplicate AuditEventType values, consolidated to 20 unique values with data migration
8. **UI/UX Improvements** - Three-dot menus, instant filters, proper disabled user login messages

## Troubleshooting
**Telegram bot not caching**: Check TELEGRAM__BOTTOKEN, bot added to chat, privacy mode off
**Image spam failing**: Check OPENAI__APIKEY, /data volume mounted
**DB growing**: Check retention (720h default), cleanup service running
**Rate limits**: Check logs for LogWarning messages from VirusTotalService or OpenAIVisionSpamDetectionService
**Build issues**: Run `dotnet clean && dotnet build` - project maintains 0 errors/warnings standard

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

### Phase 2: Unified Telegram Bot (IN PROGRESS)
**Goal**: Single bot for multi-group admin, moderation, and spam detection

**Reference Documents**:
- **[TG_SPAM_CODEBASE_REFERENCE.md](./TG_SPAM_CODEBASE_REFERENCE.md)** - Technical reference for tg-spam algorithms
- **[SPAM_DETECTION_LIBRARY_REFERENCE.md](./SPAM_DETECTION_LIBRARY_REFERENCE.md)** - API docs for 9 detection algorithms

**Development Phases**:

**Phase 2.1: Core Spam Detection Library** ✅ **COMPLETE**
- [x] **9 spam detection algorithms** - Enhanced versions of all tg-spam checks
- [x] **SpamDetectorFactory** - Central orchestration with confidence aggregation
- [x] **Database schema** - Normalized design (messages, detection_results, user_actions)
- [x] **Self-improving system** - Continuous learning with bounded training queries
- [x] **Shared services** - TokenizerService, OpenAI translation, message history
- [x] **Production-ready** - Error handling, caching, performance optimization

**Phase 2.2: Database Schema Normalization** ✅ **COMPLETE**
- [x] **Normalized schema migration** - FluentMigrator migration `202601086_NormalizeMessageSchema.cs` created and applied
- [x] **Remove obsolete tables** - `training_samples` and `spam_checks` dropped successfully
- [x] **Remove obsolete code** - Deleted `SpamCheckEndpoints.cs`, `SpamCheckRepository.cs`, `SpamCheckService.cs`
- [x] **Schema verified** - `detection_results`, `user_actions` tables created with proper indexes and FKs
- [x] **Data migrated** - Training samples migrated to `detection_results` with synthetic message records
- [x] **Update repositories** - All repositories updated (TrainingSamplesRepository, MessageHistoryRepository)
- [x] **Model consistency** - All DTOs use init-only properties, removed `expires_at` field
- [x] **Type corrections** - Fixed `chat_ids` type (string[] → long[]), column names (details → reason)
- [x] **Update spam checks** - BayesSpamCheck bounded query (all manual + recent 10k auto samples)
- [x] **Update UI** - SpamAnalytics page queries `detection_results` instead of `spam_checks`

**Phase 2.3: Performance & Production Readiness** ✅ **COMPLETE**
- Imported training data (191 spam + 26 ham samples, 11 stop words)
- Latin script detection to skip OpenAI for English messages
- Model layer separation (UI models completely decoupled from Data models)
- OpenAI veto optimization (only runs for borderline cases)
- Performance: <100ms cached, ~4s first URL check
- 30-day message retention, spam samples preserved

**Phase 2.4: Unified Bot Implementation** ✅ **COMPLETE**
- TelegramAdminBotService with command routing infrastructure
- 7 bot commands: `/help`, `/report`, `/spam`, `/ban`, `/trust`, `/unban`, `/warn`
- Permission system (ReadOnly/Admin/Owner with per-chat Telegram admin caching)
- Telegram user account linking with `/link` command
- Reports system with `/reports` UI page
- @admin mention notifications with HTML text mentions
- Message history caching and edit tracking
- **Remaining:** Command action implementation (delete, ban API calls)

**Phase 2.5: Backup & Restore System** ✅ **COMPLETE**
- Full system backup/restore (gzip JSON, 81% compression)
- Zero-maintenance reflection-based system
- All 18 tables with Data Protection for TOTP secrets
- CLI flags: `--export <path>` / `--import <path>`
- Transaction safety with foreign key-aware deletion order

**Phase 2.6: Confidence Aggregation & Training System** ✅ **COMPLETE**
- Weighted voting: Net confidence = Sum(spam votes) - Sum(ham votes)
- Asymmetric confidence: Simple checks 20% when NOT spam, trained checks full confidence
- Two-tier decision: Net >50 → OpenAI veto, ≤50 → `/reports` queue, <0 → Allow
- Training quality control: Only OpenAI 85%+ or net >80 marked as training-worthy
- Automatic spam detection with result storage
- Messages page: Mark as Spam/Ham buttons + Detection History dialog
- All check results stored as JSON with `used_for_training` flag

**Phase 2.7: Spam Action Implementation** 🔄 **NEXT**
- [ ] Auto-create reports for borderline detections (net +0 to +50)
- [ ] Auto-ban implementation (net >80 after OpenAI confirmation)
- [ ] Unban logic for "Mark as Ham" button
- [ ] Command actions: `/spam`, `/ban`, `/unban`, `/warn` with Telegram API calls
- [ ] Cross-chat ban enforcement
- [ ] Edit monitoring with re-scanning

### Phase 3: Advanced Multi-Chat Features (FUTURE)

- [ ] Chat owner delegation (non-platform admins can manage their chats)
- [ ] Cross-chat spam pattern detection (spammer detected in Chat A → auto-ban in Chats B, C)
- [ ] Shared/global blacklist across all managed chats
- [ ] Chat templates (apply settings from one chat to others)
- [ ] Bulk operations (ban user from all chats, global whitelist)

### Phase 4: Advanced Features (FUTURE)

- [ ] ML-based spam detection (train on historical data)
- [ ] Sentiment analysis for toxicity detection
- [ ] Automated report generation
- [ ] API for third-party integrations

## Next Steps (Prioritized for 2025)

### **Current Priority: Phase 2.7 - Spam Action Implementation** 🎯
Complete the spam detection workflow with automatic actions and cross-chat enforcement.

**Tasks:**
1. Auto-create reports for borderline detections (net +0 to +50) → existing `/reports` page
2. Auto-ban for confident spam (net >80 after OpenAI 85%+ confirmation)
3. Unban logic for "Mark as Ham" button (check `user_actions`, call UnbanChatMember API)
4. Complete bot command actions: `/spam`, `/ban`, `/unban`, `/warn` with Telegram API
5. Cross-chat enforcement (ban across all managed chats)
6. Edit monitoring (re-scan edited messages with `edit_version` tracking)

---

## Production Status (January 2025)

### ✅ **Migration & Backup System Complete**

**Key Achievements:**
1. ✅ **Consolidated migration** - Single `202601100_InitialSchema.cs` creates all 18 tables
2. ✅ **Schema validated** - Matches known good production schema exactly
3. ✅ **Backup/restore system** - Cross-machine support with Data Protection handling
4. ✅ **Build quality** - 0 errors, 0 warnings maintained
5. ✅ **Topological sort** - Proper FK dependency resolution for restore
6. ✅ **Self-referencing FKs** - Trigger disable/enable for circular dependencies

**Recent Fixes:**
- Removed obsolete `spam_samples` table (normalized to `detection_results`)
- Fixed topological sort to handle circular dependencies
- Added `[ProtectedData]` attribute for dynamic encryption handling
- Sequence reset after restore to prevent duplicate key violations
- Strict DTO validation (fails on missing DTOs instead of silent skip)

**System Ready For:**
- Fresh database initialization (`dotnet run --migrate-only`)
- Cross-machine backup/restore with TOTP preservation
- Production deployment
