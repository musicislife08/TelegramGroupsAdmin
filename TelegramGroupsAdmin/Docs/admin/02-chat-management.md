# Chat Management - Managing Telegram Groups

Chat Management allows you to monitor, configure, and troubleshoot the Telegram groups your bot is managing. View health status, configure per-chat settings, and manage the welcome system.

**Access**: Chat Management (GlobalAdmin/Owner only)

## Page Overview

The Chat Management page displays all monitored Telegram chats in a table format:

**Table columns**:
- **Chat Name** - Telegram group name
- **Health Status** - Bot health indicator (✓⚠️✗)
- **Member Count** - Number of group members
- **Last Activity** - When last message was received
- **Bot Status** - Connected, Disconnected, Error
- **Actions** - Configure, View Details

[Screenshot: Chat Management table with multiple chats]

---

## Chat Health Status

Each chat displays a health indicator showing bot status:

### ✓ Healthy (Green)

**All checks passed**:
- ✓ Bot is connected and polling
- ✓ Bot has admin permissions
- ✓ Bot can send messages
- ✓ Bot can delete messages
- ✓ Bot can ban users
- ✓ Invite link is valid

**Action**: None needed, bot functioning normally

---

### ⚠️ Warning (Yellow)

**Some issues detected**:
- Bot connected but missing some permissions
- Invite link expired or invalid
- Polling delays
- Intermittent connection issues

**Action**: Review warnings and resolve issues

**Common warnings**:
- "Bot cannot delete messages" → Grant Delete Messages permission
- "Invite link expired" → Generate new link in Telegram
- "Polling delay detected" → Check network/server load

---

### ✗ Unhealthy (Red)

**Critical issues**:
- Bot not connected to chat
- Bot not an admin in group
- Bot token invalid
- Chat ID mismatch

**Action**: Immediate attention required, bot not functioning

**Common errors**:
- "Bot not found in chat" → Add bot back to group
- "Bot not admin" → Promote bot to admin with required permissions
- "Authentication failed" → Verify TELEGRAM__BOTTOKEN is correct

[Screenshot: Health status indicators with details]

---

## Bot Permissions Required

For the bot to function properly, it needs these Telegram admin permissions:

### Essential Permissions

**Delete Messages** ✓
- Required to remove spam
- Without this, messages stay even after ban

**Ban Users** ✓
- Required to ban spammers
- Without this, users can return after being removed

**Invite Users via Link** ✓
- Required for welcome system
- Needed to generate invite links

### Recommended Permissions

**Pin Messages** (optional)
- Useful for pinning announcements
- Not required for spam detection

**Manage Topics** (optional)
- For groups with topics/forums
- Not required for basic groups

**Add Administrators** ❌
- Not recommended for security
- Bot doesn't need this

[Screenshot: Telegram bot permissions screen]

---

## Per-Chat vs. Global Configuration

TelegramGroupsAdmin supports **per-chat configuration overrides**:

### Global Configuration (Default)

**Applied to**: All chats

**Settings**:
- Detection algorithm enables/disables
- Thresholds (auto-ban, review queue)
- URL filtering rules
- Stop words library

**Use when**: You want consistent rules across all groups

---

### Per-Chat Configuration (Override)

**Applied to**: Specific chat only

**Can override**:
- Detection algorithms (enable/disable per chat)
- Thresholds (stricter/lenient per chat)
- Welcome system settings (per chat)
- First message checks (per chat)

**Cannot override** (global only):
- Stop words library
- URL blocklists
- File scanning rules
- OpenAI API keys

**Use when**: Different groups have different needs

**Example**:
```
Chat A (Crypto): Allows "moon" and "lambo", moderate thresholds
Chat B (Professional): Strict thresholds, blocks all crypto terms
```

[Screenshot: Per-chat configuration dialog]

---

## Configuring a Chat

### How to Configure

1. Navigate to **Chat Management** page
2. Find the chat in the table
3. Click **Configure** button
4. Configure settings tabs appear

### Configuration Tabs

#### General Tab

**Chat-specific settings**:
- **Spam Detection Enabled** - Toggle spam detection on/off for this chat
- **Use Global Config** - Use global settings or override
- **Auto-Ban Threshold** - Override global threshold (0-100)
- **Review Queue Threshold** - Override global threshold (0-100)
- **First Message Only** - Check only first N messages from new users

**Example**:
```
Global: 85 auto-ban, 70 review
Chat A Override: 90 auto-ban, 75 review (more lenient)
Chat B: Use Global (no override)
```

#### Welcome System Tab

**Per-chat welcome configuration** (explained in detail below)

#### Algorithms Tab (Future Feature)

**Per-chat algorithm enables**:
- Enable/disable specific algorithms for this chat
- Override algorithm thresholds
- Chat-specific URL whitelist

---

## Welcome System (Per-Chat)

The Welcome System greets new members and can challenge them to verify they're human.

### Welcome System Settings

Navigate to Chat Management → Configure → Welcome System tab:

**Enable Welcome** ✓/✗
- Toggle welcome messages on/off for this chat

**Welcome Message**
- Custom message sent to new members
- Supports Markdown formatting
- Variables: `{username}`, `{chatname}`

**Challenge Enabled** ✓/✗
- Require new members to answer challenge

**Challenge Type**
- Text question (e.g., "What is 2+2?")
- Emoji reaction (click specific emoji)

**Challenge Timeout**
- How long to wait for answer (seconds)
- Default: 60 seconds

**Timeout Action**
- Ban user if timeout
- OR Just remove welcome message

**Skip for Admins** ✓/✗
- Don't challenge existing admins

**Skip for Trusted Users** ✓/✗
- Don't challenge users with 10+ messages in other monitored chats

[Screenshot: Welcome System configuration]

### Example Welcome Configurations

**Example 1: Simple Welcome**
```
Welcome Message: "Welcome {username} to {chatname}! Please read our rules."
Challenge: Disabled
```

**Example 2: Challenge with Text**
```
Welcome Message: "Welcome! Please answer: What is 2+2?"
Challenge: Enabled (Text)
Expected Answer: "4"
Timeout: 60 seconds
Timeout Action: Ban
```

**Example 3: Emoji Challenge**
```
Welcome Message: "Welcome! Click the ✅ to verify you're human"
Challenge: Enabled (Emoji)
Expected Emoji: ✅
Timeout: 30 seconds
Timeout Action: Ban
```

---

## Monitoring Chat Health

### Health Checks

TelegramGroupsAdmin runs health checks every **60 seconds**:

**Checks performed**:
1. **Bot connection** - Is bot polling for messages?
2. **Admin status** - Is bot still an admin in the chat?
3. **Permissions** - Does bot have required permissions?
4. **Message sending** - Can bot send messages?
5. **Message deletion** - Can bot delete messages?
6. **Invite link** - Is the invite link valid?

**Results**:
- All pass → ✓ Healthy
- Some fail → ⚠️ Warning
- Critical fail → ✗ Unhealthy

### Health Details

Click **View Details** on any chat to see:
- Complete health check results
- Last successful check timestamp
- Error messages (if any)
- Bot permissions list
- Invite link status

[Screenshot: Chat health details panel]

---

## Managing Chat Admins

TelegramGroupsAdmin automatically syncs chat admins from Telegram:

### Admin Synchronization

**Automatic sync**:
- Runs every 60 minutes
- Fetches current admin list from Telegram
- Updates chat_admins table
- Affects which web users can see this chat

**How it works**:
1. Bot calls Telegram API: `getChatAdministrators`
2. Gets list of user IDs with admin status
3. Compares to database
4. Adds new admins, removes demoted admins

### Viewing Chat Admins

In Chat Management → View Details:
- **Admins List** - All current Telegram admins
- **Username** - Telegram username
- **Status** - Admin or Owner
- **Permissions** - What they can do

**Web admin access**:
- Web users with Admin permission level
- Can ONLY see chats they're Telegram admin in
- Chat admins table controls this

---

## Troubleshooting

### Bot not receiving messages

**Symptoms**:
- Last Activity shows old timestamp
- No new messages in Messages page
- Health status shows error

**Solutions**:
1. **Check bot is still in chat**:
   - Open Telegram
   - Verify bot appears in members list
2. **Check Privacy Mode**:
   - Message @BotFather
   - Send `/mybots`
   - Select your bot
   - Bot Settings → Group Privacy → **OFF**
3. **Check bot token**:
   - Settings → Telegram → Bot Configuration
   - Verify TELEGRAM__BOTTOKEN is correct
4. **Restart bot**:
   - Restart application
   - Bot will reconnect and start polling

---

### Bot can't delete spam

**Symptoms**:
- Spam detected but message stays
- Health shows "Cannot delete messages" warning

**Solutions**:
1. **Grant Delete Messages permission**:
   - Open Telegram
   - Chat Info → Administrators
   - Click bot → Edit permissions
   - Enable **Delete Messages**
2. **Verify bot is admin**:
   - Bot must be administrator, not just member
3. **Check message age**:
   - Telegram API limits: Can only delete messages <48 hours old

---

### Welcome system not working

**Symptoms**:
- New members join but no welcome message
- Challenge not appearing

**Solutions**:
1. **Check Welcome System enabled**:
   - Chat Management → Configure → Welcome System
   - Toggle should be ON
2. **Check bot can send messages**:
   - Health check shows "Can send messages" ✓
   - Grant permission if missing
3. **Check invite link valid**:
   - Health check shows invite link status
   - Regenerate in Telegram if expired
4. **Check TickerQ jobs running**:
   - Settings → Background Jobs
   - WelcomeTimeoutJob should be active

---

### Health checks failing intermittently

**Symptoms**:
- Health flips between ✓ and ⚠️
- No consistent error

**Solutions**:
1. **Check network connection**:
   - Server may have intermittent connectivity
   - Check firewall rules
2. **Check Telegram API status**:
   - Visit https://telegram.org/status
   - API may be experiencing issues
3. **Check server load**:
   - High CPU/memory may delay health checks
   - Optimize or upgrade server
4. **Increase timeout**:
   - Future feature: Configurable health check timeout

---

## Chat Name Updates

TelegramGroupsAdmin automatically updates chat names:

**When updated**:
- Bot receives `ChatTitleChanged` event from Telegram
- Name updated immediately in database
- Reflected in Chat Management table and Messages sidebar

**Manual update** (if needed):
- Not currently exposed in UI
- Restarting application will refresh all chat names

---

## Removing a Chat

To stop monitoring a chat:

**Option 1: Remove bot from Telegram**:
1. Open Telegram
2. Go to chat
3. Remove bot from members
4. Bot will detect removal and stop polling
5. Chat remains in database but shows "Disconnected"

**Option 2: Delete from database** (future feature):
- Currently not exposed in UI
- Chat data preserved indefinitely
- Future: Manual chat deletion with data purge option

---

## Analytics Per Chat

View analytics for specific chats:

1. Navigate to **Analytics** page
2. Use **Chat Filter** dropdown
3. Select specific chat or "All Chats"

**Metrics shown**:
- Spam detection rate for this chat
- Message volume over time
- False positive rate (if marking as ham)
- Most active users
- Most common spam patterns

[Screenshot: Analytics filtered to single chat]

---

## Related Documentation

- **[Welcome System](https://future-docs/welcome-system.md)** - Detailed welcome configuration
- **[First Configuration](../getting-started/02-first-configuration.md)** - Initial bot setup
- **[Messages Tab](../features/01-messages.md)** - Browsing chat messages
- **[Dashboard](https://future-docs/dashboard.md)** - Health overview

---

**Final step: Secure your account** → Continue to **[Profile & Security](../user/01-profile-security.md)**!
