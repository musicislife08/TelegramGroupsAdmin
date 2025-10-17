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
CREATE TABLE admin_notes (
    id BIGSERIAL PRIMARY KEY,
    telegram_user_id BIGINT NOT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE CASCADE,
    note_text TEXT NOT NULL,
    created_by VARCHAR(255) NOT NULL, -- web app user ID or telegram:@username
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    message_id BIGINT NULL, -- if note came from /note command reply
    chat_id BIGINT NULL
);

CREATE INDEX idx_admin_notes_user ON admin_notes(telegram_user_id);
CREATE INDEX idx_admin_notes_created_at ON admin_notes(created_at DESC);
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
CREATE TABLE user_tags (
    id BIGSERIAL PRIMARY KEY,
    telegram_user_id BIGINT NOT NULL REFERENCES telegram_users(telegram_user_id) ON DELETE CASCADE,
    tag_id BIGINT NOT NULL REFERENCES tag_definitions(id) ON DELETE RESTRICT,
    added_by VARCHAR(255) NOT NULL,  -- web app user ID or telegram:@username
    added_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    removed_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_user_tags_user ON user_tags(telegram_user_id);
CREATE INDEX idx_user_tags_active ON user_tags(telegram_user_id) WHERE removed_at IS NULL;
CREATE INDEX idx_user_tags_tag_id ON user_tags(tag_id) WHERE removed_at IS NULL;
CREATE UNIQUE INDEX idx_user_tags_user_tag_unique ON user_tags(telegram_user_id, tag_id) WHERE removed_at IS NULL;
```

**Design Notes:**
- FK to tag_definitions (no orphaned tags, ensures color consistency)
- Unique constraint prevents duplicate tags per user
- Soft delete with removed_at (preserves history)
- ON DELETE RESTRICT prevents deleting tags that are in use
- No existing data to migrate (user_tags table is currently empty)

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

### Phase 1: Core Infrastructure (Day 1 - ~4 hours)
- [ ] Move current Users.razor to Settings.razor#accounts tab
- [ ] Create new Users.razor (Telegram users)
- [ ] Update NavMenu.razor routing
- [ ] Create TelegramUserRepository base queries (GetAll, GetById, GetTrusted)
- [ ] Create TelegramUserManagementService stub (basic orchestration)
- [ ] Create TelegramUserDetail model (basic fields)

### Phase 2: Basic User List (Day 2 - ~6 hours)
- [ ] Top stats section (moderation queue counts: banned, flagged, warned, notes)
- [ ] Top stats section (top users - last 30 days query)
- [ ] Tab navigation component (All/Flagged/Trusted/Banned)
- [ ] User table component (MudTable with basic columns)
- [ ] Status badge component (🟢🔵🟡🟠🔴 logic)
- [ ] Trust toggle button (prominent action)
- [ ] Search functionality (username, name)
- [ ] Filtering logic per tab (4 different queries)
- [ ] Pagination basics (if needed)

### Phase 3: User Detail View (Day 3 - ~6 hours)
- [ ] TelegramUserDetailDialog.razor (modal shell)
- [ ] Overview tab (user info, status timeline, first/last seen)
- [ ] Chats tab (membership list from messages table, activity per chat)
- [ ] Moderation tab shell (warnings display from user_actions)
- [ ] Actions menu in list view (view details, view messages)
- [ ] Actions in modal (ban, trust, warn buttons)
- [ ] Chat count tooltip in list view
- [ ] Warning count badges

### Phase 4: Admin Notes & Tags (Day 4 - ~7 hours)
**Database (1 hour):**
- [ ] Create tag_definitions table migration
- [ ] Create admin_notes table migration
- [ ] Create user_tags table migration (with FK to tag_definitions)
- [ ] Run migrations
- [ ] Seed 5 default tags (suspicious, verified, impersonator, spam-bot, quality)

**Backend (2 hours):**
- [ ] Create TagDefinition model
- [ ] Create AdminNote model
- [ ] Create UserTag model (with joined Definition)
- [ ] Create TagDefinitionsRepository
- [ ] Create AdminNotesRepository
- [ ] Create UserTagsRepository
- [ ] Update TelegramUserManagementService (add notes + tags methods)

**Settings UI - Tag Management (2 hours):**
- [ ] /settings#tags tab structure
- [ ] Tag list view (name, color chip, usage count)
- [ ] Create tag dialog (name input + MudSelect color picker)
- [ ] Edit tag functionality (change color only)
- [ ] Delete tag validation (block if in use, show usage count)
- [ ] Lowercase enforcement (client-side validation)

**Users UI - Notes & Tags (2 hours):**
- [ ] Notes section in Moderation tab (list with timestamp, author, text)
- [ ] Add note dialog (MudTextField multiline, save button)
- [ ] Note display formatting
- [ ] Tags display (MudChip with colors from tag_definitions)
- [ ] Add tag dialog (MudSelect dropdown from tag_definitions)
- [ ] Remove tag functionality (soft delete with removed_at)
- [ ] Note count badge in list view
- [ ] Tag chips in list view (max 3 visible, show +N)

### Phase 5: Banned Users Tab (Day 5 - ~2 hours)
- [ ] Banned users query implementation (with ban details JOIN)
- [ ] Banned tab UI (different columns: ban date, banned by, reason, expires)
- [ ] Ban expiry display logic (permanent vs temporary with countdown)
- [ ] Unban button (primary action, calls ModerationActionService)
- [ ] Format "banned by" display (web: vs telegram: vs system: prefixes)
- [ ] Link to trigger message (if message_id present)
- [ ] Update moderation queue stats (add banned count)

### Phase 6: Polish & Export (Day 5 - ~4 hours)
- [ ] Loading states (MudProgressLinear, skeleton loaders)
- [ ] Error handling (try-catch, MudSnackbar toast notifications)
- [ ] Empty states ("No users found", "No notes yet")
- [ ] Pagination refinement (cursor-based if > 100 users)
- [ ] Export user data functionality (CSV with notes/tags)
- [ ] Mobile responsive tweaks (stack columns on small screens)
- [ ] Performance optimization (debounce search, cache stats queries)
- [ ] Accessibility (ARIA labels, keyboard navigation)

**Total: 5 days (~29 hours)**
**Most complex phase: Phase 4 (Admin Notes & Tags) at ~7 hours**

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
│  👤 @badactor      2025-01-15    Admin      🟠 24h  ││
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
