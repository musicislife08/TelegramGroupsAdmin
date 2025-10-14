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
- [x] **Training data import** - Imported 191 spam + 26 ham samples from tg-spam database
- [x] **Stop words import** - Imported 11 stop words with proper schema alignment
- [x] **Spam detection testing** - Verified Similarity (57%) + Bayes (99%) detection accuracy
- [x] **Latin script detection** - Added Unicode-based check to skip OpenAI for English messages (saves API costs)
- [x] **MinMessageLength optimization** - Lowered from 50 to 10 chars to catch short spam
- [x] **Logging enhancement** - Added Debug-level logging for all spam detection checks
- [x] **Schema alignment** - Fixed StopWordDto mismatch (removed word_type, detection_count fields)
- [x] **Model layer separation** - UI models completely decoupled from Data models with conversion layer
- [x] **OpenAI veto optimization** - Only runs for borderline cases (confidence < 95%)
- [x] **VirusTotal disabled** - Disabled by default for URLs (16s latency), framework ready for file scanning
- [x] **ClamAV preparation** - TODO added for local virus scanning integration
- [x] **Performance metrics** - Spam detection: <100ms (cached), ~4s (first URL check with blocklists)
- [x] **Message retention** - 30-day default retention, messages with detection_results preserved
- [x] **Testing complete** - All pages working, 0 errors, 0 warnings

**Phase 2.4: Unified Bot Implementation** 🔄 **NEXT**
- [x] **Service renamed** - HistoryBotService → TelegramAdminBotService (foundation ready)
- [x] **Command routing infrastructure** - IBotCommand interface, CommandRouter service, singleton architecture
- [x] **Bot command registration** - SetMyCommands API with scoped permissions (default/admin)
- [x] **Command parsing** - Regex handles `/command` and `/command@botname` formats
- [x] **Command stubs complete** ✅ - All 7 essential commands implemented and tested:
  - `/help` - Show available commands (ReadOnly, reflection-based auto-discovery)
  - `/report` - Report message for admin review (ReadOnly)
  - `/spam` - Mark as spam and delete (Admin)
  - `/ban` - Ban user from all managed chats (Admin)
  - `/trust` - Whitelist user to bypass spam detection (Admin)
  - `/unban` - Remove ban from user (Admin)
  - `/warn` - Issue warning with auto-ban threshold (Admin)
- [x] **Permission system foundation** - MinPermissionLevel checks (0=ReadOnly, 1=Admin, 2=Owner)
- [x] **Console logging** - Timestamp format for debugging command execution timing
- [x] **Reflection-based help** ✅ - Dynamic command discovery, auto-updates when new commands added
- [x] **Foundation for command actions** ✅ **COMPLETE** - Infrastructure ready for command implementation:
  1. ✅ **DetectionResultsRepository** - Insert method for manual spam/ham classifications
  2. ✅ **UserActionsRepository** - Track bans/trusts/warns across chats
  3. ✅ **ManagedChatsRepository** - Track which chats the bot manages
  4. ✅ **MyChatMember event handling** - Real-time chat tracking when bot added/removed
  5. ✅ **Enum standardization** - All enums stored as INT (consistent with permission_level, status patterns)
  6. ✅ **Migration 202601087** - Converts user_actions.action_type TEXT→INT, creates managed_chats table
- [x] **Trust system integration** ✅ **COMPLETE**:
  - ✅ **/trust command** - Reply-to-message and username syntax (username lookup requires GetChatMember API)
  - ✅ **Early exit optimization** - Trust check in TelegramAdminBotService before spam detection
  - ✅ **Architecture decision** - Trust checking outside spam library (keeps library pure and reusable)
- [x] **Telegram user account linking** ✅ **COMPLETE**:
  - ✅ **Database schema** - telegram_user_mappings and telegram_link_tokens tables (Migration 202601088)
  - ✅ **/link command** - Token verification with 15min expiry, duplicate prevention
  - ✅ **Permission system** - CommandRouter uses mappings for real-time permission lookup
  - ✅ **Profile page UI** - Generate tokens, view linked accounts, unlink functionality
  - ✅ **Repositories** - TelegramUserMappingRepository, TelegramLinkTokenRepository
  - ✅ **Security** - Cryptographic tokens, one-time use, automatic cleanup of old tokens
  - ✅ **Architecture** - One-to-many (user → Telegram accounts), one-to-one (Telegram → user)
- [x] **Per-chat admin caching** ✅ **COMPLETE**:
  - ✅ **Database schema** - chat_admins table with bidirectional indexes (Migration 202601089)
  - ✅ **ChatAdminsRepository** - Cache lookup, upsert, deactivate, permission checking
  - ✅ **RefreshAllChatAdminsAsync** - Startup cache refresh for all managed chats
  - ✅ **RefreshChatAdminsAsync** - Per-chat admin list caching with detailed logging
  - ✅ **MyChatMember event handling** - Real-time admin promotion/demotion tracking
  - ✅ **Permission hierarchy** - Web app linking (global) → Telegram admin (per-chat) → No permission
  - ✅ **Admin spam bypass** - Chat admins automatically skip spam detection (no explicit trust needed)
  - ✅ **Performance** - Eliminates GetChatMember API calls on every command
- [x] **DI architecture fixes** ✅ **COMPLETE**:
  - ✅ **Service scoping pattern** - Singleton services use IServiceProvider to create scopes for repositories
  - ✅ **Repository constructors** - Use IConfiguration instead of string connectionString parameters
  - ✅ **CommandRouter, TelegramAdminBotService** - Inject IServiceProvider, create scopes on-demand
  - ✅ **All IBotCommand implementations** - Use IServiceProvider pattern for repository access
  - ✅ **Migration 202601090** - Convert user_actions.action_type from VARCHAR to INT (enum storage)
- [x] **Reports system** ✅ **COMPLETE**:
  - ✅ **/report command** - Users can report messages for admin review (reply-to-message required)
  - ✅ **Reports database** - reports table with status tracking, reviewed_by, action_taken
  - ✅ **ReportsRepository** - Full CRUD operations with filtering by chat/status
  - ✅ **Reports UI** - /reports page with filtering, full message text display, action buttons
  - ✅ **Report actions** - Spam (delete), Ban (cross-chat), Warn (with escalation), Dismiss
  - ✅ **ReportActionsService** - Handle admin actions with Telegram API integration
  - ✅ **Message deletion tracking** - deleted_at, deletion_source columns in messages table
  - ✅ **Resilient design** - Reports work independently of message caching
- [x] **@admin mention notifications** ✅ **COMPLETE**:
  - ✅ **AdminMentionHandler** - Detects @admin in any message (text or caption)
  - ✅ **HTML text mentions** - Uses tg://user?id=X for all users (works without usernames)
  - ✅ **Auto-discovery** - Chats auto-added to managed_chats on first message
  - ✅ **Admin caching** - Auto-populates chat_admins table on discovery
  - ✅ **Smart filtering** - Skips sender and bot itself from notification list
  - ✅ **Error handling** - Failures don't prevent message history from being saved
- [ ] **Implement remaining command actions**:
  - `/spam` - Delete message, insert to detection_results, ban if threshold exceeded (TODO: prevent marking admins/trusted)
  - `/ban` - Insert to user_actions, call Telegram BanChatMember across all chats
  - `/unban` - Remove from user_actions, call Telegram UnbanChatMember
  - `/warn` - Insert to user_actions, auto-ban after threshold
- [ ] **Cross-chat actions** - Bans/warns across all managed groups
- [ ] **Edit monitoring** - Detect "post innocent, edit to spam" tactic

**Phase 2.4: Blazor Admin UI** ✅ **COMPLETE**
- [x] **UI reorganization** - Logical navigation structure with tabbed interfaces
- [x] **Spam management** - Consolidated `/spam` page with Stop Words and Training Data tabs
- [x] **Configuration UI** - `/settings` page with Spam Detection config + stubs for future settings
- [x] **Analytics dashboard** - `/analytics` page with Spam Analytics + stubs for trends/performance
- [x] **URL fragment navigation** - Direct linking to specific tabs (e.g., `/spam#training`)
- [x] **User experience** - Profile/Logout at bottom of nav
- [ ] **Username display** - Show logged-in user email in top-right corner (TODO)
- [x] **Component architecture** - Reusable spam components in `Components/Shared/SpamManagement/`
- [x] **Code quality improvements** ✅ **COMPLETE**:
  - ✅ **Helper method refactoring** - Replaced 100+ lines of nested if statements with TrackChange<T>() helpers in SpamDetectionConfig.razor
  - ✅ **Spam detection fixes** - Letter spacing regex (4+ chars), emoji-aware invisible char detection, pattern-only confidence reduction
  - ✅ **Architecture cleanup** - Separated InvisibleChars from Translation, created dedicated InvisibleCharsSpamCheck
  - ✅ **Two-phase execution** - InvisibleChars runs on original message before translation (prevents translation from hiding spam)
- [ ] **User actions UI** - Review bans, warns, appeals (future)
- [ ] **Multi-chat management** - Configure per-chat settings (future)

**Phase 2.5: Backup & Restore System** ✅ **COMPLETE**
- [x] **Full system backup/restore** - Fully dynamic JSON + reflection system
  - **Format:** gzip-compressed JSON (minimized, 81% compression ratio)
  - **Architecture:** Zero-maintenance reflection-based system
    - Auto-discovers tables from PostgreSQL information_schema
    - Auto-discovers DTOs from TelegramGroupsAdmin.Data assembly
    - No hardcoded table mappings or column lists
    - Schema changes automatically reflected in backups
  - **Scope:** ALL 18 tables (users w/ TOTP secrets, messages, spam config, Telegram mappings, everything)
  - **CLI flags:** `--export <path>` / `--import <path>` with 5-second safety delay
  - **Restore behavior:** Full wipe + restore in single transaction, foreign key-aware deletion order
  - **Transaction safety:** Single transaction, full rollback on any error, topological sort for dependencies
  - **Data Protection:** `[ProtectedData]` attribute for cross-machine encryption
    - Decrypts on export (old machine's keys)
    - Re-encrypts on import (new machine's keys)
    - Applied to: `totp_secret` (extensible to other encrypted fields)
  - **Self-referencing FKs:** Temporarily disables triggers during restore for circular dependencies
  - **Topological sort:** Proper parent→child insertion order, skips self-referencing FKs
  - **Sequence reset:** Automatically resets identity sequences after restore
  - **UI:** Settings page "Backup & Restore" tab + unauthenticated restore modal on registration page
  - **Version checking:** Prevents incompatible restore with metadata version validation
  - **DTOs:** All 18 tables have proper snake_case DTOs matching database schema exactly
  - **Benefits over MessagePack:** Human-readable, no special attributes, simpler debugging, reflection-friendly

**Phase 2.6: Confidence Aggregation & Training System** 🔄 **IN PROGRESS**
**Goal**: Improve spam detection accuracy with weighted voting and comprehensive training data collection

**Confidence Aggregation Strategy:**
- **Weighted voting system** - Net confidence = (spam votes) - (ham votes)
- **Asymmetric confidence** - Simple checks have low confidence for "not spam" (absence of evidence ≠ strong evidence)
  - Simple checks (InvisibleChars, StopWords): 20% confidence when NOT spam
  - Trained checks (Bayes, Similarity): Full confidence in both directions
- **Two-tier decision system**:
  - Net > +50: Run OpenAI veto (safety before ban)
  - Net ≤ +50: Admin review queue (skip OpenAI cost)
  - Net < 0: Allow (no spam detected)
- **OpenAI confidence handling**:
  - OpenAI 85%+ confident → Trust decision (ban or allow)
  - OpenAI <85% confident → Admin review queue (uncertain)

**Implementation Tasks:**
- [ ] **Database schema updates**:
  - [ ] Add `used_for_training` BOOLEAN flag to `detection_results` table
  - [ ] Add `net_confidence` INT column to store weighted voting result
  - [ ] Store all spam checks (not just training-worthy) for audit trail
- [ ] **SpamDetectorFactory updates**:
  - [ ] Implement `CalculateNetConfidence()` with asymmetric scoring
  - [ ] Update `AggregateResults()` for two-tier system (>+50 = OpenAI, ≤+50 = review)
  - [ ] Store all check results to `detection_results` (with `used_for_training` flag)
  - [ ] Handle OpenAI confidence threshold (85%) for final decision
- [ ] **Training data collection**:
  - [ ] Auto-add: OpenAI confident results (85%+) → `used_for_training = true`
  - [ ] Auto-add: All admin decisions (review queue, /spam, /ham buttons) → `used_for_training = true`
  - [ ] Update BayesSpamCheck query to filter `WHERE used_for_training = true`
  - [ ] Update SimilaritySpamCheck query to filter `WHERE used_for_training = true`
- [ ] **Messages page enhancements**:
  - [ ] Add "Mark as Spam" button (visible on all messages)
  - [ ] Add "Mark as Ham" button (visible on spam-flagged messages)
  - [ ] Implement unban logic for "Mark as Ham" action
  - [ ] Add spam check history dropdown/expandable UI per message
  - [ ] Show full detection history with timestamps, results, actions taken
  - [ ] Update `detection_results` and trigger training data refresh
- [ ] **Admin review queue** (future):
  - [ ] New page: `/review` for messages in limbo (net +0 to +50)
  - [ ] Spam/Ham/Dismiss buttons with training data integration
  - [ ] Real-time updates when new messages need review

**Phase 2.7: Advanced Features** 🔮 **FUTURE**
- [ ] **Ban appeal workflow** - UI + bot commands
- [ ] **Join verification** - Rule acceptance on join
- [ ] **OpenAI-guided setup** - Smart configuration
- [ ] **Performance monitoring** - Metrics, alerting

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

### **Immediate Priority: Confidence Aggregation & Training System (Phase 2.6)** 🎯
**Goal**: Improve spam detection accuracy and reduce false positives through weighted voting

**Why this is important:**
- Current tg-spam approach flags legitimate messages as spam (e.g., personal finance discussion → Bayes 100% spam)
- OpenAI must review EVERY potential spam (expensive safety net)
- Weighted voting allows multiple checks to balance each other
- Comprehensive training data collection improves all checks over time

**Implementation Order:**

1. **Database Schema** (Migration 202601091)
   - Add `used_for_training BOOLEAN DEFAULT true` to `detection_results`
   - Add `net_confidence INT` to `detection_results`
   - Update DetectionResultsRepository to store all checks (not just training-worthy)

2. **Asymmetric Confidence Scoring**
   - Update simple checks (InvisibleChars, StopWords): Return 20% confidence when NOT spam
   - Keep trained checks (Bayes, Similarity): Full confidence in both directions
   - Document reasoning in code comments

3. **Weighted Voting Logic**
   - Implement `CalculateNetConfidence()` in SpamDetectorFactory
   - Net = Sum(spam check confidences) - Sum(ham check confidences)
   - Store net_confidence in detection_results for analytics

4. **Two-Tier Decision System**
   - Net > +50: Run OpenAI veto
     - OpenAI 85%+ confident → Ban or Allow
     - OpenAI <85% confident → Admin review queue
   - Net ≤ +50: Admin review queue (skip OpenAI cost)
   - Net < 0: Allow (no spam detected)

5. **Messages Page UI**
   - Add "Mark as Spam" / "Mark as Ham" buttons (always visible)
   - Add spam check history dropdown (show all detection_results entries)
   - Implement unban logic for "Mark as Ham"

6. **Training Data Integration**
   - Update BayesSpamCheck: `WHERE used_for_training = true`
   - Update SimilaritySpamCheck: `WHERE used_for_training = true`
   - All admin decisions → `used_for_training = true`
   - Confident OpenAI (85%+) → `used_for_training = true`

### **Next Priority: Admin Review Queue (Phase 2.6 continued)** 🔜
- New `/review` page for borderline messages (net +0 to +50)
- Spam/Ham/Dismiss buttons with training integration
- Real-time updates when new messages need review

### **Future Priority: Command Actions Implementation (Phase 2.4 completion)**
- Implement `/spam`, `/ban`, `/unban`, `/warn` actions
- Cross-chat ban enforcement
- Edit monitoring and re-scanning

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
