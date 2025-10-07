# Blazor UI Implementation - AI Reference

## 🔄 MAJOR ARCHITECTURE CHANGE (2025-10-06)

**Decision 1: Merge Web UI and API into single project**
**Decision 2: Rename project to TelegramGroupsAdmin**

### Rationale for Merge
- **Target audience**: Homelab users managing Telegram spam (not enterprise/cloud)
- **Deployment simplicity**: One container > two tightly-coupled containers
- **No real separation**: Both projects share databases, volumes, and Data Protection keys
- **Common pattern**: Blazor Server apps hosting API endpoints is standard practice
- **Easier maintenance**: Single codebase for integrated features

### Rationale for Rename
- **Descriptive naming**: "TelegramGroupsAdmin" is instantly clear, SEO-friendly
- **Plural "Groups"**: Emphasizes multi-group admin capabilities
- **No abbreviations**: Professional, self-documenting (PowerShell naming style)
- **Future scope**: Not limited to spam - can grow into full admin suite
- **Availability**: Name not taken on GitHub

### New Architecture (Post-Merge + Rename)
```
TelegramGroupsAdmin/  (single unified project)
├── Pages/              Blazor UI pages (Dashboard, Messages, Login, Profile, etc.)
├── Components/         Blazor components (chat bubbles, filters, etc.)
├── Endpoints/          Minimal API endpoints (/check, /health)
├── Services/           All services (HistoryBot, SpamCheck, Auth, Invite, etc.)
└── Program.cs          Unified service registration

TelegramGroupsAdmin.Data/  (shared library - KEEP)
├── Models/             Database models
├── Repositories/       Dapper repositories (User, Invite, MessageHistory)
├── Migrations/         FluentMigrator migrations (identity.db + message_history.db)
├── Services/           TOTP protection, data services
└── ServiceCollectionExtensions.cs  (unified extension method)
```

### Migration Plan
1. ✅ Keep Data project structure (already well-structured)
2. Rename `TgSpam-PreFilterApi.Data` → `TelegramGroupsAdmin.Data`
3. Rename `TgSpam-PreFilterApi` → `TelegramGroupsAdmin`
4. Copy Blazor UI code from `TgSpam-PreFilterApi.Web` → `TelegramGroupsAdmin`
5. Merge `Program.cs` files (API endpoints + Blazor UI + Background services)
6. Update all namespaces: `TgSpam_PreFilterApi` → `TelegramGroupsAdmin`
7. Delete `TgSpam-PreFilterApi.Web` project
8. Update solution file, docker-compose
9. Single port, single container, single deployment

---

## Stack (Post-Merge)
- **Single ASP.NET Core 10 app** with Blazor Server + Minimal APIs
- **MudBlazor 8.13.0** for UI
- **SQLite**: message_history.db (30-day retention), identity.db
- **Dapper + FluentMigrator 7.1.0**
- **Auth**: Cookie-based auth + Otp.NET for TOTP 2FA + Data Protection API
- **SignalR** for real-time updates, **ImageSharp**, **CsvHelper**, **DiffPlex**, **PWA**

## Database Architecture
- **Two SQLite databases**, both in `/data` volume:
  - `identity.db` - Users, invites, recovery codes (FluentMigrator migrations)
  - `message_history.db` - Telegram messages, spam checks, edits (FluentMigrator migrations)
- **Single app manages both databases**:
  - Identity DB for authentication
  - Message History DB for spam checking + UI display
- **Background services** (HistoryBot, Cleanup) run in same process
- **Data Protection keys** in `/data/keys` (chmod 700)

## Features
- Multi-chat monitoring, real-time updates, spam indicators
- Image spam (local storage + thumbnails)
- Edit history + diff viewer
- Client-side filtering (chat/user/spam/date/text)
- CSV/JSON export (Admin+)
- Permissions: ReadOnly(0) → Admin(1) → Owner(2)
- Invite system

## Pages
- `/` - Dashboard
- `/messages` - Chat viewer
- `/audit` - Audit log (Admin+)
- `/users` - User mgmt (Owner)
- `/invite` - Invite gen (Owner)
- `/profile` - Settings

---

## Implementation Status

### ✅ Step 1: Blazor Project Setup (COMPLETE - MERGED)
**Originally created:** `TgSpam-PreFilterApi.Web`
**Now merged into:** `TelegramGroupsAdmin`
- MudBlazor 8.13.0 integrated
- Dark mode theme configured
- Navigation: Dashboard/Messages/Audit/Users/Invite/Profile
- Warnings as errors enabled
- Build: PASSING

### ✅ Step 2: Shared Database Library (COMPLETE)
**Created:** `TelegramGroupsAdmin.Data`

**Structure:**
```
TelegramGroupsAdmin.Data/
├── Models/
│   └── MessageRecord.cs (MessageRecord, PhotoMessageRecord, HistoryStats)
├── Repositories/
│   └── MessageHistoryRepository.cs
└── Migrations/
    └── 202510061_InitialSchema.cs (FluentMigrator)
```

**Packages:**
- Dapper 2.1.66
- Microsoft.Data.Sqlite 9.0.9
- Microsoft.Extensions.Configuration.Abstractions 9.0.9
- Microsoft.Extensions.Logging.Abstractions 9.0.9
- FluentMigrator.Runner.SQLite 7.1.0 (Apache 2.0)

**Migration System:**
- FluentMigrator handles Up/Down migrations
- Auto-rollback on version mismatch
- Runner configured in API Program.cs
- Migrations run on startup

**Current Schema (V1):**
```sql
messages:
  message_id (PK), user_id, user_name, chat_id, timestamp, expires_at,
  message_text, photo_file_id, photo_file_size, urls
Indexes: idx_user_chat_timestamp, idx_expires_at, idx_user_chat_photo
```

---

### ✅ Step 3: Database Schema Updates (COMPLETE)

**Created:** `202510062_AddEditTrackingAndSpamCorrelation.cs`

**Changes:**
- Messages table: +edit_date, +content_hash, +chat_name, +photo_local_path, +photo_thumbnail_path
- New table: message_edits (edit history tracking)
- New table: spam_checks (spam correlation)
- New models: MessageEditRecord, SpamCheckRecord
- Updated MessageRecord with new nullable fields
- Updated MessageHistoryRepository.InsertMessageAsync
- Updated HistoryBotService to populate new fields
- Build: PASSING

**TODO:** Update MESSAGE_HISTORY_RETENTION_HOURS=720 in deployment config

---

### ✅ Step 4: Authentication System (COMPLETE - Pre-Merge)

**Approach:** Cookie Authentication (without ASP.NET Core Identity)
- Dapper + FluentMigrator (consistent with existing stack)
- Custom user store, full control over schema
- Otp.NET 1.4.0 for TOTP 2FA

**Schema (202510063_IdentitySchema.cs):**
```
users: id(UUID), email, password_hash, security_stamp, permission_level(0/1/2),
       invited_by, is_active, totp_secret, totp_enabled, created_at, last_login_at
recovery_codes: id, user_id, code_hash, used_at
invites: token(UUID), created_by, created_at, expires_at, used_by, used_at
```

**Completed:**
- ✅ Migration: 202510063_IdentitySchema.cs
- ✅ Models: UserRecord, RecoveryCodeRecord, InviteRecord
- ✅ Repositories: UserRepository, InviteRepository (Dapper)
- ✅ Cookie auth config in Web/Program.cs
- ✅ FluentMigrator runner for identity.db
- ✅ Otp.NET package added
- ✅ AuthService (login/register/2FA/recovery codes)
- ✅ InviteService (create/list invites)
- ✅ Login page UI
- ✅ 2FA verification page UI (TOTP + recovery codes)
- ✅ Register page UI (with invite validation)
- ✅ HttpContextAccessor for cookie management
- ✅ Build: PASSING

---

## 🚀 Step 4.5: Project Consolidation (NEXT)

**Merge TgSpam-PreFilterApi.Web → TgSpam-PreFilterApi**

### Tasks
- [x] Update ServiceCollectionExtensions to single unified method
- [x] Copy Components/, Pages/, Services/ from Web → API
- [x] Merge Program.cs (Blazor + API endpoints + Background services)
- [x] Add MudBlazor + Otp.NET packages to API project
- [x] Update all namespaces from `TgSpam_PreFilterApi.Web` → `TelegramGroupsAdmin`
- [x] Test build
- [x] Delete TgSpam-PreFilterApi.Web project
- [x] Update solution file
- [x] Rename projects to TelegramGroupsAdmin
- [ ] Update Dockerfile (if exists)

---

### ✅ Step 5: SignalR Integration (SKIPPED - Not Needed)

**Decision:** Blazor Server already uses SignalR for component communication. No need for separate hub.

**Confirmed Approach (per Microsoft docs):**
- HistoryBotService exposes C# events: `event Action<MessageRecord>? OnNewMessage;`
- Blazor pages subscribe: `historyBot.OnNewMessage += HandleNewMessage;`
- **CRITICAL:** Must use `InvokeAsync(StateHasChanged)` because HistoryBot runs on background thread
- Blazor's built-in SignalR connection handles real-time UI updates automatically

**Example Pattern:**
```csharp
// In Blazor component
protected override void OnInitialized()
{
    historyBotService.OnNewMessage += HandleNewMessage;
}

private async void HandleNewMessage(MessageRecord msg)
{
    await InvokeAsync(StateHasChanged); // Required for background service thread safety
}
```

**Why InvokeAsync is Required:**
- HistoryBotService runs outside Blazor's synchronization context
- Without InvokeAsync: `InvalidOperationException` - "current thread not associated with Dispatcher"
- InvokeAsync marshals execution to renderer's synchronization context (like UI thread in WPF)

**Implementation:**
- Events will be added to HistoryBotService during Step 10 (Message History Page)
- Much simpler than separate hub, idiomatic for Blazor Server

---

### 📋 Step 6-12: UI Components

**Step 6:** Chat bubble component (Telegram style)
**Step 7:** Client-side filtering
**Step 8:** Export (CSV/JSON)
**Step 9:** PWA + diff viewer + charts
**Step 10:** Message history page
**Step 11:** Spam correlation display
**Step 12:** Testing & deployment

---

## Key Implementation Notes

**Content Hashing:**
- SHA256 of (message_text + urls) normalized (lowercase, trim)
- Used for spam check correlation (±5 sec window)

**Image Storage:**
- Download on arrival → `/data/images/{message_id}.jpg`
- Thumbnail: 200x200 → `/data/images/thumbs/{message_id}_thumb.jpg`
- Store paths in DB

**Edit History:**
- Separate table, not overwrite
- Store old/new text + hashes
- UI shows diff with DiffPlex

**Spam Correlation:**
- Match by content_hash within ±5sec of check_timestamp
- Fall back to user_id + timestamp if no hash match
- Display matched check in message bubble

---

## Timeline

- Step 1: ✅ DONE (Blazor setup)
- Step 2: ✅ DONE (Shared DB lib + FluentMigrator)
- Step 3: 30-45 min (Schema updates)
- Step 4: 45-60 min (Auth)
- Step 5: 30-45 min (SignalR)
- Step 6: 45-60 min (Chat bubbles)
- Step 7: 30-45 min (Filtering)
- Step 8: 30-45 min (Export)
- Step 9: 45-60 min (PWA/diff/charts)
- Step 10: 45-60 min (Message page)
- Step 11: 30-45 min (Spam correlation)
- Step 12: 30-45 min (Testing)

**Total:** ~7-11 hours

---

Last Updated: 2025-10-06
