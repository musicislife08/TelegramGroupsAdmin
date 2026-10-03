# Ban Celebration

When a spammer gets banned, why not make it memorable? The **Ban Celebration** feature posts a random celebratory GIF with a witty caption to the chat every time a user is banned. Optionally, it can also send the celebration directly to the banned user via DM for maximum impact, and members can [subscribe](#dm-subscribers) to receive every celebration in their own DMs.

GIFs and captions are drawn from a shared library and paired randomly, with a shuffle-bag algorithm ensuring every item is shown before any repeats.

## How It Works

### Celebration Flow

When a ban occurs (automatic or manual), the system checks configuration, selects a random GIF and caption, replaces placeholder variables, and sends the result to the chat. The diagram below covers the chat post; [DM subscribers](#dm-subscribers) get the same celebration independently of the chat toggles.

```mermaid
flowchart TD
    A[User Banned] --> B{Ban Celebration<br/>Enabled?}
    B -->|No| Z[Skip]
    B -->|Yes| C{Trigger Type<br/>Matches Config?}
    C -->|No| Z
    C -->|Yes| D[Get Next GIF<br/>from Shuffle Bag]
    D --> E{GIF<br/>Available?}
    E -->|No| Z
    E -->|Yes| F[Get Next Caption<br/>from Shuffle Bag]
    F --> G{Caption<br/>Available?}
    G -->|No| Z
    G -->|Yes| H[Replace Placeholders]
    H --> I[Send GIF + Caption<br/>to Chat]
    I --> J{Send DM<br/>Enabled?}
    J -->|No| K[Done]
    J -->|Yes| L{DM-Based Welcome<br/>Mode Active?}
    L -->|No| K
    L -->|Yes| M[Send GIF + DM Caption<br/>to Banned User]
    M --> K

    style Z fill:#999
    style K fill:#6bcf7f
```

### Shuffle-Bag Algorithm

Ban celebrations use a **shuffle-bag** (Fisher-Yates shuffle) to select GIFs and captions. This guarantees that every GIF and every caption is shown exactly once before any can repeat, preventing the same celebration from appearing twice in a row.

**How it works:**

1. All GIF IDs are loaded from the database and shuffled into a random order (the "bag")
2. Each ban draws the next GIF from the bag
3. When the bag is empty, all IDs are reloaded and reshuffled
4. Captions use a separate, independent bag with the same algorithm

**Result:** If you have 10 GIFs and 10 captions, you will see all 10 GIFs and all 10 captions before any repeats. Since GIFs and captions are paired independently, you get up to 100 unique combinations before patterns emerge.

The shuffle-bag state is **persisted in the database**, not held in memory: each GIF/caption row has a `dispensed_at` column, and a Postgres advisory lock (`pg_advisory_xact_lock`) guards each claim so concurrent bans can't dispense the same row twice. When every row in a cycle has been dispensed, the next claim starts a fresh cycle by clearing the stamps and reshuffling.

---

## Trigger Configuration

Ban celebrations can be triggered by two types of bans, each independently toggleable:

| Trigger | Description | Default |
|---------|-------------|---------|
| **Auto-ban** | Triggered when spam detection automatically bans a user (confidence >= threshold) | Enabled |
| **Manual ban** | Triggered when an admin manually bans a user via the `/ban` command or web UI | Enabled |

Both triggers can be enabled simultaneously, or you can limit celebrations to only one type.

---

## Placeholder Variables

Captions support three placeholder variables that are replaced at send time:

| Placeholder | Chat Message | DM Message | Description |
|-------------|-------------|------------|-------------|
| `{username}` | Banned user's display name | `You` | The subject of the caption |
| `{chatname}` | Chat name | Chat name | The group where the ban occurred |
| `{bancount}` | Today's count | Today's count | Total bans across all chats today (resets at midnight) |

Placeholders are case-insensitive (`{Username}`, `{USERNAME}`, and `{username}` all work).

**Flagged names:** if [Profile Scanning](08-profile-scanning.md#masking-flagged-names) flagged the banned user's name, the chat caption shows `[name removed: explicit]` (or `[name removed: spam]`) for `{username}` instead of the real name, as every bot message does while **Mask flagged names** is on (the default). The DM version is unaffected (it already says "You").

### Chat vs. DM Grammar

Each caption has **two versions**: a chat caption and a DM caption. The DM version uses "You" grammar instead of the banned user's name, making the message more personal.

**Example caption pair:**

- **Chat:** `{username} has been eliminated! That makes {bancount} today in {chatname}.`
- **DM:** `You have been eliminated! That makes {bancount} today in {chatname}.`

---

## Configuration

### Global Settings

Navigate to **Settings > Moderation > Ban Celebration** to configure the global defaults and manage the GIF/caption library.

The global settings page has four sections:

1. **Global Configuration** -- Default toggle and trigger settings for all chats
2. **GIF Library** -- Upload, preview, and manage celebration GIFs
3. **Caption Library** -- Create, edit, and manage caption templates
4. **Test Preview** -- Preview a random GIF + caption combination

[Screenshot: Ban Celebration global settings page with all four sections visible]

### Per-Chat Settings

Individual chats can override the global defaults. Navigate to **Chat Management > (select chat) > Chat Configuration** to access per-chat ban celebration settings.

Per-chat settings include:

- **Enable/Disable** -- Turns the chat post on or off for this specific chat (does not affect [DM subscribers](#dm-subscribers))
- **Trigger on auto-ban** -- Override the global auto-ban trigger setting
- **Trigger on manual ban** -- Override the global manual ban trigger setting
- **Send DM to banned user** -- Override the global DM setting

When a chat has no override configured, the global defaults apply.

[Screenshot: Per-chat ban celebration settings in the Chat Configuration modal]

---

## Managing GIFs

### GIF Library

The GIF library is a global collection shared across all chats. Each GIF entry includes:

- **Preview** -- Static thumbnail that animates on hover; click to view full size
- **Name** -- Optional friendly name for identification
- **Cached** -- Whether Telegram has cached the file (speeds up delivery)
- **Added** -- Date the GIF was uploaded

### Uploading a GIF

1. Click **Add GIF** in the GIF Library section
2. Choose an upload method:
   - **Upload File** -- Select a `.gif` or `.mp4` file from your computer (up to 50 MB)
   - **From URL** -- Paste a direct link to a GIF or MP4 file
3. Optionally enter a friendly name (auto-populated from filename if uploading)
4. Click **Add GIF**

The system automatically:

- Generates a thumbnail from the first frame
- Computes a perceptual hash for duplicate detection
- Checks for visually similar GIFs already in the library (87.5% similarity threshold)

If a similar GIF is detected, you can choose to **Keep Both** or **Cancel Upload**.

[Screenshot: Add GIF dialog with file upload tab selected]

### Deleting a GIF

1. Click the delete icon next to the GIF in the library table
2. Confirm the deletion in the dialog

Deleting a GIF removes both the database record and the file from disk. If the deleted GIF is still in the shuffle bag, it will be skipped automatically on next draw.

### File ID Caching

The first time a GIF is sent to Telegram, the API returns a `file_id`. This ID is cached so subsequent sends are instant (no re-upload needed). If the cached ID becomes stale, the system automatically falls back to uploading from the local file and caches the new ID.

---

## Managing Captions

### Caption Library

The caption library is a global collection shared across all chats. Each caption has:

- **Name** -- Optional friendly name (e.g., "Mortal Kombat - Fatality")
- **Chat Caption** -- Text posted to the group chat
- **DM Caption** -- Text sent directly to the banned user

### Creating a Caption

1. Click **Add Caption** in the Caption Library section
2. Enter an optional name
3. Write the **Chat Caption** using placeholder variables
4. Write the **DM Caption** (typically the same text but with "You" grammar)
5. Review the live preview panels that show how the caption will render
6. Click **Add Caption**

The preview replaces placeholders with example values (`SpammerX` for username, `Test Chat` for chatname, `42` for bancount) and renders Markdown formatting.

[Screenshot: Add Caption dialog with preview panels showing rendered Markdown]

### Editing a Caption

1. Click the edit icon next to the caption in the library table
2. Modify the text as needed
3. Review the updated preview
4. Click **Save Changes**

### Deleting a Caption

1. Click the delete icon next to the caption
2. Confirm the deletion

If the deleted caption is still in the shuffle bag, it will be skipped automatically on next draw.

### Caption Formatting

Captions support **Markdown** formatting. The chat message is sent using Telegram's Markdown parse mode, and DM messages use MarkdownV2 (escaped automatically by the system).

---

## DM Behavior

When **Send DM to banned user** is enabled, the system attempts to send the celebration GIF and DM caption directly to the banned user's private messages.

### Requirements

DM delivery requires **all** of the following:

1. The **Send DM to banned user** toggle is enabled (global or per-chat)
2. The chat uses a **DM-based welcome mode** (either DmWelcome or EntranceExam)
3. The banned user has previously interacted with the bot (started a conversation)

### DM Delivery Details

- The DM caption uses `{username}` replaced with `You` for direct address
- The GIF is sent as an **animation**, reusing the cached Telegram `file_id` when there is one (see [File ID Caching](#file-id-caching)), so it is not re-uploaded for every DM
- DM failures are handled silently -- if the user has blocked the bot or never started it, the celebration still posts to the chat
- There is no fallback: a DM that cannot be delivered (e.g., the user blocked the bot) is **not** queued in the pending notification system, so nobody gets a stale celebration replayed later

---

## DM Subscribers

Any member of a chat can opt in to receive that chat's ban celebrations as a private message from the bot. This suits chats where some members love the celebrations and others would rather not see them: an admin can turn the chat post off and members who want the celebrations still get them in their DMs.

### Subscribing and Unsubscribing

Members run the command **in the group** they want celebrations from:

| Command | Result |
|---------|--------|
| `/dmcelebrations on` | Subscribes you to this chat's ban celebrations |
| `/dmcelebrations off` | Unsubscribes you from this chat |
| `/dmcelebrations` | Shows whether you are subscribed in this chat |

The command message is deleted and the bot's reply is removed after 30 seconds. Running the command in a private chat with the bot, or while posting as the group or a channel (anonymous admin), is refused -- subscribing from inside the group is what proves membership.

### Starting the Bot

Telegram only lets a bot DM users who have started a conversation with it. If you haven't (or you blocked the bot), `/dmcelebrations on` still saves your subscription and posts a short prompt in the group that mentions you, with an **Open a chat with me** button:

1. Tap the button -- Telegram opens a private chat with the bot and shows **Start** (or **Restart** if you blocked it)
2. Tap **Start** -- the bot confirms "You're all set" in the DM and deletes the prompt from the group
3. If you don't tap it, the prompt deletes itself after **60 seconds**. Your subscription stays; DMs begin whenever you start the bot by any route

Running `/dmcelebrations on` again while a prompt is open replaces it, so there is never more than one prompt per member per chat.

### What Subscribers Receive

- The same GIF and chat caption as the chat post (including [flagged-name masking](#placeholder-variables)), headed with the **chat name** so members subscribed to several chats can tell where each one came from
- A DM for **every** ban celebration in the chat. **Enable** and the **Trigger on auto-ban / manual ban** toggles control **only the chat post** -- subscribers get every celebration even when the chat post is off
- If the chat post is off and nobody is subscribed, nothing happens and no GIF or caption is used up from the rotation

DMs are sent one at a time in the background, paced to stay under Telegram's rate limits. The first DM uploads the GIF and every later one reuses the cached `file_id`.

### When a Subscription Is Removed

| Event | Removes |
|-------|---------|
| `/dmcelebrations off` | That chat's subscription |
| Leaving the chat or being kicked | That chat's subscription |
| Being banned | All of the user's subscriptions (before the celebration, so a banned user never gets a celebration of their own ban) |
| Blocking the bot | All of the user's subscriptions |

Blocking the bot is the way to stop every celebration DM at once. Unblocking it does not restore old subscriptions -- run `/dmcelebrations on` again in each chat.

There is no admin control over subscriptions and no web UI for them.

---

## Test Preview

The global settings page includes a **Test Preview** section where you can preview random GIF + caption combinations without triggering an actual ban.

1. Ensure you have at least one GIF and one caption in the library
2. Click **Test Random Combo**
3. The preview shows:
   - The selected GIF (animated)
   - The chat caption with placeholders replaced using example values
   - The DM caption with "You" grammar

This is useful for verifying that your captions render correctly and that GIF + caption pairings look good together.

[Screenshot: Test Preview section showing a random GIF paired with rendered chat and DM captions]

---

## Troubleshooting

### Celebrations not posting

- **Check enabled state** -- Verify ban celebration is enabled both globally and for the specific chat
- **Check trigger type** -- If only auto-ban is enabled, manual bans will not trigger celebrations (and vice versa)
- **Verify library content** -- Both GIFs and captions are required; if either library is empty, celebrations are silently skipped
- **Check bot permissions** -- The bot needs permission to send animations in the group

### GIF not displaying / "wrong file identifier" errors

- **Stale file cache** -- The cached `file_id` may have expired. The system automatically clears stale IDs and re-uploads from the local file
- **Missing file on disk** -- If the GIF file was deleted from the `/data/media` directory, the GIF will be skipped. Re-upload it through the UI
- **File too large** -- Telegram limits file uploads to 20 MB via the Bot API

### DM not being sent to banned user

- **Welcome mode** -- DM delivery requires DM-based welcome mode (DmWelcome or EntranceExam) to be active for the chat
- **User never started bot** -- The banned user must have previously started a conversation with the bot
- **User blocked bot** -- If the user blocked the bot, DM delivery fails silently
- **DM toggle** -- Verify the "Send celebration to banned user via DM" toggle is enabled

### Subscriber not getting celebration DMs

- **Bot not started** -- The subscriber must have started a conversation with the bot. Run `/dmcelebrations on` in the group again and tap the button in the prompt
- **Blocked the bot** -- Blocking removes every subscription; after unblocking, run `/dmcelebrations on` again in each chat
- **Left or banned** -- Leaving, being kicked, or being banned removes the subscription
- **Library empty** -- Both GIFs and captions are required, even when the chat post is off

### Duplicate GIF warning on upload

- The system uses perceptual hashing to detect visually similar GIFs (87.5% similarity threshold)
- If you receive a duplicate warning but the GIFs are genuinely different, click **Keep Both**
- If the GIF is truly a duplicate, click **Cancel Upload**

### Same GIF or caption appearing frequently

- **Small library** -- With only 2-3 GIFs, repeats will be noticeable even with the shuffle bag. Add more variety to the library

---

## Related Documentation

- **[Spam Detection Guide](03-spam-detection.md)** -- Understand how auto-bans are triggered
- **[Reports Queue](02-reports.md)** -- Review borderline detections before they become bans
- **[Messages](01-messages.md)** -- View ban celebration messages in the message browser
