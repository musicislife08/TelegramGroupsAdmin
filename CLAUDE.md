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
- TickerQ 2.5.3 (background job scheduler with EF Core integration)

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
- EF Core DbContext and entity models
- Database migrations
- Data Protection services
- Internal to repositories - UI uses UI models instead

**TelegramGroupsAdmin.Telegram** (Telegram bot services library)
- All Telegram bot services and background workers
- Bot command system (9 commands: /help, /report, /spam, /ban, /trust, /unban, /warn, /delete, /link)
- Telegram-related repositories and models
- Moderation and spam orchestration services
- `AddTelegramServices()` extension method

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
- `AddTickerQBackgroundJobs()` - TickerQ background job system with PostgreSQL backend

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

**TelegramGroupsAdmin/** (main project)
- Models/ - UI Models (UserModels.cs, MessageModels.cs, SpamDetectionModels.cs, VerificationModels.cs)
- Repositories/ - Data access layer with ModelMappings.cs for Data ↔ UI conversions
- Services/ - Business logic using UI models exclusively

**TelegramGroupsAdmin.Data/** (data layer)
- Models/ - Database DTOs internal to data layer (snake_case → PascalCase conversion)

## Database Schema (PostgreSQL)

**Single PostgreSQL database:** `telegram_groups_admin`
**Initial migration:** `202601100_InitialSchema.cs` (18 tables, validated against known good schema)
**Latest migration:** `AddWelcomeResponsesTable` (Phase 4.4 - welcome system tracking)

### Core Tables (Normalized Design)

#### **messages** - Central message storage
**Columns:**
- message_id (BIGINT) - Telegram message ID (primary key)
- chat_id, user_id (BIGINT) - Telegram chat and user IDs
- user_name, chat_name (TEXT) - Cached display names
- timestamp, edit_date (BIGINT) - Unix timestamps for create/edit
- message_text (TEXT) - Message content
- photo_file_id, photo_file_size, photo_local_path, photo_thumbnail_path - Image storage metadata
- urls (TEXT) - Extracted URLs from message
- content_hash (VARCHAR) - MD5 hash for deduplication

**Retention:** Configurable (default 180 days), except messages referenced by `detection_results` or `user_actions`

#### **detection_results** - Spam/ham classifications
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- message_id (BIGINT) - References messages table (cascade delete)
- detected_at (BIGINT) - Unix timestamp when detection occurred
- detection_source (TEXT) - 'auto' or 'manual'
- is_spam (BOOLEAN) - true=spam, false=ham/false positive
- confidence (INT) - 0-100 confidence score
- reason (TEXT) - Human-readable detection reason
- detection_method (TEXT) - Algorithm name ('StopWords', 'Bayes', 'Manual', etc)
- added_by (TEXT) - Web UI user ID who classified it (NULL for auto)

**Purpose:**
- Spam detection history (for analytics)
- Bayes training data (bounded query: recent 10k + all manual)
- False positive tracking (is_spam=false)
**Retention:** Permanent (never cleaned up)

#### **message_edits** - Edit history audit trail
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- message_id (BIGINT) - References messages table (cascade delete)
- edit_date (BIGINT) - Unix timestamp when edit occurred
- previous_text (TEXT) - Message text before edit
- previous_content_hash (VARCHAR) - MD5 hash before edit

**Purpose:** Track message edits (spam tactic: post innocent message, edit to spam later)
**Retention:** Cascades with messages table

#### **user_actions** - Moderation actions (bans, warns, mutes, trusts)
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- user_id (BIGINT) - Telegram user ID being actioned
- action_type (TEXT) - 'ban', 'warn', 'mute', 'trust', or 'unban'
- message_id (BIGINT) - Related message (NULL on delete)
- issued_by (TEXT) - Web UI user ID who issued action (NULL for system/auto)
- issued_at (BIGINT) - Unix timestamp when action was taken
- expires_at (BIGINT) - Expiry timestamp (NULL=permanent)
- reason (TEXT) - Human-readable reason for action

**Purpose:** Global moderation actions (all actions apply across all managed chats)
**Retention:** Permanent

**User Whitelisting (Trust Action):**
- Action type `trust` marks user as trusted (bypasses all spam checks)
- Applied globally across all chats
- Manual: Admin uses `/trust` command or UI
- Auto-trust: After N consecutive non-spam messages (configurable threshold, default 3)
  - Implemented via UserAutoTrustService
  - Revocable if spam detected (compromised account protection)
  - Logged to audit_log as UserAutoWhitelisted event

#### **welcome_responses** - Welcome system tracking (Phase 4.4)
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- chat_id (BIGINT) - Telegram chat ID
- user_id (BIGINT) - Telegram user ID
- username (TEXT) - Cached Telegram username (nullable)
- welcome_message_id (INT) - Telegram message ID of the welcome message
- response (TEXT) - 'accepted', 'denied', 'timeout', or 'left'
- responded_at (TIMESTAMPTZ) - When user responded (clicked button, left, or timed out)
- dm_sent (BOOLEAN) - Did DM succeed after acceptance?
- dm_fallback (BOOLEAN) - Did we fall back to chat message because DM failed?
- created_at (TIMESTAMPTZ) - Record creation timestamp

**Purpose:** Track welcome system effectiveness and user behavior
- Analytics: acceptance rate per chat, DM success rate, timeout rates
- Spam correlation: users who accept then spam quickly may indicate compromised accounts
- Performance monitoring: Track DM delivery success across Telegram's privacy settings
**Retention:** Permanent (analytics data)

### Configuration Tables

#### **stop_words** - Keyword blocklist
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- word (TEXT) - The blocked keyword/phrase
- word_type (INT) - 0=message content, 1=username, 2=userID
- added_date (BIGINT) - Unix timestamp when added
- source (TEXT) - 'manual', 'auto', or 'imported'
- enabled (BOOLEAN) - Whether the rule is active
- added_by (TEXT) - Web UI user ID who added it
- detection_count (INT) - Usage tracking counter
- last_detected_date (BIGINT) - Last time this word triggered

#### **spam_detection_configs** - Per-chat detection settings
**Columns:**
- chat_id (TEXT) - Telegram chat ID (primary key)
- min_confidence_threshold (INT) - Minimum confidence to flag (default 85)
- enabled_checks (TEXT[]) - Array of enabled algorithm names
- custom_prompt (TEXT) - Custom OpenAI instructions for this chat
- auto_ban_threshold (INT) - Auto-ban threshold (default 95)
- created_at, updated_at (BIGINT) - Timestamps

#### **spam_check_configs** - Algorithm-specific settings
**Columns:**
- check_name (TEXT) - Algorithm name (primary key)
- enabled (BOOLEAN) - Whether this check is globally enabled
- confidence_weight (INT) - Confidence multiplier (default 100)
- config_json (TEXT) - Algorithm-specific JSON configuration
- updated_at (BIGINT) - Last update timestamp

### Identity & Auth Tables

#### **users** - Web UI users (not Telegram users)
**Columns:**
- id (TEXT) - GUID primary key
- email, normalized_email (TEXT) - Email address (unique)
- password_hash, security_stamp (TEXT) - Authentication data
- permission_level (INT) - 0=ReadOnly, 1=Admin, 2=Owner
- invited_by (TEXT) - References users(id) for audit trail
- is_active (BOOLEAN) - Account enabled flag
- totp_secret, totp_enabled, totp_setup_started_at - 2FA configuration
- created_at, last_login_at, modified_at (BIGINT) - Timestamps
- status (INT) - 0=Pending, 1=Active, 2=Disabled, 3=Deleted
- modified_by (TEXT) - Who last modified this user
- email_verified (BOOLEAN) - Email verification status
- email_verification_token, email_verification_token_expires_at - Email verification
- password_reset_token, password_reset_token_expires_at - Password reset

#### **invites** - Invite token system
**Columns:**
- token (TEXT) - Unique invite token (primary key)
- created_by (TEXT) - References users(id) who created it
- created_at, expires_at (BIGINT) - Creation and expiry timestamps
- used_by (TEXT) - References users(id) who used it
- permission_level (INT) - Permission level granted by this invite
- status (INT) - 0=Pending, 1=Used, 2=Revoked
- modified_at (BIGINT) - Last modification timestamp

#### **audit_log** - Security audit trail
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- event_type (INT) - Enum value (Login, Logout, UserCreated, etc)
- timestamp (BIGINT) - Unix timestamp when event occurred
- actor_user_id (TEXT) - References users(id) who performed action
- target_user_id (TEXT) - References users(id) who was affected
- value (TEXT) - Additional context/details about the event

#### **verification_tokens** - Email/password reset tokens
**Columns:**
- id (BIGSERIAL) - Auto-increment primary key
- user_id (TEXT) - References users(id)
- token_type (TEXT) - 'email_verify', 'password_reset', or 'email_change'
- token (TEXT) - Unique token string
- value (TEXT) - Additional data (e.g., new email for email_change)
- expires_at, created_at, used_at (BIGINT) - Token lifecycle timestamps

### Design Principles

1. **Normalized storage** - Message content stored once, referenced by detections/actions
2. **Cascade deletes** - When message deleted, edits cascade; detections/actions remain for analytics
3. **Configurable retention** - Messages cleaned up after N days, unless flagged as spam
4. **Global actions** - All moderation actions (ban/warn/trust) apply across all managed chats
5. **Audit trail** - Complete history of who did what, when

### Background Services (Refactored Architecture) ✅

**Composition Pattern** - Services separated by responsibility for maintainability:

1. **TelegramAdminBotService.cs** (208 lines) - Core bot lifecycle
   - Bot polling and update routing (5 event types):
     - `Message` - New messages (commands, text, photos)
     - `EditedMessage` - Message edits (spam tactic detection)
     - `MyChatMember` - Bot added/removed from chats
     - `ChatMember` - User joins/leaves chat (Phase 4.4 ready)
     - `CallbackQuery` - Inline button clicks (Phase 4.4 ready)
   - Command registration with Telegram
   - Event forwarding from child services
   - Implements `IMessageHistoryService` interface

2. **MessageProcessingService.cs** (509 lines) - Message handling
   - New message processing and storage
   - Edit monitoring with spam re-scanning
   - Image download/thumbnail generation
   - Spam detection orchestration
   - URL extraction and content hashing

3. **ChatManagementService.cs** (359 lines) - Chat/admin management
   - MyChatMember event handling (bot added/removed)
   - Admin cache refresh (startup + runtime)
   - Health checking (per-chat + all chats)
   - Chat name updates from Telegram API

4. **SpamActionService.cs** (230 lines) - Spam action handling
   - Training data quality control
   - Auto-ban execution (cross-chat enforcement)
   - Borderline report creation
   - Confidence-based decision logic

5. **CleanupBackgroundService.cs** (80 lines) - Message retention cleanup with smart retention (keeps spam/ham samples)

## Configuration (Env Vars)

### Required
- VIRUSTOTAL__APIKEY - VirusTotal API key for threat intelligence
- OPENAI__APIKEY - OpenAI API key for vision and translation spam detection
- TELEGRAM__BOTTOKEN - Telegram bot token
- TELEGRAM__CHATID - Default Telegram chat ID
- SPAMDETECTION__APIKEY - Spam detection API key
- SENDGRID__APIKEY, SENDGRID__FROMEMAIL, SENDGRID__FROMNAME - Email service configuration

### Optional
- APP__BASEURL - Base URL for application (default: http://localhost:5161)
- OPENAI__MODEL - OpenAI model to use (default: gpt-4o-mini)
- OPENAI__MAXTOKENS - Max tokens for OpenAI responses (default: 500)
- MESSAGEHISTORY__ENABLED, MESSAGEHISTORY__DATABASEPATH, MESSAGEHISTORY__RETENTIONHOURS, MESSAGEHISTORY__CLEANUPINTERVALMINUTES - Message history configuration
- SPAMDETECTION__TIMEOUTSECONDS, SPAMDETECTION__IMAGELOOKUPRETRYDELAYMS, SPAMDETECTION__MINCONFIDENCETHRESHOLD - Spam detection tuning
- IDENTITY__DATABASEPATH, DATAPROTECTION__KEYSPATH - Identity and data protection paths

## Logging

### Configuration
- Microsoft namespace: LogLevel.Warning
- Microsoft.Hosting.Lifetime: LogLevel.Information
- TelegramGroupsAdmin: LogLevel.Information

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
Returns JSON with status=healthy and bot statistics

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
Build solution with dotnet build, run main project with dotnet run. Docker image can be built and run with standard docker commands, exposing port 8080 and passing environment variables.

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
**TickerQ attribute not found**: If `TickerFunctionAttribute` or `TickerFunctionContext<T>` cannot be found, ensure you're using the correct namespaces:
  - `using TickerQ.Utilities.Base;` (for TickerFunctionAttribute)
  - `using TickerQ.Utilities.Models;` (for TickerFunctionContext<T>)
  - These types are in the `TickerQ.Utilities` package, which is a transitive dependency
  - **Pro tip**: When NuGet package documentation is unclear, clone the GitHub repo and inspect the source code directly to find correct namespaces

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
- [x] **Type corrections** - Removed `chat_ids` column (all actions are global), column names (details → reason)
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

**Phase 2.7: Spam Action Implementation** ✅ **COMPLETE**
- [x] Auto-create reports for borderline detections (net +0 to +50)
- [x] Auto-ban implementation (net >50 + OpenAI 85%+ confirmation)
- [x] Unban logic for "Mark as Ham" button
- [x] Command actions: `/spam`, `/ban`, `/unban`, `/warn` with Telegram API calls
- [x] Cross-chat ban enforcement
- [x] Edit monitoring with re-scanning
- [x] **Major refactoring**: Split TelegramAdminBotService (1,243 lines → 4 focused services)

**Phase 2.8: UI Enhancements & Service Message Filtering** ✅ **COMPLETE**
- [x] **Chat icons** - Fetch and display group profile pictures from Telegram
  - TelegramPhotoService with ImageSharp (64x64 icons, cached on disk)
  - Auto-refresh during admin cache refresh
  - Stored in `managed_chats.chat_icon_path` (properly normalized)
- [x] **Messages UI improvements** - Better multi-chat visibility
  - Reversed hierarchy: Chat name prominent (top, bold, primary color), username secondary (smaller, gray)
  - 24px chat icon displayed first, fallback group icon when no photo
  - Inline filter buttons (filter by user/chat with single click)
  - Quick filters (always visible) + collapsible advanced filters
- [x] **Service message filtering** - Auto-delete Telegram clutter
  - Property-based detection (NewChatMembers, LeftChatMember, NewChatPhoto, DeleteChatPhoto, etc.)
  - Prevents empty messages in database (e.g., "User updated group photo")
- [x] **Delete message button** - Admin+ can delete messages from UI
  - Confirmation dialog before deletion
  - Deletes from Telegram + soft-deletes in database
  - Proper architecture: UI calls ModerationActionService (no bot client in UI)
- [x] **Architecture cleanup** - Service layer handles bot client internally
  - ModerationActionService injects TelegramBotClientFactory via DI
  - UI-friendly overloads (no bot client parameter needed)
  - UI completely decoupled from Telegram infrastructure

### Phase 3: Advanced Multi-Chat Features ✅ **COMPLETE**

- [x] **Cross-chat spam detection** - All bans are global across all managed chats (ModerationActionService)
- [x] **Shared/global blacklist** - user_actions and stop_words are global (no per-chat isolation)
- [x] **Global moderation** - Ban/warn/trust actions apply to all managed chats automatically
- **Not implemented (not needed):**
  - Chat owner delegation - No multi-tenant use case (Owner/Admin permissions sufficient)
  - Chat templates - Per-chat configs exist but no template/copy feature needed
  - Bulk operations UI - Already happening automatically (every ban is global)

### Phase 4: Infrastructure & Configuration (IN PROGRESS)

**Goal:** Production-ready configuration management and background job infrastructure

**Phase 4.1: TickerQ Background Job System** ✅ **COMPLETE**
- [x] PostgreSQL-backed job queue (TickerQ library) via #:package directive
- [x] TickerQ NuGet package installed and configured
- [x] Database connection setup
- [x] Ready for job implementations in future phases

**Phase 4.2: Unified Configuration System** ✅ **COMPLETE**
- [x] Created unified `configs` table with JSONB columns and native PostgreSQL timestamps
- [x] `chat_id` column: NULL = global defaults, non-null = chat-specific overrides
- [x] IConfigService with Save/Get/GetEffectiveAsync/Delete methods
- [x] Automatic global + chat-specific config merging
- [x] Migrated existing spam_detection_configs data
- [x] Proper dependency chain: Configuration → Data
- [x] Fixed DI lifetime issues (CommandRouter scope management)
- [x] Updated EF Core tools to 10.0.0-rc.2

**Phase 4.2.1: Database Timestamp Modernization** ✅ **COMPLETE**
- [x] Audited 36 timestamp columns across 19 tables
- [x] Created migration to convert bigint → PostgreSQL `timestamp with time zone`
- [x] Updated all DTOs and UI models to use `DateTimeOffset`
- [x] Updated all repositories, services, and Blazor components
- [x] Added TickerQ entity configurations and tables migration
- [x] Suppressed PendingModelChangesWarning (EF Core 9+ behavior)
- [x] Data integrity verified: 228 messages preserved with correct date ranges
- [x] Build: 0 errors, 0 warnings maintained

**Phase 4.3: Telegram Services Library** ✅ **COMPLETE**
- [x] Created `TelegramGroupsAdmin.Telegram` class library with proper dependency hierarchy
- [x] Moved 6 model files (MessageModels, SpamDetectionModels, ChatAdminModels, UserModels, etc.)
- [x] Moved 13 repositories (MessageHistory, DetectionResults, UserActions, ManagedChats, etc.)
- [x] Moved all Telegram services (ModerationActionService, SpamCheckOrchestrator, UserAutoTrustService)
- [x] Moved bot command system (9 commands + CommandRouter + IBotCommand interface)
- [x] Moved 4 background services (TelegramAdminBotService, MessageProcessingService, ChatManagementService, SpamActionService)
- [x] Created `AddTelegramServices()` extension method for centralized DI registration
- [x] Made ModelMappings public for cross-assembly usage
- [x] Fixed `[DatabaseGenerated]` attribute on ReportDto.Id for proper EF Core auto-increment
- [x] Clean downward dependency flow: Main App → Telegram → Data/Config/SpamDetection
- [x] Updated 100+ using statements across main app
- [x] Build: 0 errors, 0 warnings

**Phase 4.4: Welcome Message System** ⏳ **IN PROGRESS**
- [x] Database schema: welcome_responses table (acceptance rate, DM success tracking)
- [x] WelcomeService: ChatMember event handler, inline button callbacks, permission management
- [x] Repository: WelcomeResponsesRepository with analytics (GetStatsAsync)
- [x] Bot integration: Wire up ChatMember and CallbackQuery event handlers
- [ ] TODO: Configuration system (load WelcomeConfig from database instead of hardcoded defaults)
- [ ] TODO: Chat name caching (replace "this chat" placeholder with actual chat name)
- [ ] TODO: TickerQ timeout job (auto-kick if no response within configured timeout)
- [ ] TODO: Callback validation (only tagged user can click their buttons)
- [ ] TODO: User leaves before timeout handling (cancel job, delete welcome)
- [ ] TODO: UI - `/settings#telegram` tab for template editing and timeout configuration

**Implementation Details:**
- Rule acceptance enforcement before user can post (ChatMemberUpdated event triggers flow)
- Two-stage message system: consent in chat, detailed info via DM (fallback to chat if DM fails)
- User flow: restrict permissions on join → post welcome with inline buttons → timeout if no response (default 60s)
- Accept: restore permissions, delete welcome, DM rules (or post in chat if DM blocked)
- Deny/timeout: kick user (ban + immediate unban), delete welcome
- Returning members: show welcome again (simpler, rules may have changed)
- Configuration in WelcomeConfig (timeout, templates, button text)
- Templates support variables: {chat_name}, {username}, {rules_text}
- Three templates: chat_welcome (consent), dm_template (preferred), chat_fallback (if DM fails)

**Phase 4.5: Temporary Ban System**
- `/tempban` command with three presets: Quick (5min), Medium (1hr), Long (24hr)
- Telegram RestrictChatMember with until_date (auto-unrestricts, no TickerQ needed)
- Record in user_actions table for audit trail
- UI integration in Reports and Messages pages

**Phase 4.6: Runtime Log Level Configuration**
- `/settings#logging` page for dynamic log level adjustment (like *arr apps)
- Per-namespace configuration stored in configs table (JSONB)
- Immediate application via ILoggerFactory (no restart required)

**Phase 4.7: Settings UI Completion**
- `/settings#general` - App config (retention, timezone, session timeout, password policy)
- `/settings#integrations` - API keys (encrypted storage, masked display, test connection buttons, feature status indicators)
- `/settings#telegram` - Bot token, managed chats list, welcome message config
- `/settings#notifications` - Email/Telegram toggles, spam wave thresholds, quiet hours
- `/settings#security` - Password requirements, session timeout, login limits, audit retention
- `/settings#logging` - Dynamic log level controls (from Phase 4.6)

### Phase 5: Analytics & Data Aggregation (FUTURE)

**Goal:** Complete analytics UI with historical data aggregation

**Phase 5.1: Analytics Repository**
- Time-series queries for message volume, spam/ham ratios, detection method breakdown
- False positive/negative rate calculations
- Per-check performance metrics (hit rates, accuracy, execution time)

**Phase 5.2: Daily Stats Aggregation**
- TickerQ daily jobs to pre-calculate analytics (avoid expensive queries)
- New tables: analytics_daily_stats, api_usage_stats, check_performance_stats
- Weekly/monthly rollup aggregation

**Phase 5.3: Analytics UI Pages**
- `/analytics#trends` - Message volume over time, spam/ham ratios, peak activity patterns
- `/analytics#performance` - Detection accuracy metrics, per-check performance, confidence distributions

**Phase 5.4: Charting Library**
- Integrate charting library (MudBlazor Charts or ApexCharts.Blazor)
- Line charts (time-series), bar charts (comparisons), pie charts (breakdowns)

### Phase 6: ML-Powered Insights (FUTURE)

**Goal:** Intelligent configuration recommendations and pattern detection

**Phase 6.1: Insights Data Service**
- Analyze manual override patterns (suggest threshold adjustments)
- Check performance analysis (effectiveness, redundancy, cost per detection)
- Stop word suggestions (analyze missed spam for keyword patterns)
- Pattern detection (ML clustering for new spam tactics, coordinated attacks)
- Auto-trust effectiveness (trust → ban conversion rates)

**Phase 6.2: OpenAI Recommendation Generation**
- Convert ML analysis to natural language recommendations
- Actionable insights with "Apply" buttons
- Priority levels (High/Medium/Low)

**Phase 6.3: Insights Dashboard**
- `/analytics#insights` - ML-powered configuration recommendations
- Category cards: Configuration, Performance, Cost, Patterns, Auto-Trust
- Historical recommendation tracking

**Phase 6.4: Background Insights Generation**
- TickerQ daily job for insights generation
- Notification when high-priority insights available

### Phase 7: Advanced Features (OPTIONAL)

- ML-based spam detection algorithm (10th spam check using historical data)
- Sentiment analysis for toxicity detection (shelved - too many false positives, `/report` sufficient)
- API for third-party integrations (not needed currently)

## Future Enhancements (Pending Admin Feedback)

### Cross-Group Welcome Exemption
**Problem:** Users who join multiple managed groups must complete welcome flow for each group separately.

**Proposed Solution:** Auto-deleting rules notification for already-trusted users
- User joins Group B (already accepted in Group A)
- Bot detects prior acceptance in another group
- Send tagged message: "@username, welcome! Here are the rules for this group: [rules]"
- Auto-delete after X seconds (configurable, e.g., 30-60s)
- No restrictions, no buttons, no action required
- User can participate immediately

**Benefits:**
- Zero friction for legitimate users
- Rules still visible (respects group differences)
- Clean chat (auto-delete)
- Maintains trust transfer across groups

**Decision Pending:** Awaiting feedback from other group admins on whether auto-trust is appropriate or if per-group vetting is preferred.

## Next Steps (Prioritized for 2025)

### **Recent Completion: Phases 1-3 ✅ COMPLETE**

**Current State:**
- ✅ 9 spam detection algorithms with confidence aggregation
- ✅ Auto-ban, auto-trust, auto-report workflows
- ✅ Cross-chat enforcement (global bans/trust)
- ✅ Blazor UI with full admin capabilities
- ✅ Telegram bot with 7 commands + @admin notifications
- ✅ ModerationActionService (unified bot/UI logic)
- ✅ Edit monitoring with re-scanning
- ✅ Backup/restore system
- ✅ 0 errors, 0 warnings build quality

### **Next: Phase 4 - Infrastructure & Configuration**

**Focus:** Production-ready configuration management and automation

**Key deliverables:**
1. TickerQ background job system (recurring, scheduled, queued jobs)
2. Unified configs table (JSONB columns, chat_id NULL = global, per-chat overrides)
3. Runtime log level configuration (dynamic adjustment like *arr apps)
4. Temporary ban system (/tempban command with Telegram auto-unrestrict)
5. Welcome message system (auto-DM new users via TickerQ)
6. Settings UI completion (6 tabs with real functionality)

**Why Phase 4 matters:**
- Moves all config from environment variables to database (runtime changes, no restarts)
- Enables per-chat customization (spam settings, welcome messages, notifications)
- Production debugging capabilities (dynamic log levels)
- Foundation for Phase 5 analytics (TickerQ daily aggregation jobs)

**After Phase 4:**
- Phase 5: Analytics UI with data aggregation
- Phase 6: ML-powered insights and recommendations
- Phase 7: Optional advanced features

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
