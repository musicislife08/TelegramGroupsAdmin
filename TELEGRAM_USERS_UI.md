# Telegram Users UI - Planning Document

## Overview
Comprehensive Telegram user management interface for moderation and community insights.

---

## UX Changes

### Navigation Restructure
**Current:**
- `/users` - Web app admins (infrastructure)
- No Telegram user interface

**Proposed:**
- `/settings#accounts` - Web app admins (moved from `/users`)
- `/users` - Telegram users (NEW - primary moderation interface)

**Rationale:**
- 90% of admin time = Telegram user moderation
- Web user management = infrastructure setup (belongs in settings)
- Cleaner mental model: "Users" = people in Telegram groups

---

## Page Layout

### Top Section: Action Queue
```
┌─ Moderation Queue ────────────────────────┐
│ 🔴 15 Banned Users                        │
│ 🟡 12 Flagged for Review                  │
│ 🟠 8 Users with Warnings                  │
│ 📝 5 Users with Notes                     │
└────────────────────────────────────────────┘

┌─ Most Active (Last 30 Days) ──────────────┐
│ 🥇 @alice - 432 msgs  🥈 @bob - 387 msgs  │
│ 🥉 @charlie - 256 msgs [View All →]       │
└────────────────────────────────────────────┘
```

### Tabs
- **All Users** - Everyone bot has seen (messages/joins/actions)
- **Flagged for Review** - Action queue (reports, borderline spam, notes)
- **Trusted** - Explicitly trusted users
- **Banned** - Currently banned users (quick unban access)

### List View Columns
1. Photo
2. Name/Username
3. **Status Badge** (single combined indicator):
   - 🟢 **Trusted** - Explicitly trusted, bypasses checks
   - 🔵 **Clean** - No issues, normal user
   - 🟡 **Flagged** - Has reports/notes, needs review
   - 🟠 **Warned** - Has warnings
   - 🔴 **Banned** - Banned from chats
4. Chat Count (hover: list of chats)
5. Warning Count
6. Notes Count
7. Last Active
8. **Trust Toggle** (prominent button - most-used action)
9. Actions menu (⋮)

### Detail Modal Tabs
1. **Overview**
   - Photo, name, username, Telegram user ID
   - Status timeline
   - First seen, last seen
   - Linked web account (if mapped via /link)

2. **Chats**
   - List of group memberships
   - Message count per chat
   - Last activity per chat

3. **Moderation**
   - Warning history (from user_actions)
   - Admin notes (timestamped comments)
   - Tags (suspicious, verified, etc.)
   - Detection results history (spam/ham)

4. **Actions**
   - Trust/Untrust
   - Ban from all chats
   - Add warning
   - Add note
   - Add tag
   - View all messages
   - View similar users (photo hash - Phase 4.10)
   - Export user data

---

## Database Schema

### New Tables

#### admin_notes
```sql
-- Phase 4.19 Actor System: Uses exclusive arc pattern (web_user_id/telegram_user_id/system_identifier)
CREATE TABLE admin_notes (
    id BIGSERIAL PRIMARY KEY,
    telegram_user_id BIGINT NOT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE CASCADE,
    note_text TEXT NOT NULL,
    -- Actor columns (Phase 4.19 - completed)
    web_user_id VARCHAR(450) NULL REFERENCES users(id) ON DELETE SET NULL,
    telegram_user_id_actor BIGINT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE SET NULL,
    system_identifier VARCHAR(50) NULL,
    CONSTRAINT admin_notes_actor_check CHECK (
        (web_user_id IS NOT NULL AND telegram_user_id_actor IS NULL AND system_identifier IS NULL) OR
        (web_user_id IS NULL AND telegram_user_id_actor IS NOT NULL AND system_identifier IS NULL) OR
        (web_user_id IS NULL AND telegram_user_id_actor IS NULL AND system_identifier IS NOT NULL)
    ),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    message_id BIGINT NULL, -- if note came from /note command reply
    chat_id BIGINT NULL
);

CREATE INDEX idx_admin_notes_user ON admin_notes(telegram_user_id);
CREATE INDEX idx_admin_notes_created_at ON admin_notes(created_at DESC);
CREATE INDEX idx_admin_notes_web_user ON admin_notes(web_user_id) WHERE web_user_id IS NOT NULL;
CREATE INDEX idx_admin_notes_telegram_user ON admin_notes(telegram_user_id_actor) WHERE telegram_user_id_actor IS NOT NULL;
```

#### tag_definitions
```sql
CREATE TABLE tag_definitions (
    id BIGSERIAL PRIMARY KEY,
    tag_name VARCHAR(50) NOT NULL UNIQUE,  -- Lowercase enforced: "suspicious", "spam-bot"
    color VARCHAR(20) NOT NULL,             -- MudBlazor Color: "Warning", "Error", "Success"
    created_by VARCHAR(255) NOT NULL,       -- Web app user ID
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_tag_definitions_name ON tag_definitions(tag_name);

-- Default tags
INSERT INTO tag_definitions (tag_name, color, created_by) VALUES
('suspicious', 'Warning', 'system'),
('verified', 'Success', 'system'),
('impersonator', 'Error', 'system'),
('spam-bot', 'Error', 'system'),
('quality', 'Primary', 'system');
```

**Design Notes:**
- Lowercase enforced in application layer (validation before insert)
- Simple color string (MudBlazor Color enum names: Primary, Secondary, Success, Error, Warning, Info, Dark)
- Minimal fields (no description, no soft delete complexity)
- FK relationship from user_tags ensures tag consistency

#### user_tags
```sql
-- Phase 4.19 Actor System: Uses exclusive arc pattern (web_user_id/telegram_user_id/system_identifier)
CREATE TABLE user_tags (
    id BIGSERIAL PRIMARY KEY,
    telegram_user_id BIGINT NOT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE CASCADE,
    tag_id BIGINT NOT NULL REFERENCES tag_definitions(id) ON DELETE RESTRICT,
    -- Actor columns (Phase 4.19 - completed)
    web_user_id VARCHAR(450) NULL REFERENCES users(id) ON DELETE SET NULL,
    telegram_user_id_actor BIGINT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE SET NULL,
    system_identifier VARCHAR(50) NULL,
    CONSTRAINT user_tags_actor_check CHECK (
        (web_user_id IS NOT NULL AND telegram_user_id_actor IS NULL AND system_identifier IS NULL) OR
        (web_user_id IS NULL AND telegram_user_id_actor IS NOT NULL AND system_identifier IS NULL) OR
        (web_user_id IS NULL AND telegram_user_id_actor IS NULL AND system_identifier IS NOT NULL)
    ),
    added_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    removed_at TIMESTAMPTZ NULL,
    -- Removal actor (when tag is removed)
    removed_by_web_user_id VARCHAR(450) NULL REFERENCES users(id) ON DELETE SET NULL,
    removed_by_telegram_user_id BIGINT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE SET NULL,
    removed_by_system_identifier VARCHAR(50) NULL
);

CREATE INDEX idx_user_tags_user ON user_tags(telegram_user_id);
CREATE INDEX idx_user_tags_active ON user_tags(telegram_user_id) WHERE removed_at IS NULL;
CREATE INDEX idx_user_tags_tag_id ON user_tags(tag_id) WHERE removed_at IS NULL;
CREATE UNIQUE INDEX idx_user_tags_user_tag_unique ON user_tags(telegram_user_id, tag_id) WHERE removed_at IS NULL;
CREATE INDEX idx_user_tags_web_user ON user_tags(web_user_id) WHERE web_user_id IS NOT NULL;
CREATE INDEX idx_user_tags_telegram_user ON user_tags(telegram_user_id_actor) WHERE telegram_user_id_actor IS NOT NULL;
```

**Design Notes:**
- FK to tag_definitions (no orphaned tags, ensures color consistency)
- Unique constraint prevents duplicate tags per user
- Soft delete with removed_at (preserves history)
- ON DELETE RESTRICT prevents deleting tags that are in use
- No existing data to migrate (user_tags table is currently empty)
- **Phase 4.19 Complete:** Actor system infrastructure ready for use

### Existing Tables (Used)
- `telegram_users` - Base user data
- `messages` - For chat memberships, activity
- `user_actions` - Warnings, bans, trusts
- `detection_results` - Spam/ham history
- `managed_chats` - Chat names

---

## Key Features

### Status Badge Logic
**🟢 Trusted:**
- `telegram_users.is_trusted = true`

**🔴 Banned:**
- Active ban in `user_actions` (action_type = Ban, expires_at IS NULL OR > NOW())

**🟠 Warned:**
- Active warnings in `user_actions` (action_type = Warn)

**🟡 Flagged:**
- Has admin notes
- Has tags (especially "suspicious")
- Has borderline spam detections (net_confidence 20-50, not banned)
- Has reports (Phase 4.14 - when implemented)

**🔵 Clean:**
- None of the above

### Trust Toggle
**Most-used action** - prominent button in list view:
- Current state shown (✅ Trusted / ⭕ Not Trusted)
- One-click toggle
- Updates `telegram_users.is_trusted`
- Audit logged

### Top Users Calculation
```sql
SELECT
  tu.telegram_user_id,
  tu.username,
  tu.first_name,
  COUNT(m.message_id) as message_count
FROM telegram_users tu
JOIN messages m ON m.user_id = tu.telegram_user_id
WHERE m.timestamp >= NOW() - INTERVAL '30 days'
GROUP BY tu.telegram_user_id, tu.username, tu.first_name
ORDER BY message_count DESC
LIMIT 3;
```

### Flagged for Review Query
```sql
-- Users needing attention
SELECT DISTINCT tu.*
FROM telegram_users tu
LEFT JOIN admin_notes an ON an.telegram_user_id = tu.telegram_user_id
LEFT JOIN user_tags ut ON ut.telegram_user_id = tu.telegram_user_id AND ut.removed_at IS NULL
LEFT JOIN tag_definitions td ON td.id = ut.tag_id
LEFT JOIN detection_results dr ON dr.user_id = tu.telegram_user_id
LEFT JOIN user_actions ua ON ua.user_id = tu.telegram_user_id
WHERE
  -- Has notes
  an.id IS NOT NULL
  -- Has tags (especially suspicious)
  OR (td.tag_name IN ('suspicious', 'spam-bot'))
  -- Has borderline spam (not auto-banned)
  OR (dr.net_confidence BETWEEN 20 AND 50 AND NOT EXISTS (
    SELECT 1 FROM user_actions ban
    WHERE ban.user_id = tu.telegram_user_id
    AND ban.action_type = 0 -- Ban
    AND (ban.expires_at IS NULL OR ban.expires_at > NOW())
  ))
  -- Has active warnings
  OR (ua.action_type = 1 AND (ua.expires_at IS NULL OR ua.expires_at > NOW()));
```

### Banned Users Query
```sql
-- Users with active bans
SELECT
  tu.*,
  ua.issued_at as ban_date,
  ua.issued_by as banned_by,
  ua.reason as ban_reason,
  ua.expires_at as ban_expires,
  ua.message_id as trigger_message_id
FROM telegram_users tu
INNER JOIN user_actions ua ON ua.user_id = tu.telegram_user_id
WHERE ua.action_type = 0  -- Ban
  AND (ua.expires_at IS NULL OR ua.expires_at > NOW())
ORDER BY ua.issued_at DESC;
```

---

## Implementation Phases

### Phase 1: Core Infrastructure ✅ COMPLETE
- [x] Move current Users.razor to Settings.razor#accounts tab
- [x] Create new Users.razor (Telegram users)
- [x] Update NavMenu.razor routing
- [x] Create TelegramUserRepository base queries (GetAll, GetById, GetTrusted)
- [x] Create TelegramUserManagementService stub (basic orchestration)
- [x] Create TelegramUserDetail model (basic fields)

### Phase 2: Basic User List ✅ COMPLETE
- [x] ~~Top stats section (moderation queue counts: banned, flagged, warned, notes)~~ NOT NEEDED
- [x] ~~Top stats section (top users - last 30 days query)~~ NOT NEEDED
- [x] ~~Tab navigation component (All/Flagged/Trusted/Banned)~~ NOT NEEDED (single list works fine)
- [x] User table component (MudTable with basic columns)
- [x] Status badge component (🟢🔵🟡🟠🔴 logic)
- [x] Trust toggle button (prominent action)
- [x] Search functionality (username, name)
- [x] ~~Filtering logic per tab (4 different queries)~~ NOT NEEDED
- [x] Pagination basics (if needed)

### Phase 3: User Detail View ✅ COMPLETE
- [x] TelegramUserDetailDialog.razor (modal shell)
- [x] Overview tab (user info, status timeline, first/last seen)
- [x] Chats tab (membership list from messages table, activity per chat)
- [x] Moderation tab shell (warnings display from user_actions)
- [x] Actions menu in list view (view details, view messages)
- [x] Actions in modal (ban, trust, warn buttons)
- [x] Chat count tooltip in list view
- [x] Warning count badges
- [x] Action History Timeline with date grouping (Today/Yesterday/Day Name/Month Day)
- [x] Phase 4.19 Actor System integration (proper actor display names)

### Phase 4: Admin Notes & Tags ⏳ MOSTLY COMPLETE (~90%)
**Database:** ✅ COMPLETE
- [x] Create admin_notes table migration (Phase 4.19 Actor system)
- [x] Create user_tags table migration (Phase 4.19 Actor system)
- [x] Run migrations
- [x] ~~Seed 5 default tags~~ NOT NEEDED (using TagType enum)

**Backend:** ✅ COMPLETE
- [x] Create AdminNote model (with Actor)
- [x] Create UserTag model (with TagType enum)
- [x] Create AdminNotesRepository (Add, TogglePin, Delete)
- [x] Create UserTagsRepository (Add, Delete)
- [x] Update TelegramUserManagementService (GetUserDetailAsync includes notes+tags)
- [x] Register repositories in DI

**Settings UI - Tag Management:** 🔲 PENDING
- [ ] /settings#tags tab structure
- [ ] Tag list view (name, color chip, usage count)
- [ ] Create tag dialog (name input + MudSelect color picker)
- [ ] Edit tag functionality (change color only)
- [ ] Delete tag validation (block if in use, show usage count)
- [ ] Lowercase enforcement (client-side validation)

**Users UI - Notes & Tags:** ✅ COMPLETE
- [x] Notes section in UserDetailDialog (list with timestamp, author, text)
- [x] Add note dialog (TextInputDialog with multiline)
- [x] Note display formatting
- [x] Pin/Unpin note functionality
- [x] Delete note with confirmation
- [x] Tags display (MudChip with TagType colors)
- [x] Add tag dialog (TagSelectionDialog with TagType enum)
- [x] Remove tag functionality
- [x] Phase 4.19 Actor attribution (web user email, Telegram username, system)
- [x] Confidence modifier support

### Phase 5: Banned Users Tab ✅ COMPLETE (Integrated into main view)
- [x] Banned users display in main Users.razor
- [x] Ban details in UserDetailDialog action history
- [x] Ban expiry display logic (permanent vs temporary)
- [x] Unban button (calls ModerationActionService)
- [x] Phase 4.19 Actor display (banned by web user/Telegram/system)
- [x] Link to trigger message (if message_id present)

### Phase 6: Polish & Export ✅ COMPLETE
- [x] Loading states (MudProgressLinear, skeleton loaders)
- [x] Error handling (try-catch, MudSnackbar toast notifications)
- [x] Empty states ("No users found", "No notes yet", "No tags yet")
- [x] ~~Pagination refinement~~ NOT NEEDED (user count manageable)
- [x] ~~Export user data functionality~~ NOT NEEDED (can add later if requested)
- [x] Mobile responsive tweaks (MudBlazor responsive by default)
- [x] ~~Performance optimization (debounce search, cache stats queries)~~ NOT NEEDED (performance adequate)
- [x] Accessibility (MudBlazor handles ARIA labels)

**Status: Phase 4.12 is 90% complete - core functionality working, only Settings tag management page pending**

---

## UI Mockups

### Settings > Tags Management
```
┌─ Tag Management ─────────────────────────────────────┐
│                                                       │
│  [ + New Tag ]                       [Search...]     │
│                                                       │
│  ┌──────────────────────────────────────────────────┐│
│  │ 🟠 suspicious                 Used by: 12 users  ││
│  │    [Edit Color] [Delete]                         ││
│  │                                                   ││
│  │ 🟢 verified                   Used by: 47 users  ││
│  │    [Edit Color] [Delete]                         ││
│  │                                                   ││
│  │ 🔴 impersonator               Used by: 3 users   ││
│  │    [Edit Color] [Delete]                         ││
│  │                                                   ││
│  │ 🔴 spam-bot                   Used by: 8 users   ││
│  │    [Edit Color] [Delete]                         ││
│  │                                                   ││
│  │ 🔵 quality                    Used by: 5 users   ││
│  │    [Edit Color] [Delete]                         ││
│  └──────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────┘
```

### Create Tag Dialog
```
┌─ Create New Tag ────────────────────────┐
│                                         │
│  Tag Name:    [suspicious____________]  │
│               Lowercase only            │
│                                         │
│  Color:       [🟠 Warning ▼]            │
│                                         │
│    • 🔴 Error                           │
│    • 🟠 Warning                         │
│    • 🟢 Success                         │
│    • 🔵 Primary                         │
│    • 🟣 Secondary                       │
│    • ⚪ Info                            │
│    • ⚫ Dark                            │
│                                         │
│          [Cancel]  [Create]             │
└─────────────────────────────────────────┘
```

### Users > Detail Modal > Moderation Tab
```
┌─ Moderation ─────────────────────────────────────────┐
│                                                       │
│  Tags                                                 │
│  ┌──────────────────────────────────────────────────┐│
│  │ 🟠 suspicious  🔴 spam-bot    [+ Add Tag]        ││
│  └──────────────────────────────────────────────────┘│
│                                                       │
│  Admin Notes                                          │
│  ┌──────────────────────────────────────────────────┐│
│  │ 2025-01-16 14:32 - admin@example.com             ││
│  │ User sent multiple similar messages across        ││
│  │ different chats. Monitoring for spam patterns.    ││
│  │                                                   ││
│  │ 2025-01-15 09:15 - telegram:@moderator           ││
│  │ Warned user about off-topic content.              ││
│  │                                                   ││
│  │ [+ Add Note]                                      ││
│  └──────────────────────────────────────────────────┘│
│                                                       │
│  Warning History                                      │
│  ┌──────────────────────────────────────────────────┐│
│  │ 2025-01-15 - Warned by @moderator                ││
│  │ Reason: Off-topic content                         ││
│  │                                                   ││
│  │ 2025-01-10 - Warned by system:auto-detect        ││
│  │ Reason: Spam pattern detected (confidence: 75%)   ││
│  └──────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────┘
```

### Users > Banned Tab
```
┌─ Banned Users ───────────────────────────────────────┐
│                                                       │
│  User               Ban Date      Banned By   Expires│
│  ───────────────────────────────────────────────────┐│
│  👤 @spammer123    2025-01-16    🤖 System   🔴 Perm││
│      John Doe      14:32          auto-ban          ││
│      Reason: Spam detected (95% confidence)         ││
│      [Unban] [View Messages] [⋮]                    ││
│                                                      ││
│  👤 @badactor      2025-01-15    Admin      🟠 Temp ││
│      Jane Smith    09:15          @mod1             ││
│      Reason: Repeated warnings ignored               ││
│      [Unban] [View Messages] [⋮]                    ││
│                                                      ││
│  👤 @testuser      2025-01-14    Web User    🔴 Perm││
│      Test User     18:20          admin@ex.com      ││
│      Reason: Manual ban - impersonation attempt     ││
│      [Unban] [View Messages] [⋮]                    ││
│  └──────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────┘
```

---

## Stubbed for Future

### Engagement Categories (Phase 2+)
```csharp
public enum EngagementStyle
{
    Discussant,     // 🗣️ Mostly text, occasional links
    LinkPoster,     // 🔗 High link ratio, low discussion
    MediaPoster,    // 📸 Photos/videos, minimal text
    Lurker,         // 👻 Low message count vs time in group
    Balanced        // ⚖️ Good mix
}

// In TelegramUserDetail model
public EngagementStyle? EngagementStyle { get; set; } // null for MVP
```

**Calculation logic (future):**
- Link-to-discussion ratio
- Media message percentage
- Average message length
- Reply count (discussion indicator)

### Risk Scoring Algorithm (Analytics Phase)
```csharp
public int? RiskScore { get; set; } // 0-100, null for MVP
```

**Algorithm (future):**
- Warnings weight
- Spam detection rate
- Recent activity patterns
- Tag influences
- Account age

### Invite Tracking (Phase 4.21)
```csharp
public string? InvitedBy { get; set; } // null for MVP
```

**Requirements:**
- Bot generates all invite links
- Track which admin created link
- Referral quality metrics

---

## Questions / Decisions

### Resolved
✅ **Status badge** - Single combined badge vs separate trust/risk
   - **Decision:** Single badge (🟢🔵🟡🟠🔴) - simpler, cleaner

✅ **Most important quick action** - What goes in list view?
   - **Decision:** Trust toggle (most frequently used)

✅ **Top users location** - Where to show leaderboard?
   - **Decision:** Small section at top of /users page, duplicate in /analytics#users

✅ **Risk calculation** - When to implement?
   - **Decision:** Stub for now, implement during analytics work

✅ **Engagement categories** - Include in MVP?
   - **Decision:** Stub for later (interesting but complex)

✅ **Banned users tab** - Separate view or filter?
   - **Decision:** Separate tab with ban-specific columns (date, reason, expires, unban button)

✅ **Tag system complexity** - Full metadata (description, system tags, etc.) vs simple?
   - **Decision:** Simple - lowercase name + color only, enforce consistency

✅ **Tag definitions** - Predefined in settings vs ad-hoc creation?
   - **Decision:** Predefined in /settings#tags, dropdown selection in user UI

✅ **Tag storage** - VARCHAR in user_tags vs separate tag_definitions table?
   - **Decision:** Separate tag_definitions table with FK (data integrity, color consistency)

### Open Questions
❓ **Photo hash impersonation** - Proactive check on join (Phase 4.10 dependency)
❓ **Report tracking** - Phase 4.14 dependency for "Flagged for Review" tab
❓ **Bot commands** - Implement `/note` and `/tag` commands? (optional)
❓ **Pagination** - How many users before we need it? (100? 500?)
❓ **Real-time updates** - SignalR for live status changes? (nice-to-have)

---

## Dependencies

### Existing Features
- ✅ telegram_users table
- ✅ user_actions table (warnings, bans)
- ✅ messages table (activity, chat memberships)
- ✅ detection_results table (spam/ham history)
- ✅ managed_chats table (chat names)

### Future Features (Affect This UI)
- Phase 4.10: Anti-Impersonation (photo hash matching)
- Phase 4.12: Admin Notes & Tags (core of this feature)
- Phase 4.14: Report Aggregation (affects "Flagged" tab)
- Phase 5.x: Analytics (engagement metrics, risk scoring)

---

## Technical Notes

### User List Source
**Q:** Can Telegram bots pull complete member list?
**A:** No - bots can only see:
- Users who sent messages
- Users who joined (triggered welcome)
- Users who were banned/warned
- Admins (via getChatAdministrators)

**Implication:** Our `telegram_users` table is the complete list of users the bot knows about. This is actually ideal - lurkers who never message aren't moderation targets.

### Performance Considerations
- **Flagged query** - Complex JOIN, may need optimization
- **Chat memberships** - Derived from messages table (no separate tracking)
- **Top users** - Cache for 30 minutes (not real-time critical)
- **Pagination** - Implement if user count > 100

### MudBlazor Components Used
- MudTable (list view)
- MudDialog (detail modal)
- MudTabs (modal tabs)
- MudChip (status badges)
- MudBadge (counts)
- MudTooltip (chat list hover)
- MudButton (trust toggle, actions)
- MudMenu (actions dropdown)
- MudTextField (search, add note)
- MudSelect (add tag)

---

## Success Metrics

**MVP is successful if:**
- ✅ Admins can see all Telegram users in one place
- ✅ Quick visual triage (status badges, counts)
- ✅ One-click trust toggle (most frequent action)
- ✅ Easy access to user details (modal)
- ✅ Notes and tags work for tracking context
- ✅ "Flagged for Review" tab surfaces action items
- ✅ Top users section provides community insight

**Future enhancements driven by:**
- Which metrics admins actually look at
- Which filters get used most
- Which actions are clicked in detail view
- User feedback on what's missing
