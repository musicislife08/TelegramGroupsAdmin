# UserMessagingService Integration Examples

## Overview

The `UserMessagingService` provides intelligent message delivery with automatic DM preference handling and fallback to chat mentions. It's designed to respect user privacy while ensuring important notifications are always delivered.

## How It Works

1. **Check DM Preference**: Queries `telegram_users.bot_dm_enabled`
2. **Attempt DM**: If enabled, tries to send private message
3. **Handle Blocking**: Catches `403 Forbidden` errors when user blocks bot → sets `bot_dm_enabled=false`
4. **Fallback**: On any failure, sends as chat mention instead
5. **Result Tracking**: Returns `MessageSendResult` with delivery method

## Architecture

```
┌─────────────────────────────────────┐
│   Command (Warn/Ban/Report)         │
│   - Injects IUserMessagingService   │
└────────────┬────────────────────────┘
             │
             ▼
┌─────────────────────────────────────┐
│   UserMessagingService              │
│   - Query bot_dm_enabled            │
│   - Try DM if enabled               │
│   - Catch Forbidden → Update DB     │
│   - Fallback to chat mention        │
└─────────────────────────────────────┘
```

## Integration Examples

### Example 1: WarnCommand

**Before** (No DM support):
```csharp
// Old: Always sends in chat
return $"⚠️ @{targetUser.Username} has been warned. Reason: {reason}";
```

**After** (With DM + Fallback):
```csharp
public class WarnCommand : IBotCommand
{
    private readonly IUserMessagingService _messagingService;
    private readonly IUserActionsRepository _userActionsRepository;

    public WarnCommand(
        IUserMessagingService messagingService,
        IUserActionsRepository userActionsRepository)
    {
        _messagingService = messagingService;
        _userActionsRepository = userActionsRepository;
    }

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // ... existing validation code ...

        // Record warning in database
        var action = new UserActionRecord(/* ... */);
        await _userActionsRepository.InsertAsync(action, cancellationToken);

        // Notify user via DM (preferred) or chat mention (fallback)
        var notificationText = $"⚠️ **Warning Issued**\\n\\n" +
                              $"**Reason:** {reason}\\n" +
                              $"**Issued by:** Admin\\n\\n" +
                              $"Please review the group rules.";

        var result = await _messagingService.SendToUserAsync(
            botClient,
            userId: targetUserId,
            chatId: message.Chat.Id,
            messageText: notificationText,
            replyToMessageId: message.MessageId,
            cancellationToken);

        // Return confirmation to admin
        return result.DeliveryMethod == MessageDeliveryMethod.PrivateDm
            ? $"⚠️ Warning sent to user via DM."
            : $"⚠️ Warning sent (DM unavailable, posted in chat).";
    }
}
```

---

### Example 2: BanCommand

```csharp
public class BanCommand : IBotCommand
{
    private readonly IUserMessagingService _messagingService;
    private readonly ModerationActionService _moderationService;

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // ... validation and ban execution ...

        // Notify user why they were banned
        var banNotice = $"🚫 **You have been banned**\\n\\n" +
                       $"**Reason:** {reason}\\n" +
                       $"**Chat:** {message.Chat.Title}\\n\\n" +
                       $"If you believe this was a mistake, contact the chat admins.";

        var result = await _messagingService.SendToUserAsync(
            botClient,
            userId: targetUserId,
            chatId: message.Chat.Id,
            messageText: banNotice,
            replyToMessageId: null, // Don't reply to trigger message
            cancellationToken);

        return $"✅ User banned. Notification: {result.DeliveryMethod}";
    }
}
```

---

### Example 3: ReportCommand (Notify Multiple Admins)

```csharp
public class ReportCommand : IBotCommand
{
    private readonly IUserMessagingService _messagingService;
    private readonly IChatAdminsRepository _chatAdminsRepository;

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Get all chat admins
        var admins = await _chatAdminsRepository.GetChatAdminsAsync(message.Chat.Id, cancellationToken);
        var adminUserIds = admins.Select(a => a.TelegramId).ToList();

        // Notify all admins about the report
        var reportText = $"🚨 **New Report**\\n\\n" +
                        $"**Reported by:** @{message.From.Username}\\n" +
                        $"**Message:** [Jump to message](https://t.me/c/{Math.Abs(message.Chat.Id)}/{message.ReplyToMessage?.MessageId})\\n\\n" +
                        $"Please review in the Reports tab.";

        var results = await _messagingService.SendToMultipleUsersAsync(
            botClient,
            userIds: adminUserIds,
            chatId: message.Chat.Id,
            messageText: reportText,
            replyToMessageId: message.MessageId,
            cancellationToken);

        var dmCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.PrivateDm);
        var mentionCount = results.Count(r => r.DeliveryMethod == MessageDeliveryMethod.ChatMention);

        return $"✅ Report submitted. Notified {dmCount} admins via DM, {mentionCount} via chat mention.";
    }
}
```

---

### Example 4: Appeal System (Future - Phase 4.15)

```csharp
public class AppealHandler
{
    private readonly IUserMessagingService _messagingService;

    public async Task NotifyUserOfAppealDecisionAsync(
        ITelegramBotClient botClient,
        long userId,
        long chatId,
        bool approved,
        string? adminNotes = null)
    {
        var message = approved
            ? $"✅ **Appeal Approved**\\n\\nYour ban has been lifted. Welcome back!\\n\\n" +
              $"**Admin Notes:** {adminNotes ?? "None"}"
            : $"❌ **Appeal Denied**\\n\\n{adminNotes ?? "Your appeal has been reviewed and denied."}";

        await _messagingService.SendToUserAsync(
            botClient,
            userId,
            chatId,
            message,
            replyToMessageId: null);
    }
}
```

---

## Key Benefits

### 1. **Privacy-Respecting**
- Users must `/start` bot before receiving DMs (opt-in)
- Respects Telegram's anti-spam policies
- Automatic fallback when user blocks bot

### 2. **Resilient**
- Handles `403 Forbidden` errors gracefully
- Persists DM status to database (avoids repeated failures)
- Always delivers message (either DM or chat)

### 3. **Admin-Friendly**
- Cleaner chats (warnings/bans via DM when possible)
- Better user experience (private notifications)
- Audit trail in result objects

### 4. **Reusable**
- Single service for all notification needs
- Consistent behavior across commands
- Easy to test and mock

---

## Database Schema

```sql
-- telegram_users table
CREATE TABLE telegram_users (
    telegram_user_id BIGINT PRIMARY KEY,
    username VARCHAR(32),
    first_name VARCHAR(64),
    bot_dm_enabled BOOLEAN NOT NULL DEFAULT FALSE,  -- ← Key field
    ...
);
```

**Lifecycle:**
1. User first appears → `bot_dm_enabled = false`
2. User sends `/start` → `bot_dm_enabled = true` (StartCommand.cs:55)
3. Bot gets `403 Forbidden` → `bot_dm_enabled = false` (UserMessagingService.cs:117)

---

## Testing Scenarios

### Test Case 1: User Has DM Enabled
1. User sends `/start` to bot (sets `bot_dm_enabled=true`)
2. Admin issues `/warn @user Test warning`
3. **Expected:** User receives DM, chat stays clean

### Test Case 2: User Blocked Bot
1. User blocks bot after previously enabling
2. Admin issues `/ban @user Spam`
3. **Expected:** Bot tries DM → 403 error → Sets `bot_dm_enabled=false` → Sends chat mention

### Test Case 3: User Never Started Bot
1. New user joins group
2. Admin issues `/warn @newuser Be nice`
3. **Expected:** `bot_dm_enabled=false` → Sends chat mention directly

### Test Case 4: Multiple Admins Notification
1. User submits `/report` on spam message
2. Chat has 5 admins: 3 enabled DMs, 2 did not
3. **Expected:** 3 receive DMs, 2 get mentioned in chat

---

## Migration Guide

### For New Commands
Just inject `IUserMessagingService` and use `SendToUserAsync()`.

### For Existing Commands
Replace direct `botClient.SendMessage()` calls with service:

**Old:**
```csharp
await botClient.SendMessage(
    chatId: message.Chat.Id,
    text: $"@{user.Username}: {notification}",
    cancellationToken: cancellationToken);
```

**New:**
```csharp
await _messagingService.SendToUserAsync(
    botClient,
    userId: user.Id,
    chatId: message.Chat.Id,
    messageText: notification,
    cancellationToken: cancellationToken);
```

---

## Common Patterns

### Pattern 1: Fire-and-Forget Notification
```csharp
// Don't wait for result, just send
_ = _messagingService.SendToUserAsync(botClient, userId, chatId, "You were muted.");
```

### Pattern 2: Check Delivery Method
```csharp
var result = await _messagingService.SendToUserAsync(...);

if (result.DeliveryMethod == MessageDeliveryMethod.Failed)
{
    _logger.LogError("Failed to notify user {UserId}", userId);
}
```

### Pattern 3: Batch Admin Notifications
```csharp
var adminIds = await _chatAdminsRepository.GetAdminIdsAsync(chatId);
var results = await _messagingService.SendToMultipleUsersAsync(
    botClient, adminIds, chatId, "New spam detected");
```

---

## Performance Considerations

- **Database Queries:** 1 query per user (SELECT `bot_dm_enabled`)
- **API Calls:** 1 Telegram API call per user (DM or chat message)
- **Error Handling:** Automatic retry via fallback (no manual retry needed)

For batch notifications to 100+ users, consider:
- Queuing jobs (TickerQ)
- Rate limiting (Telegram: 30 messages/second)
- Pagination of admin lists

---

## Future Enhancements

1. **Rate Limiting:** Respect Telegram's 30 msg/sec limit for batch sends
2. **Delivery Receipt:** Track message IDs for edit/delete later
3. **Template System:** Predefined message templates (welcome, warn, ban, etc.)
4. **Localization:** Multi-language notifications based on user language
5. **Retry Logic:** Configurable retry for transient failures (not 403)

---

## Summary

The `UserMessagingService` provides a **production-ready** solution for user notifications with:

✅ Automatic DM preference detection
✅ Graceful fallback to chat mentions
✅ Database state persistence (avoid repeated failures)
✅ Multi-user support (admin broadcasts)
✅ Rich result tracking
✅ Privacy-respecting (opt-in via `/start`)

**Integrate today:** Just inject `IUserMessagingService` and replace your `SendMessage` calls!
