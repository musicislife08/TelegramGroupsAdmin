# Messages Tab - Your Message Browser

The **Messages** page is your central hub for browsing, searching, and managing all Telegram messages from your monitored groups. Think of it as a Telegram client built into your web browser, with powerful moderation tools at your fingertips.

**This will likely be your most-used feature** for day-to-day monitoring and management.

## Page Overview

The Messages page has a two-panel layout similar to Telegram:

- **Left Panel** - Chat list with all your monitored groups
- **Right Panel** - Messages from the selected chat
- **Top Bar** - Filters and search controls

[Screenshot: Messages page full layout]

---

## Left Panel - Chat List

The left sidebar shows all Telegram chats the bot is monitoring.

### Chat Display

Each chat shows:
- **Chat name** - The Telegram group name
- **Avatar** - Group photo (if available)
- **Last message time** - When the most recent message was received
- **Unread count** - Number of new messages since you last viewed (if implemented)

### Selecting a Chat

1. Click any chat in the list
2. Messages from that chat load in the right panel
3. The selected chat is highlighted
4. URL updates to `/messages?chatId=123` (bookmarkable!)

### Chat Sorting

Chats are typically sorted by most recent activity (newest messages first).

[Screenshot: Chat list sidebar]

---

## Right Panel - Message View

The main message display area shows messages in chronological order (oldest to newest, like Telegram).

### Message Layout

Each message displays:
- **User avatar** - Profile photo (automatically fetched in background)
- **Username** - Telegram username or first name
- **Timestamp** - When the message was sent
- **Message content** - Full text of the message
- **Media** - Images, videos, audio, stickers, etc. (displayed inline)
- **Status indicators**:
  - 📝 **Edited** - Pencil icon if message was edited
  - 🚫 **Spam** - Red badge if flagged as spam
  - 🗑️ **Deleted** - Gray badge if message was deleted
- **Actions menu** - Three-dot menu (⋮) for message actions

### Media Display

TelegramGroupsAdmin automatically displays media inline:

**Images**:
- Displayed full-width
- Click to view full size
- Hover to see file details

**Videos**:
- HTML5 video player with controls
- Play/pause, seek, volume
- Autoplay for GIFs/animations

**Audio/Voice Messages**:
- Audio player with waveform (if available)
- Play/pause, seek
- Shows duration

**Stickers/Animations**:
- Displayed inline
- Autoplay for animated stickers

**Documents**:
- Shows file name, size, and icon
- No inline display (security precaution)
- File scanner processes these automatically

[Screenshot: Messages with various media types]

---

## Infinite Scroll

The Messages page uses **infinite scroll** to load messages efficiently without pagination buttons.

### How It Works

1. Initially loads **50 messages** from the selected chat
2. As you scroll to the top, automatically loads **50 more messages**
3. Continues loading until you reach the oldest message or hit retention limit
4. **Scroll position is preserved** when you navigate away and return

### Performance

- **Fast loading** - Only loads what you see
- **No lag** - Uses virtual scrolling for smooth performance
- **Timestamp-based** - Uses `beforeTimestamp` parameter for reliable pagination

### Tips

- **Scroll to top** to load older messages
- **Refresh page** to reload recent messages
- **Use filters** to narrow results before scrolling

[Screenshot: Infinite scroll in action]

---

## Filtering and Search

The top bar contains powerful filtering options to narrow down messages.

### Available Filters

#### Spam Status Filter

Filter by spam detection status:
- **All Messages** - Show everything (default)
- **Spam Only** - Only messages flagged as spam
- **Not Spam** - Only legitimate messages

**Use case**: Review all spam detections at once

#### Deleted Messages Filter

Toggle to show/hide deleted messages:
- **Show Deleted** - Include messages that were deleted
- **Hide Deleted** - Only show active messages (default)

**Use case**: Audit deleted content or clean up view

#### Date Range Filter

Select a date range to view messages from a specific timeframe:
- **Start Date** - Oldest message to include
- **End Date** - Newest message to include
- **Clear** - Remove date filter

**Use case**: Review messages from a specific event or time period

#### User Search

Search for messages from a specific user:
- Type username or name
- Autocomplete suggestions appear
- Select user to filter

**Use case**: Review all messages from a suspected spammer

### Combining Filters

You can combine multiple filters:
- **Example**: Spam messages from user "JohnDoe" between March 1-15
- Filters are cumulative (AND logic)

### Clearing Filters

Click **Clear Filters** or **Reset** to remove all active filters and return to default view.

[Screenshot: Filter controls with multiple filters active]

---

## Message Actions

Click the three-dot menu (⋮) on any message to access actions.

### Available Actions

#### View Details

Opens a detailed view showing:
- Full message text
- All metadata (timestamp, user ID, chat ID)
- Spam detection breakdown (if flagged)
- Edit history (if message was edited)
- Translations (if message was translated)

**Keyboard shortcut**: Click anywhere on the message card

#### Ban User

Permanently ban the user who sent this message:
- User is banned from the Telegram group
- Cannot rejoin
- Option to delete all their messages
- Adds entry to Audit Log

**Confirmation required**: Yes

**Use case**: Manually ban a spammer that wasn't auto-banned

#### Warn User

Issue a warning to the user:
- User receives a warning in the group
- Warning is tracked in Users page
- No ban, just a notice
- Can configure auto-ban after X warnings

**Use case**: First offense for borderline behavior

#### Delete Message

Delete this specific message from Telegram:
- Message removed from the group immediately
- Marked as deleted in Messages view
- Cannot be undone
- Adds entry to Audit Log

**Use case**: Remove specific problematic message without banning user

#### View User Profile

Navigate to the user's profile page:
- See all messages from this user
- View ban/warning history
- See spam detection stats
- Add user notes or tags

**Use case**: Investigate a user's history before taking action

#### View Edit History

If the message was edited, view all previous versions:
- Shows original message
- Shows each edit with timestamp
- Shows who edited (always the original sender)
- Shows spam detection for each version

**Use case**: See if user is trying to hide spam by editing

#### Translate Message

If the message is in a foreign language, translate it to English:
- Uses OpenAI translation (requires API key)
- Translation cached for future views
- Automatic translation for non-Latin scripts
- Toggle between original and translation

**Use case**: Understand spam in other languages

[Screenshot: Message actions menu expanded]

---

## Understanding Message Statuses

Messages can have multiple status indicators:

### Normal Message

- No special badges
- White/light background
- Standard display

### Spam Message

- **Red "Spam" badge**
- Confidence score shown (e.g., "Spam: 87%")
- Click to see detection breakdown
- May be deleted and user banned (if auto-ban triggered)

### Deleted Message

- **Gray "Deleted" badge**
- Strikethrough text (sometimes)
- Still visible in Messages page for audit purposes
- Removed from Telegram group

### Edited Message

- **Pencil icon** 📝
- "Edited" indicator
- Click to view edit history
- Each edit re-runs spam detection

### Combined Statuses

Messages can have multiple statuses:
- **Spam + Deleted** - Spam was auto-banned and deleted
- **Spam + Edited** - User edited message after spam detection
- **Edited + Deleted** - Message was edited then deleted

[Screenshot: Messages with various status indicators]

---

## Spam Detection Breakdown

For messages flagged as spam, click to view the detailed detection breakdown.

### What You'll See

**Overall Confidence**: Final aggregated score (0-100)

**Per-Algorithm Results**:
- **Algorithm name** (e.g., Stop Words, CAS, Naive Bayes)
- **Confidence score** for this algorithm (0-100)
- **Why it flagged** - Explanation (e.g., "Matched stop word: 'crypto signals'")
- **Weight** - How much this algorithm contributed to final score

**Action Taken**:
- Auto-Ban (85+)
- Review Queue (70-84)
- Pass (<70)

**OpenAI Veto** (if enabled):
- Shows if GPT-4 reviewed the message
- GPT-4's verdict (Spam / Not Spam)
- Reasoning provided by GPT-4

### Example Breakdown

```
Overall Confidence: 92% → AUTO-BAN

Algorithm Results:
✓ Stop Words (100%) - Matched: "guaranteed profits", "VIP signals"
✓ URL Content (85%) - Blocked domain: bit.ly/scam123
✓ CAS Database (100%) - User in global spammer database
✓ Naive Bayes (78%) - Classified as spam (trained on 250 samples)
○ Invisible Chars (0%) - No suspicious characters detected
○ Similarity (42%) - Moderate similarity to known spam

Action: AUTO-BAN (confidence ≥ 85)
User banned and message deleted automatically.
```

[Screenshot: Spam detection breakdown modal]

---

## Edit History

If a message was edited, you can view all previous versions.

### Viewing Edit History

1. Click the message with the **pencil icon** 📝
2. Click **View Edit History** in the actions menu
3. A modal shows all versions chronologically

### What's Included

For each edit:
- **Timestamp** - When the edit was made
- **Original message** - Text before edit
- **Edited message** - Text after edit
- **Changes highlighted** - Additions/deletions shown
- **Spam detection** - Rerun for each version
- **Translation** (if applicable)

### Why This Matters

Spammers sometimes:
1. Send a legitimate message
2. Wait for it to pass detection
3. Edit it to add spam links

**Edit detection catches this**: The system re-runs spam detection on every edit, so edited spam gets flagged.

[Screenshot: Edit history modal with multiple versions]

---

## Translation

TelegramGroupsAdmin can automatically translate messages in foreign languages.

### Automatic Translation

Messages are automatically translated if:
- Message is ≥10 characters
- Less than 80% Latin script (e.g., Cyrillic, Chinese, Arabic)
- OpenAI API key is configured

### Manual Translation

For any message, you can:
1. Click the message
2. Click **Translate** in actions menu
3. Translation appears below original text

### Toggle Translation

Once translated, a **toggle button** (🌐) appears:
- Click to switch between original and translation
- Useful for verifying translation accuracy

### Translation in Spam Detection

Translations are used by:
- **OpenAI Verification** - GPT-4 analyzes translated text
- **Multi-Language Detection** - Compares multiple translation attempts
- **Your review** - You can understand foreign spam

[Screenshot: Message with translation toggle]

---

## Common Workflows

### Daily Monitoring Routine

1. **Morning check**:
   - Open Messages page
   - Review recent messages from all chats
   - Look for any spam that slipped through

2. **Filter by spam**:
   - Set filter to "Spam Only"
   - Review auto-banned spam
   - Confirm bans were justified

3. **Check deleted messages**:
   - Toggle "Show Deleted"
   - Audit what was removed
   - Look for false positives

### Investigating a User

1. **Find user's messages**:
   - Use **User Search** filter
   - Type username
   - Review all their messages

2. **Check history**:
   - Click **View User Profile**
   - See ban/warning history
   - View spam detection stats

3. **Take action**:
   - Ban if spammer
   - Warn if borderline
   - Add notes for future reference

### Auditing a Specific Time Period

1. **Set date range**:
   - Click **Date Range** filter
   - Select start and end dates
   - Click **Apply**

2. **Review messages**:
   - Scroll through messages from that period
   - Check for patterns
   - Look for spam trends

3. **Export or note**:
   - Take notes for analytics
   - Report trends to team

### Handling False Positives

1. **Identify false positive**:
   - Message flagged as spam but is legitimate
   - Check spam detection breakdown

2. **Mark as ham**:
   - Go to Reports → Moderation Reports
   - Find the detection
   - Click **Mark as Ham**

3. **Unban user** (if auto-banned):
   - Navigate to Users page
   - Find the user
   - Click **Unban**

4. **Adjust configuration**:
   - Review why it was flagged
   - Adjust thresholds or whitelist domains

[Screenshot: User search and filtering workflow]

---

## Performance Tips

### For Large Groups (10,000+ Messages)

- **Use filters** before scrolling - Narrow results first
- **Avoid "All Messages" filter** - Use spam status or date range
- **Limit date ranges** - Don't load months of data at once
- **Close old tabs** - Each Messages tab maintains scroll position

### For Multiple Chats

- **Switch chats frequently** - Chat selection is instant
- **Bookmark chat URLs** - `/messages?chatId=123` for quick access
- **Use browser tabs** - Open multiple chats in separate tabs

### Smooth Scrolling

- **Let scroll finish** - Wait for messages to load before scrolling more
- **Use keyboard** - Page Up/Down for faster navigation
- **Refresh occasionally** - Reload page to clear memory if sluggish

---

## Keyboard Shortcuts

While no dedicated keyboard shortcuts exist yet, you can use browser shortcuts:

- **Ctrl/Cmd + F** - Search for text on current page
- **Page Up/Down** - Scroll through messages quickly
- **Home/End** - Jump to top/bottom of message list
- **Ctrl/Cmd + R** - Refresh messages

---

## Troubleshooting

### Messages not loading

- **Check chat selection** - Ensure a chat is selected in left panel
- **Verify bot status** - Check Dashboard → Chat Health
- **Check filters** - Too restrictive filters may show no results
- **Refresh page** - Browser cache may be stale

### Media not displaying

- **Check permissions** - Bot needs access to download media
- **Verify storage** - Ensure `/data/media` volume is mounted
- **Check file types** - Only certain media types display inline (images, videos, audio)
- **Documents don't preview** - Security precaution, only metadata shown

### Scroll position jumping

- **Known issue** - Sometimes scroll position jumps when new messages load
- **Workaround** - Scroll to a specific message, let it load, then continue
- **Report if frequent** - This should be rare

### Translations not working

- **Check OpenAI API key** - Settings → External Services → OpenAI Integration
- **Verify message length** - Only messages ≥10 characters are translated
- **Check language** - Already-English messages won't translate
- **Manual translate** - Use actions menu to force translation

### Filters not working

- **Clear all filters** - Click Reset to start over
- **Check date ranges** - Ensure dates are in correct order (start < end)
- **Verify user exists** - User search only shows users with messages
- **Reload page** - Filter state may be stale

---

## Related Documentation

- **[Reports Queue](02-reports.md)** - Review borderline spam detections
- **[Spam Detection Guide](03-spam-detection.md)** - Understand detection algorithms
- **[Users Page](https://future-docs/users.md)** - Manage Telegram users
- **[Chat Management](../admin/02-chat-management.md)** - Configure per-chat settings

---

## Advanced Tips

### Message Retention

Messages are retained based on the retention policy:
- Default: **720 hours (30 days)**
- Spam/ham training samples: **Kept forever**
- Configure: Settings → System → General Settings → Message Retention

**Tip**: Extend retention if you need longer audit history.

### Bulk Actions (Future Feature)

Currently, actions are per-message. Future versions may include:
- Select multiple messages
- Bulk delete
- Bulk ban users

**Workaround**: Use Reports page for bulk spam review.

### Export Messages (Future Feature)

Not currently available, but planned:
- Export messages to CSV
- Export spam detections
- Export for analysis

**Workaround**: Use Analytics page for aggregate stats.

---

**Master the review queue next**: Continue to **[Reports Queue](02-reports.md)**!
