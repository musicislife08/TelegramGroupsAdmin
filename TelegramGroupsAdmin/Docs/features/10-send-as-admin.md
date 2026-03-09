# Send As Admin

The **Send As Admin** feature lets you send messages to your Telegram groups as your personal Telegram account instead of the bot. When enabled, a **Bot/Me toggle** appears in the ChatInput area of the Messages page, allowing you to switch between sending as the bot (with a signature) or as yourself (with no signature, appearing natively in the chat).

This is powered by the **WTelegram User API** -- the same MTProto session infrastructure used by profile scanning. Messages sent in "Me" mode are indistinguishable from messages you would send directly from the Telegram app.

---

## How It Works

When you open the Messages page and select a chat, the system performs two availability checks:

1. **Feature availability** -- Does your web user account have an active WTelegram session?
2. **Chat availability** -- Is your personal Telegram account a member of the selected chat?

If both checks pass, the Bot/Me toggle is enabled and defaults to **Me** mode. If either check fails, the toggle is disabled with a tooltip explaining why.

### Message Routing

| Mode | API Used | Signature | Bot Sees Update |
|------|----------|-----------|-----------------|
| **Bot** | Telegram Bot API | Appended (e.g., `\n\n--username`) | No -- manual page refresh required |
| **Me** | WTelegram User API (MTProto) | None -- message appears from your account | Yes -- bot receives it as a normal incoming message and the UI updates automatically |

When sending in "Me" mode, the message is routed through `WebUserMessagingService`, which resolves the chat to an `InputPeer` via the session's peer cache and calls `SendMessageAsync` on the WTelegram client. The bot's polling loop picks up the message as a normal incoming message, so the chat view updates without a manual refresh.

When sending in "Bot" mode, the message is routed through `WebBotMessagingService` and includes the admin's linked username as a signature. Since the bot does not receive updates for its own messages, the page performs a manual refresh after sending.

---

## Prerequisites

Before you can use Send As Admin, the following must be in place:

1. **User API credentials configured** -- An Owner must set the Telegram API ID and API Hash in **Settings > Telegram > User API Settings**. These credentials are obtained from [my.telegram.org](https://my.telegram.org) and are shared across all admins.

2. **Personal Telegram account connected** -- Each admin connects their own Telegram account from their **Profile** page under the **Telegram User API** section. The connection flow is:
   - Enter your phone number (international format)
   - Enter the verification code sent to your Telegram app
   - Enter your 2FA password (if enabled on your account)

3. **Membership in the target chat** -- Your personal Telegram account must be a member of the group you want to send to. The system checks this via the WTelegram peer cache.

---

## UI Behavior

### The Bot/Me Toggle

The toggle button appears to the left of the message input field in the ChatInput component.

| State | Appearance | Behavior |
|-------|------------|----------|
| **Bot mode** | Robot icon with "Bot" label | Messages sent via Bot API with signature |
| **Me mode** | Person icon with "Me" label | Messages sent via User API, no signature |
| **Checking** | Skeleton placeholder | Displayed while the system verifies chat availability |
| **Disabled** | Grayed out with tooltip | User API unavailable or not a member of the chat |

[Screenshot: ChatInput area showing the Bot/Me toggle in Me mode with a message being composed]

When you switch chats, the toggle resets and the system re-checks availability for the new chat. If the User API is available for the new chat, it defaults to "Me" mode.

### Placeholder Text

The input field placeholder changes based on the current mode:

- **Me mode**: "Message as you..."
- **Bot mode**: "Message as bot..."
- **Unavailable**: Shows the specific reason (e.g., "Connect your Telegram account in Settings")

### Message Length Limits

- **Me mode**: Full 4096 character limit (no signature appended)
- **Bot mode**: Reduced by the signature length (typically 4096 minus the length of `\n\n--` plus your linked username)

---

## Editing Messages

The toggle state also controls how message edits are routed:

- **Me mode**: Edits are sent via `Messages_EditMessage` on the WTelegram client. You can only edit messages that were originally sent by your personal account.
- **Bot mode**: Edits are sent via the Bot API. You can only edit messages that were originally sent by the bot.

Attempting to edit a message sent by one API using the other will fail with a Telegram API error.

---

## When to Use Each Mode

| Scenario | Recommended Mode |
|----------|-----------------|
| Responding to a user's question personally | **Me** -- appears as a real person, builds trust |
| Posting an announcement as yourself | **Me** -- community sees your name and profile photo |
| Sending automated or impersonal notices | **Bot** -- clearly distinguished as a system message |
| Testing bot features or commands | **Bot** -- the bot can interact with its own messages |

---

## Session Management

### Connecting Your Account

1. Navigate to your **Profile** page
2. Scroll to the **Telegram User API** section
3. Enter your phone number in international format (e.g., `+1234567890`)
4. Click **Connect**
5. Enter the verification code from your Telegram app
6. If prompted, enter your 2FA password
7. The status changes to **Connected** with your display name and Telegram ID

[Screenshot: Profile page showing the Telegram User API section in connected state]

### Session Persistence

Sessions are stored encrypted in the database and reconnect automatically when the application restarts. The `TelegramSessionManager` maintains a cache of warm client connections and lazily reconnects from stored session data on first access.

### Disconnecting

1. Navigate to your **Profile** page
2. In the **Telegram User API** section, click **Disconnect**
3. The session is deactivated in the database and the cached client is disposed

Disconnecting from within Telegram itself (e.g., terminating the session from Telegram's Active Sessions settings) is also detected automatically. The system handles `AUTH_KEY_UNREGISTERED`, `SESSION_REVOKED`, and related errors by cleaning up the session and logging an audit event.

---

## Troubleshooting

### Toggle shows "Connect your Telegram account in Settings"

Your web user does not have an active WTelegram session. Connect your Telegram account from your **Profile** page. If the Telegram User API section shows a message about API credentials, ask an Owner to configure them in **Settings > Telegram > User API Settings**.

### Toggle shows "You're not a member of this group with your personal account"

Your personal Telegram account is not a member of the selected chat. Join the group from your Telegram app, then switch to a different chat and back to trigger a re-check. The system refreshes the peer cache automatically when checking chat availability.

### Toggle shows "Rate limited -- try again in Xs"

The WTelegram client hit Telegram's rate limits. Wait for the indicated duration and try again. The flood gate protects against further calls until the wait expires. Waits under 60 seconds are handled transparently on the next attempt.

### Message sent but chat did not update

If you sent in **Bot mode**, the bot does not receive updates for its own messages. The page should refresh automatically after a bot-mode send. If it did not, manually refresh the page.

If you sent in **Me mode** and the message does not appear, check the Telegram group directly to confirm delivery. The bot's polling loop should pick up the message within a few seconds.

### "No connected Telegram account" error when sending

Your session may have been revoked or expired since the page loaded. Navigate to your **Profile** page and reconnect your Telegram account.

### Edit failed with a Telegram error

You can only edit messages sent by the same account. If you sent a message in "Me" mode, you must edit it in "Me" mode. If you sent it in "Bot" mode, use "Bot" mode to edit.

---

## Related Documentation

- **[Messages](01-messages.md)** -- Full guide to the Messages page where Send As Admin is used
- **[Profile Scanning](08-profile-scanning.md)** -- Uses the same WTelegram User API sessions
