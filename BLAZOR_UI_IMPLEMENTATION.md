# Blazor UI Implementation - AI Reference

## Stack
- Blazor Server (.NET 10.0) + MudBlazor 8.13.0
- SQLite: message_history.db (30-day retention), identity.db
- Dapper + FluentMigrator 7.1.0
- Auth: ASP.NET Core Identity + TOTP 2FA
- SignalR, ImageSharp, CsvHelper, DiffPlex, PWA

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

### ✅ Step 1: Blazor Project Setup (COMPLETE)
**Created:** `TgSpam-PreFilterApi.Web`
- MudBlazor 8.13.0 integrated
- Dark mode theme configured
- Navigation: Dashboard/Messages/Audit/Users/Invite/Profile
- Warnings as errors enabled
- Build: PASSING

### ✅ Step 2: Shared Database Library (COMPLETE)
**Created:** `TgSpam-PreFilterApi.Data`

**Structure:**
```
TgSpam-PreFilterApi.Data/
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

### 🔄 Step 4: Authentication System (IN PROGRESS)

**Approach:** Cookie Authentication (without ASP.NET Core Identity)
- Dapper + FluentMigrator (consistent with existing stack)
- Custom user store, full control over schema
- OtpNet for TOTP 2FA

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
- ✅ Build: PASSING

**TODO:**
- [ ] Add OtpNet package
- [ ] AuthService (login/register/2FA)
- [ ] InviteService
- [ ] Auth UI

---

### 📋 Step 5: SignalR Integration

**Hub:** `/hubs/messages`
**Events:**
- `NewMessage(MessageRecord)` - Broadcast to all clients
- `MessageEdited(long messageId, MessageEditRecord)` - Broadcast edit
- `SpamCheckResult(SpamCheckRecord)` - Real-time spam updates

**HistoryBot Integration:**
- After inserting message → Hub.Clients.All.SendAsync("NewMessage", record)

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
