# Per-User Message History

The **Per-User Message History** dialog provides a cross-chat view of all messages sent by a specific Telegram user across every monitored group. Instead of searching through individual chat logs, you can open a single dialog and see everything that user has posted -- regardless of which group they sent it in.

---

## How to Access

The per-user message history is opened from the **User Detail Dialog**:

1. Open the User Detail Dialog by clicking on any user (from the Users list, a report review, or the message browser)
2. Click the **View Messages** button in the user's profile header

[Screenshot: User Detail Dialog with the View Messages button highlighted]

This opens a scrollable dialog titled "Messages from [username]" showing the user's message history in reverse chronological order (newest first).

---

## Features

### Cross-Chat Message View

Each message displays a **chat badge** above the message bubble, showing which group the message was posted in. The badge includes the chat's profile icon (or a default forum icon if none is set) and the chat display name. This makes it straightforward to see at a glance which group each message belongs to.

### Message Display

Messages are rendered using the same `MessageBubbleTelegram` component used in the main message browser, providing a consistent presentation including:

- Message text content
- Media attachments (photos, GIFs, videos, documents, etc.)
- Timestamps
- Reply context (if the message was a reply)
- Translations (for foreign-language messages)

### Jump to Message

Below each message, a **Jump to message** link navigates directly to that message in the main message browser, filtered to the relevant chat with the message highlighted. Clicking this link closes the per-user dialog and loads the Messages page.

### Pagination

Messages load in pages of **50**. When more messages are available, a **Load More** button appears at the bottom of the list. Each additional page fetches the next 50 messages older than the last displayed message, using cursor-based pagination for consistent results.

---

## Permission-Aware Filtering

The dialog respects the current web user's permission level:

- **Admin** users see messages only from chats they are a Telegram admin in
- **GlobalAdmin** and **Owner** users see messages from all managed chats

This ensures that admins cannot view message history from chats they do not have access to.

---

## Use Cases

### Investigating Suspicious Users

When a user is reported or flagged by spam detection, reviewing their full message history across all groups helps determine whether the behavior is isolated or part of a broader pattern.

### Reviewing Message Patterns

Before taking moderation action, admins can check whether a user has been posting similar content across multiple groups -- a common indicator of coordinated spam.

### Pre-Moderation Context

When reviewing a report or considering a ban, the per-user message history provides the full picture of the user's activity without needing to switch between individual chat logs.

---

## Troubleshooting

### "No messages found for this user"

- The user may not have sent any messages in chats you have access to
- If you are an Admin (not GlobalAdmin or Owner), you will only see messages from chats where you are a Telegram admin
- The user may have joined but never posted

### Messages from a specific chat are missing

- Verify that the chat is registered as a managed chat in the system
- If you are an Admin, confirm you are listed as an admin for that chat in Telegram

### Dialog is slow to load

- Users with extensive message history across many chats may take longer to load the initial page
- Subsequent "Load More" requests fetch the same page size (50 messages) and should remain responsive

---

## Related Documentation

- **[Messages](01-messages.md)** -- Main message browser where "Jump to message" navigates to
- **[Reports Queue](02-reports.md)** -- Report review workflow where user investigation often starts
- **[Spam Detection Guide](03-spam-detection.md)** -- How automatic spam detection flags users for review
