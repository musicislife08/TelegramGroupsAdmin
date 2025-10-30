# WTelegram User Account Integration - Planning Document

## Overview
Integration of WTelegramClient to enable admin user account access alongside bot functionality. Unlocks features impossible with Bot API while maintaining bot-based message processing.

---

## Core Philosophy

**Bot Owns:** Message detection, spam filtering, moderation actions, welcome system
**WTelegram Owns:** Full member lists, historical imports, send-as-admin, chat interface, enhanced user data

**Why Both?**
- Bot API: Reliable, simple, perfect for automated moderation
- WTelegram (User API): Powerful, full access, better for human-driven actions
- **Best of both worlds:** Bot handles automation, WTelegram enables admin superpowers

---

## Features Unlocked by WTelegram

### **🌟 High-Value Features (Impossible with Bot API)**

#### 1. Full Member List
**Current Limitation:** Bot only sees users who have:
- Sent messages
- Joined (triggered welcome)
- Been banned/warned/trusted
- Are chat admins

**With WTelegram:**
- `Channels_GetParticipants()` returns **ALL** members including lurkers
- Complete `/users` page with accurate counts
- Identify truly inactive members vs active lurkers
- Export complete member lists

**Use Cases:**
- "Show me all 500 group members, not just the 120 who've messaged"
- "Find members who joined but never posted (potential spam bots)"
- "Generate complete member analytics"

**Implementation:**
- Button in `/users` page: "Import Full Member List"
- Shows progress bar (can be 1000s of users)
- One-time import or periodic refresh (daily background job)

---

#### 2. Historical Message Import
**Current Limitation:** Bot only sees messages after it was added to chat

**With WTelegram:**
- `Messages_GetHistory()` retrieves full chat history
- Backfill analytics for trends analysis
- Find old spam patterns
- Complete message archive

**Use Cases:**
- "Import last 6 months of messages to analyze spam trends"
- "Backfill message counts for user activity metrics"
- "Search historical messages for keywords/patterns"

**Implementation:**
- Button per chat: "Import Historical Messages"
- Date range selector (last 30/90/365 days, or all time)
- Progress bar (can be 10,000s of messages)
- Backfills `messages` table

---

#### 3. Send Messages as Admin User
**Current Limitation:** All bot messages appear from bot account (@YourBot)

**With WTelegram:**
- Messages appear from admin's personal Telegram account
- More authoritative moderation ("from @admin_username, not @bot")
- Personal touch for announcements/replies
- No need to switch to mobile app

**Use Cases:**
- "Reply to user question as myself, not the bot"
- "Post announcement from my account for credibility"
- "Moderate chat from browser without opening mobile app"

**Implementation:**
- Text input in chat interface
- "Send as Admin" button (vs "Send as Bot" option)
- Audit log of all admin-sent messages

---

#### 4. Admin Chat Dashboard
**Current Limitation:** No chat interface in web UI

**With WTelegram:**
- Full chat interface accessible from browser
- View all chats, threads, pinned messages
- Reply to DMs, group messages
- Post announcements
- Manage multiple chats from single interface

**Use Cases:**
- "Manage Telegram groups from desktop browser"
- "Reply to admin DMs without switching to mobile"
- "Monitor multiple chats in tabs"

**Implementation:**
- New page: `/chat`
- Left sidebar: List of chats (with unread counts)
- Main area: Message thread
- Text input: Send as admin user
- Read-only until admin connects Telegram account

---

#### 5. Enhanced User Information
**Current Limitation:** Bot API provides basic profile only

**With WTelegram:**
- Last seen / online status (if privacy allows)
- User bio
- Phone number (if shared in common chats)
- Common chats
- Verified badge status
- Account creation date indicators

**Use Cases:**
- "Flag accounts created < 30 days ago with suspicious names"
- "Identify abandoned accounts (not seen in 90 days)"
- "Detect impersonation via bio analysis"

**Implementation:**
- Enhanced user detail modal in `/users`
- Additional fields displayed when WTelegram session active
- Graceful fallback if data unavailable (privacy settings)

---

## Architecture: Multi-Instance Session Management

### The Challenge
**Problem:** Multiple web admins need simultaneous WTelegram access on single server

**Scenario:**
```
Admin 1 (owner@example.com)  → WTelegram session for +1234567890
Admin 2 (mod1@example.com)   → WTelegram session for +9876543210
Admin 3 (mod2@example.com)   → WTelegram session for +5555555555
```

Each admin needs:
- Isolated WTelegram client instance
- Separate session file (encrypted)
- Independent authentication state
- No session sharing (security risk)

### Solution: Per-User Service Instances

```csharp
public class UserTelegramSessionManager : IDisposable
{
    // In-memory cache: web user ID → active WTelegram client
    private readonly ConcurrentDictionary<Guid, WTelegram.Client> _activeSessions = new();
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly ILogger<UserTelegramSessionManager> _logger;

    // Session storage: /data/telegram-sessions/{userId}/session.dat
    private string GetSessionPath(Guid userId)
        => Path.Combine("/data/telegram-sessions", userId.ToString(), "session.dat");

    public async Task<WTelegram.Client?> GetOrCreateSessionAsync(Guid userId)
    {
        // Return existing if already in memory
        if (_activeSessions.TryGetValue(userId, out var existing))
        {
            _logger.LogDebug("Returning cached WTelegram session for user {UserId}", userId);
            return existing;
        }

        // Check if session file exists (user authenticated before)
        var sessionPath = GetSessionPath(userId);
        if (!File.Exists(sessionPath))
        {
            _logger.LogDebug("No WTelegram session found for user {UserId}", userId);
            return null; // User hasn't connected Telegram account
        }

        // Create new client instance with isolated config
        var client = new WTelegram.Client(config =>
        {
            return config switch
            {
                "session_pathname" => sessionPath,
                "api_id" => Environment.GetEnvironmentVariable("TELEGRAM__APIID"),
                "api_hash" => Environment.GetEnvironmentVariable("TELEGRAM__APIHASH"),
                _ => null
            };
        });

        // Verify session is still valid
        try
        {
            var me = await client.LoginUserIfNeeded();
            _logger.LogInformation(
                "WTelegram session restored for user {UserId} (Telegram: @{Username})",
                userId, me.username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore WTelegram session for user {UserId}", userId);
            client.Dispose();
            return null; // Session expired, user must reauthenticate
        }

        // Cache in memory
        _activeSessions[userId] = client;
        return client;
    }

    public async Task DisconnectSessionAsync(Guid userId)
    {
        if (_activeSessions.TryRemove(userId, out var client))
        {
            _logger.LogInformation("Disconnecting WTelegram session for user {UserId}", userId);
            client.Dispose();
        }
    }

    // Background cleanup job (TickerQ, every 30 minutes)
    public async Task CleanupInactiveSessionsAsync()
    {
        var inactiveThreshold = TimeSpan.FromMinutes(30);
        var toRemove = new List<Guid>();

        foreach (var (userId, client) in _activeSessions)
        {
            var lastActivity = await GetLastActivityAsync(userId);
            if (DateTimeOffset.UtcNow - lastActivity > inactiveThreshold)
            {
                _logger.LogInformation(
                    "Cleaning up inactive WTelegram session for user {UserId} (idle > {Minutes} min)",
                    userId, inactiveThreshold.TotalMinutes);
                toRemove.Add(userId);
            }
        }

        foreach (var userId in toRemove)
        {
            await DisconnectSessionAsync(userId);
        }
    }

    public void Dispose()
    {
        foreach (var (userId, client) in _activeSessions)
        {
            client.Dispose();
        }
        _activeSessions.Clear();
    }
}
```

### Resource Management

**Memory per active session:** ~10-20 MB (WTelegram client + local cache)
**Expected concurrent sessions:** 5 typical, 10 max
**Total memory overhead:** ~100-200 MB (acceptable)

**Session lifecycle:**
1. **Created:** When admin connects Telegram account
2. **Cached:** In-memory after first use (lazy load)
3. **Idle timeout:** Disposed after 30 min inactivity
4. **Web logout:** Explicitly disposed
5. **App shutdown:** Graceful dispose all sessions

**Cleanup strategies:**
- TickerQ background job every 30 min
- Track last activity per user (update on every WTelegram call)
- Dispose sessions idle > 30 min
- Persist session files (re-load on app restart)

---

## Authentication Flow

### Initial Connection

```
┌─ /settings#telegram-account ─────────────────────────────┐
│                                                            │
│  Your Telegram Account: ❌ Not Connected                  │
│                                                            │
│  [ Connect Telegram Account ]                             │
│                                                            │
│  Benefits of connecting your account:                      │
│  • View full member list (including lurkers)              │
│  • Send messages as yourself (not bot)                    │
│  • Import historical messages for analytics               │
│  • Access admin chat interface from browser               │
│                                                            │
│  ⚠️  Requires Owner or Admin permission                   │
└────────────────────────────────────────────────────────────┘
```

### Step 1: Phone Number

```
┌─ Connect Telegram Account ────────────────────────────────┐
│                                                            │
│  Phone Number (with country code):                        │
│  [+1234567890________________________________]            │
│                                                            │
│  [ Send Verification Code ]  [ Cancel ]                   │
│                                                            │
│  ℹ️  A verification code will be sent to your Telegram    │
│     app and/or SMS.                                        │
└────────────────────────────────────────────────────────────┘
```

**Backend:**
```csharp
public async Task<string> SendVerificationCodeAsync(Guid userId, string phoneNumber)
{
    // Create new WTelegram client for this user
    var sessionPath = GetSessionPath(userId);
    Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);

    var client = new WTelegram.Client(config =>
    {
        return config switch
        {
            "session_pathname" => sessionPath,
            "api_id" => Environment.GetEnvironmentVariable("TELEGRAM__APIID"),
            "api_hash" => Environment.GetEnvironmentVariable("TELEGRAM__APIHASH"),
            "phone_number" => phoneNumber,
            _ => null
        };
    });

    // This sends the code via Telegram
    await client.Login(phoneNumber);

    // Cache client temporarily (for verification step)
    _pendingAuthentications[userId] = client;

    return "Code sent to your Telegram app";
}
```

### Step 2: Verification Code

```
┌─ Enter Verification Code ─────────────────────────────────┐
│                                                            │
│  Enter the code sent to your Telegram app:                │
│  [12345_______________________________________]            │
│                                                            │
│  [ Verify ]  [ Resend Code ]  [ Cancel ]                  │
│                                                            │
│  ℹ️  Check your Telegram app for the verification code    │
└────────────────────────────────────────────────────────────┘
```

**Backend:**
```csharp
public async Task<VerificationResult> VerifyCodeAsync(Guid userId, string code)
{
    if (!_pendingAuthentications.TryGetValue(userId, out var client))
        throw new InvalidOperationException("No pending authentication");

    try
    {
        // Complete authentication
        var user = await client.Login(code);

        // Check if 2FA required
        if (user == null)
        {
            return VerificationResult.Requires2FA;
        }

        // Success - move to active sessions
        _activeSessions[userId] = client;
        _pendingAuthentications.Remove(userId);

        // Audit log
        await _auditLogService.LogAsync(new AuditLog
        {
            EventType = AuditEventType.TelegramAccountConnected,
            ActorUserId = userId,
            Timestamp = DateTimeOffset.UtcNow,
            Value = $"Connected Telegram account @{user.username}"
        });

        return VerificationResult.Success(user);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Verification failed for user {UserId}", userId);
        return VerificationResult.InvalidCode;
    }
}
```

### Step 3: Two-Factor Auth (If Enabled)

```
┌─ Two-Factor Authentication ───────────────────────────────┐
│                                                            │
│  Enter your Telegram cloud password:                      │
│  [••••••••••••________________________________]            │
│                                                            │
│  [ Submit ]  [ Cancel ]                                    │
│                                                            │
│  ℹ️  This is the password you set in Telegram settings    │
│     for two-step verification.                             │
└────────────────────────────────────────────────────────────┘
```

### Success State

```
┌─ /settings#telegram-account ─────────────────────────────┐
│                                                            │
│  Your Telegram Account: ✅ Connected                       │
│  Phone: +1 (234) 567-8900                                 │
│  Username: @admin_username                                 │
│  User ID: 123456789                                        │
│                                                            │
│  [ Disconnect Account ]                                    │
│                                                            │
│  Features Now Available:                                   │
│  • ✅ View full member list in /users                      │
│  • ✅ Send messages as yourself                            │
│  • ✅ Import historical messages                           │
│  • ✅ Access chat interface at /chat                       │
│                                                            │
│  Session Expires: 30 days (automatic re-auth required)    │
└────────────────────────────────────────────────────────────┘
```

---

## Security Model

### Session Encryption
- **Storage:** `/data/telegram-sessions/{userId}/session.dat`
- **Encryption:** ASP.NET Core Data Protection API (same as TOTP secrets)
- **Key rotation:** Handled by Data Protection (automatic)
- **Isolation:** Each user has separate encrypted session file

### Permissions
- **Connect Account:** Owner or Admin permission required
- **Use Features:** Requires active connected account
- **Audit Trail:** All actions logged to audit_log table

### Audit Events
```csharp
public enum AuditEventType
{
    // Existing events...
    TelegramAccountConnected = 100,
    TelegramAccountDisconnected = 101,
    TelegramMessageSent = 102,
    TelegramMemberListImported = 103,
    TelegramHistoryImported = 104,
}
```

### Session Security
- **No shared sessions:** Each web user has isolated WTelegram client
- **Automatic expiry:** Telegram sessions expire after 30 days (reauthenticate)
- **Idle cleanup:** Sessions disposed after 30 min inactivity
- **Logout cleanup:** Sessions destroyed on web user logout
- **App shutdown:** Graceful dispose all active sessions

### Rate Limiting
- WTelegram inherits Telegram's user account rate limits
- Typically more generous than Bot API
- No artificial rate limiting needed (Telegram enforces)

---

## Disable Message Listener Pattern

**Critical Design Decision:** Bot owns all incoming message processing

```csharp
public class UserTelegramService
{
    private readonly UserTelegramSessionManager _sessionManager;
    private readonly IAuditLogService _auditLogService;

    // ❌ NO message update handler registered
    // ✅ Bot (TelegramAdminBotService) handles all incoming messages

    public async Task<List<TelegramUser>> GetAllMembersAsync(Guid webUserId, long chatId)
    {
        var client = await _sessionManager.GetOrCreateSessionAsync(webUserId);
        if (client == null)
            throw new InvalidOperationException("Telegram account not connected");

        // Get channel info
        var chat = await client.Channels_GetFullChannel(chatId);

        // Get ALL participants (including lurkers)
        var participants = await client.Channels_GetParticipants(chat, limit: 10000);

        return participants.users.Values
            .Select(u => new TelegramUser(
                TelegramUserId: u.ID,
                Username: u.username,
                FirstName: u.first_name,
                LastName: u.last_name,
                UserPhotoPath: null, // Populated by FetchUserPhotoJob
                PhotoHash: null,
                IsTrusted: false,
                FirstSeenAt: DateTimeOffset.UtcNow,
                LastSeenAt: DateTimeOffset.UtcNow,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow
            ))
            .ToList();
    }

    public async Task SendMessageAsync(Guid webUserId, long chatId, string text)
    {
        var client = await _sessionManager.GetOrCreateSessionAsync(webUserId);
        if (client == null)
            throw new InvalidOperationException("Telegram account not connected");

        // Send message as admin user (not bot)
        await client.SendMessageAsync(chatId, text);

        // Audit log
        await _auditLogService.LogAsync(new AuditLog
        {
            EventType = AuditEventType.TelegramMessageSent,
            ActorUserId = webUserId,
            Timestamp = DateTimeOffset.UtcNow,
            Value = $"Sent message to chat {chatId}: {text.Substring(0, Math.Min(50, text.Length))}..."
        });
    }

    public async Task<List<Message>> ImportHistoricalMessagesAsync(
        Guid webUserId,
        long chatId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        IProgress<ImportProgress> progress)
    {
        var client = await _sessionManager.GetOrCreateSessionAsync(webUserId);
        if (client == null)
            throw new InvalidOperationException("Telegram account not connected");

        var messages = new List<Message>();
        var offsetId = 0;
        var totalProcessed = 0;

        while (true)
        {
            // Fetch batch of messages
            var history = await client.Messages_GetHistory(chatId, offsetId, limit: 100);
            if (history.Messages.Length == 0)
                break;

            foreach (var msg in history.Messages)
            {
                if (msg.Date < startDate.ToUnixTimeSeconds())
                    goto done; // Reached start date

                if (msg.Date > endDate.ToUnixTimeSeconds())
                    continue; // Skip messages after end date

                // Convert to our Message model
                messages.Add(ConvertMessage(msg));
                totalProcessed++;

                // Report progress
                progress?.Report(new ImportProgress
                {
                    TotalProcessed = totalProcessed,
                    CurrentBatch = history.Messages.Length
                });
            }

            offsetId = history.Messages[^1].ID;
        }

        done:
        // Audit log
        await _auditLogService.LogAsync(new AuditLog
        {
            EventType = AuditEventType.TelegramHistoryImported,
            ActorUserId = webUserId,
            Timestamp = DateTimeOffset.UtcNow,
            Value = $"Imported {messages.Count} historical messages from chat {chatId}"
        });

        return messages;
    }
}
```

**Why no message handler?**
- Bot already has robust message processing (MessageProcessingService)
- Duplicate processing would waste resources
- Bot has proper spam detection, welcome flow, edit tracking
- WTelegram only used for admin-initiated actions (pull data, send messages)

---

## Implementation Phases

### Phase 1: Core Infrastructure (Day 1 - ~4 hours)

**Database:**
- [ ] Create telegram_sessions table (track connected accounts)
```sql
CREATE TABLE telegram_sessions (
    id BIGSERIAL PRIMARY KEY,
    web_user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    phone_number VARCHAR(20) NOT NULL,
    telegram_user_id BIGINT NOT NULL,
    telegram_username VARCHAR(255) NULL,
    connected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_activity_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    session_expires_at TIMESTAMPTZ NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX idx_telegram_sessions_user ON telegram_sessions(web_user_id);
CREATE INDEX idx_telegram_sessions_active ON telegram_sessions(is_active) WHERE is_active = TRUE;
```

**Backend:**
- [ ] Install WTelegramClient NuGet package (v4.3.11+)
- [ ] Create UserTelegramSessionManager service
- [ ] Create UserTelegramService (orchestration layer)
- [ ] Add TELEGRAM__APIID and TELEGRAM__APIHASH env vars
- [ ] Create session storage directory (/data/telegram-sessions/)
- [ ] Register services in DI container

**Configuration:**
```bash
# Obtain from https://my.telegram.org/apps
TELEGRAM__APIID=12345678
TELEGRAM__APIHASH=abcdef1234567890abcdef1234567890
```

---

### Phase 2: Authentication UI (Day 2 - ~5 hours)

**UI:**
- [ ] Create /settings#telegram-account tab
- [ ] Phone number input + validation
- [ ] Verification code dialog
- [ ] 2FA password dialog (conditional)
- [ ] Connection status display
- [ ] Disconnect functionality
- [ ] Error handling (invalid code, expired session)

**Backend:**
- [ ] SendVerificationCodeAsync endpoint
- [ ] VerifyCodeAsync endpoint
- [ ] Submit2FAPasswordAsync endpoint
- [ ] DisconnectAccountAsync endpoint
- [ ] GetConnectionStatusAsync endpoint
- [ ] Audit logging for all auth events

---

### Phase 3: Full Member List Import (Day 3 - ~4 hours)

**UI:**
- [ ] "Import Full Member List" button in /users page
- [ ] Progress dialog with stats (X of Y processed)
- [ ] Success/error notifications
- [ ] Disabled state if no Telegram account connected

**Backend:**
- [ ] GetAllMembersAsync implementation
- [ ] Batch insert into telegram_users (upsert logic)
- [ ] Progress reporting via SignalR (optional) or polling
- [ ] Handle large member lists (1000s of users)
- [ ] Audit logging

**Query optimization:**
- [ ] Bulk upsert strategy (avoid N+1 queries)
- [ ] Transaction handling
- [ ] Duplicate detection (match by telegram_user_id)

---

### Phase 4: Send Message as Admin (Day 4 - ~3 hours)

**UI:**
- [ ] Add "Send as Admin" option in chat interface (future /chat page)
- [ ] Or: Quick message dialog in /users detail modal
- [ ] Display sender info (shows admin username, not bot)
- [ ] Success confirmation

**Backend:**
- [ ] SendMessageAsync implementation
- [ ] Input validation (rate limiting, content checks)
- [ ] Error handling (user kicked from chat, etc.)
- [ ] Audit logging with full message content

---

### Phase 5: Historical Message Import (Day 5 - ~6 hours)

**UI:**
- [ ] Import history dialog per chat
- [ ] Date range picker (last 30/90/365 days, custom range, all time)
- [ ] Progress bar with ETA
- [ ] Pause/cancel support
- [ ] Summary statistics (X messages imported, Y skipped)

**Backend:**
- [ ] ImportHistoricalMessagesAsync implementation
- [ ] Batch processing (100 messages per API call)
- [ ] Duplicate detection (match by message_id + chat_id)
- [ ] Progress reporting
- [ ] Memory-efficient streaming (don't load all into memory)
- [ ] Cancellation token support
- [ ] Audit logging

**Background job option:**
- [ ] TickerQ job for large imports (run in background)
- [ ] Notification when complete

---

### Phase 6: Admin Chat Interface (Day 6-7 - ~8 hours)

**UI:**
- [ ] Create /chat page
- [ ] Left sidebar: List of chats (unread counts, last message preview)
- [ ] Main area: Message thread (load on scroll, virtualization)
- [ ] Text input: Send message area
- [ ] Chat selection routing (/chat/{chatId})
- [ ] "Send as Bot" vs "Send as Admin" toggle
- [ ] Read-only state if no Telegram account connected
- [ ] Message formatting (bold, italic, code, links)

**Backend:**
- [ ] GetChatsAsync (list of admin's chats)
- [ ] GetMessagesAsync (fetch messages for chat)
- [ ] MarkAsReadAsync (update read state)
- [ ] LoadMoreMessagesAsync (infinite scroll)
- [ ] Real-time message updates (optional - SignalR)

**Note:** This is a major feature, may split into multiple sub-phases

---

### Phase 7: Enhanced User Details (Day 8 - ~3 hours)

**UI:**
- [ ] Add fields to user detail modal (when WTelegram session active):
  - Last seen / online status
  - Bio
  - Common chats
  - Verified badge
  - Account age indicators
- [ ] Graceful fallback if data unavailable (privacy settings)

**Backend:**
- [ ] GetEnhancedUserInfoAsync implementation
- [ ] Cache user data (don't re-fetch every time)
- [ ] Handle privacy restrictions gracefully

---

### Phase 8: Cleanup & Polish (Day 9 - ~4 hours)

**Background jobs:**
- [ ] TickerQ cleanup job (dispose inactive sessions every 30 min)
- [ ] Session expiry monitoring (notify admins 7 days before expiry)
- [ ] Periodic member list refresh (daily at 2 AM, optional)

**Error handling:**
- [ ] Session expired → prompt to reauthenticate
- [ ] User kicked from chat → graceful error message
- [ ] API rate limit → retry with backoff
- [ ] Network errors → user-friendly messages

**Security:**
- [ ] Session encryption validation
- [ ] Permission checks on all endpoints
- [ ] Input sanitization
- [ ] XSS prevention in chat messages

**Documentation:**
- [ ] Update CLAUDE.md with WTelegram section
- [ ] Admin user guide (how to connect account)
- [ ] Troubleshooting guide

---

## Total Effort Estimate

**Phase 1-5 (Core Features):** ~22 hours (~3 days)
**Phase 6 (Chat Interface):** ~8 hours (~1 day)
**Phase 7-8 (Polish):** ~7 hours (~1 day)

**Total: ~37 hours (~5 days)**

**Suggested order:**
1. Phase 1-2 (Infrastructure + Auth) - Foundation
2. Phase 3 (Member List) - Quick high-value win
3. Phase 4 (Send Messages) - Another quick win
4. Phase 5 (Historical Import) - High-value analytics
5. Phase 6 (Chat Interface) - Major feature (optional)
6. Phase 7-8 (Polish) - Production readiness

---

## Technical Considerations

### WTelegramClient Library
- **NuGet Package:** WTelegramClient (v4.3.11+)
- **License:** MIT (commercial-friendly)
- **Maintenance:** Active (last updated Feb 2025)
- **Documentation:** https://wiz0u.github.io/WTelegramClient/
- **Language:** Pure C# (no native dependencies)

### Telegram API Credentials
- **Obtain from:** https://my.telegram.org/apps
- **Required:** api_id (integer), api_hash (string)
- **Note:** These are for your application, not per-user
- **Storage:** Environment variables (TELEGRAM__APIID, TELEGRAM__APIHASH)

### Rate Limits
- **User API:** More generous than Bot API
- **Typical limits:**
  - Messages: 30/sec across all chats
  - Member list: 200 users per call
  - History: 100 messages per call
- **Handled by:** WTelegram library (automatic backoff)

### Session Persistence
- **Format:** Binary file (encrypted by Data Protection API)
- **Location:** `/data/telegram-sessions/{userId}/session.dat`
- **Size:** ~100-500 KB per session
- **Backup:** Include in backup/restore process
- **Expiry:** 30 days (Telegram enforces, reauthenticate after)

### Known Issues / Gotchas
- **2FA:** If user has 2FA enabled, requires cloud password (not SMS code)
- **Privacy:** Some user data unavailable if privacy settings restrict
- **Chat access:** Can only access chats the admin is a member of
- **Phone number change:** If admin changes phone, must reconnect account
- **Multiple devices:** Session works across multiple web browsers (shared session file)

---

## Success Metrics

**Phase 1-2 is successful if:**
- ✅ Admin can connect Telegram account via UI
- ✅ Session persists across browser restarts
- ✅ Multiple admins can have separate sessions
- ✅ All auth steps audited

**Phase 3 is successful if:**
- ✅ Import returns ALL members (including lurkers)
- ✅ Member count matches Telegram Desktop display
- ✅ Progress bar shows real-time status
- ✅ Import completes in < 30 seconds for 1000 users

**Phase 4 is successful if:**
- ✅ Messages appear from admin's personal account
- ✅ Messages show in Telegram clients immediately
- ✅ Audit log captures full message content
- ✅ Error handling for kicked/restricted scenarios

**Phase 5 is successful if:**
- ✅ Historical import retrieves messages before bot joined
- ✅ Backfill completes in < 5 minutes for 10,000 messages
- ✅ No duplicate messages in database
- ✅ Analytics reflect imported data

**Phase 6 is successful if:**
- ✅ Chat interface loads in < 2 seconds
- ✅ Messages display correctly (formatting, media)
- ✅ Sending messages works as admin or bot
- ✅ Unread counts accurate

---

## Future Enhancements

### Advanced Features (Post-MVP)
- **Scheduled messages** - Queue messages for future sending
- **Message templates** - Pre-defined responses for common scenarios
- **Bulk actions** - Send message to multiple chats simultaneously
- **Chat statistics** - Advanced analytics via WTelegram data
- **User search** - Find users across all chats by name/username/phone
- **Media management** - Download/upload photos, videos, files
- **Sticker packs** - Manage custom sticker sets
- **Bot management** - Create/configure bots via admin account

### Integration with Existing Features
- **Auto-trust** - Use last seen data to identify active users
- **Impersonation detection** - Compare bio/username to known admins
- **Enhanced spam detection** - Analyze user behavior patterns from WTelegram data
- **Invite tracking** - Track who invited whom (via common chat analysis)

---

## Open Questions

❓ **Multiple admins send messages** - How to attribute in UI (show "sent by @admin1" vs "sent by @admin2")?
❓ **Session sharing** - Should married admins share a session, or always separate?
❓ **Real-time chat updates** - SignalR for live message updates, or manual refresh?
❓ **Message editing** - Allow admin to edit sent messages via WTelegram?
❓ **Message deletion** - Confirm before deleting admin-sent messages?
❓ **Chat creation** - Allow creating new chats/channels via WTelegram?
❓ **Export Telegram data** - Parse Telegram Desktop JSON exports as alternative to WTelegram?

---

## Risks & Mitigations

### Risk: Session Hijacking
**Mitigation:**
- Encrypt session files with Data Protection API
- Isolate sessions per web user (no sharing)
- Audit all actions
- Auto-expire after 30 days

### Risk: Multiple Admins Conflict
**Mitigation:**
- Clear UI indicators of who sent what
- Separate sessions prevent interference
- Audit log tracks all actions by actor

### Risk: WTelegram Library Abandonment
**Mitigation:**
- Active maintenance (last update Feb 2025)
- Pure C#, can fork if needed
- Alternative: TDLib (official, more complex)

### Risk: Telegram API Changes
**Mitigation:**
- WTelegram library updates with API changes
- Monitor WTelegram GitHub for updates
- Graceful fallback if API calls fail

### Risk: Memory Leaks (Multiple Sessions)
**Mitigation:**
- Aggressive cleanup (30 min idle timeout)
- Dispose on web user logout
- Monitor memory usage in production
- TickerQ background job for cleanup

---

## Dependencies

### Existing Features
- ✅ Data Protection API (for session encryption)
- ✅ Audit logging system
- ✅ User management (permissions)
- ✅ telegram_users table
- ✅ messages table

### External Services
- Telegram MTProto API (via WTelegram)
- Telegram authentication servers

### New NuGet Packages
- WTelegramClient (v4.3.11+)

---

## Related Documents
- CLAUDE.md - Main project documentation
- BACKLOG.md - Feature roadmap and pending work
