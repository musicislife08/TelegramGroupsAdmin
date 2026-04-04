# Username History Tracking

## Problem

When a Telegram user changes their username, first name, or last name, the `telegram_users` table is overwritten on the next message via `UpsertAsync`. The old values are lost permanently. Admins who remember a user by a previous name have no way to find them, and there is no audit trail of profile changes.

This was surfaced by a production bug where `ProfileDiffDetected` was firing but the scheduled `ProfileScanJob` threw an `ArgumentException`, masking the fact that profile changes were being detected but not recorded anywhere.

## Scope

- Track **username, first name, last name** changes only (Bot API fields compared by `ProfileDiffDetected`)
- Bio, channel, stories, and other User API fields are out of scope (already tracked via `profile_scan_results`)
- Primary goal: **admin visibility and lookup** — not moderation scoring

## Data Model

### New table: `username_history`

| Column | Type | Notes |
|--------|------|-------|
| `id` | `bigint` PK | Auto-generated |
| `user_id` | `bigint` FK | References `telegram_users(telegram_user_id)`, CASCADE DELETE |
| `username` | `varchar(32)` nullable | Previous username at time of change |
| `first_name` | `varchar(64)` nullable | Previous first name at time of change |
| `last_name` | `varchar(64)` nullable | Previous last name at time of change |
| `recorded_at` | `timestamptz` | When the change was detected |

Each row captures the **old** values at the moment of change. Current values remain on `telegram_users`.

### Indexes

- `IX_username_history_user_id` on `user_id` — load history for user detail dialog
- `IX_username_history_username_lower` on `LOWER(username)` — global search by past username
- `IX_username_history_name_lower` on `LOWER(first_name), LOWER(last_name)` — global search by past name

### Retention

No separate retention policy. Rows are deleted via CASCADE when the parent `telegram_users` row is deleted. Name history is lightweight and its value increases over time.

### New `UserActionType` enum value

Add `ProfileChange = 10` to `UserActionType`. When a profile diff is detected, insert a `user_actions` row:

- **Actor:** `system_identifier = "ProfileDiffDetection"`
- **Target:** `user_id` of the user whose profile changed
- **Reason:** Summary of changed fields, e.g. `"Username: @old_name → @new_name, Name: Joe Koops → Joseph K"`
- **chat_id / message_id:** From the message that triggered detection (provides context for where the change was first seen)

## Capture Logic

Single insertion point: `MessageProcessingService.HandleNewMessageAsync`, inside the existing `ProfileDiffDetected` block.

Sequence after this change:

1. `ProfileDiffDetected` returns true (username, first name, or last name differs)
2. Log the change at Info level with old/new values (already implemented in hotfix)
3. **Insert row into `username_history`** with `existingUser`'s old values
4. **Insert `ProfileChange` action into `user_actions`** with change summary
5. Schedule profile scan via Quartz (already implemented)
6. `UpsertAsync` overwrites `telegram_users` with new values

No other code paths update user profile fields from Telegram data — `BotMessageService.UpsertAsync` calls are for bot user records, not real user profiles.

## Search Integration

### Repository changes

Extend the search `WHERE` clause in three `TelegramUserRepository` methods:

- `GetPagedUsersAsync` (line 487-494)
- `GetPagedBannedUsersWithDetailsAsync` (line 566-573)
- `GetUserTabCountsAsync` (line 674-681)

Each currently searches `telegram_users` via `ILike` on username, first_name, last_name, and telegram_user_id. Add an `OR EXISTS` subquery:

```csharp
// Existing
query = query.Where(u =>
    (u.Username != null && EF.Functions.ILike(u.Username, $"%{search}%")) ||
    (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{search}%")) ||
    (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{search}%")) ||
    EF.Functions.ILike(u.TelegramUserId.ToString(), $"%{search}%") ||
    // NEW: match past names
    context.Set<UsernameHistoryDto>().Any(h =>
        h.UserId == u.TelegramUserId &&
        ((h.Username != null && EF.Functions.ILike(h.Username, $"%{search}%")) ||
         (h.FirstName != null && EF.Functions.ILike(h.FirstName, $"%{search}%")) ||
         (h.LastName != null && EF.Functions.ILike(h.LastName, $"%{search}%")))
    ));
```

Also update `SearchByNameAsync` (used by ban command autocomplete) with the same pattern.

### Placeholder text

Update the search bar placeholder from `"Search by name, username, or ID..."` to `"Search by name, username, ID, or past names..."`.

## UI Display

### User Detail Dialog

Add a **Name History** section using `MudExpansionPanel`, collapsed by default. Only rendered if the user has history entries.

Content: a `MudSimpleTable` showing past names in reverse chronological order:

| Username | Name | Date |
|----------|------|------|
| @old_username | Joe Koops | Mar 15, 2026 |
| @spammer123 | Joseph K | Feb 28, 2026 |

This is a secondary reference — the primary visibility is through the `ProfileChange` action in the existing action timeline, which renders alongside bans, warns, kicks, etc.

### Audit Page

No changes needed. The `ProfileChange` user action will appear automatically in the existing audit timeline rendering.

## Testing

### Unit Tests

**`MessageProcessingServiceProfileDiffTests.cs`** (UnitTests/Telegram/Services)

Tests for the capture logic in the `ProfileDiffDetected` block:

- Inserts history row when username changes
- Inserts history row when first name changes
- Inserts history row when last name changes
- Inserts history row when multiple fields change simultaneously
- Does not insert history when no diff detected (existing user, same fields)
- Does not insert history for new users (`existingUser == null`)
- Does not insert history for trusted/admin users (skipped by `contentCheckSkipReason`)
- Inserts `ProfileChange` user action with correct actor (`system_identifier = "ProfileDiffDetection"`)
- User action reason contains old and new values for changed fields only
- User action includes chat_id and message_id from triggering message
- Profile scan scheduling failure does not prevent history/action insertion (try/catch isolation)

### Integration Tests

**`UsernameHistoryRepositoryTests.cs`** (IntegrationTests/Repositories)

Tests against a real PostgreSQL database via `MigrationTestHelper`:

- Insert and retrieve history entries for a user
- History ordered by `recorded_at` descending
- Cascade delete: deleting parent `telegram_users` row deletes all history
- Search by past username returns matching user (via repository search query)
- Search by past first/last name returns matching user
- Search does not return false positives (different user's history)
- Multiple history entries for same user returned correctly

**`TelegramUserRepositorySearchTests.cs`** (extend existing or new file)

- `GetPagedUsersAsync` search matches past username from history
- `GetPagedBannedUsersWithDetailsAsync` search matches past username from history
- `GetUserTabCountsAsync` counts include users matched by past names
- `SearchByNameAsync` matches past names (ban command autocomplete)

### Component Tests

**`UserDetailDialogHistoryTests.cs`** (ComponentTests/Components)

bUnit + NSubstitute tests for the name history UI:

- Name history panel not rendered when user has no history entries
- Name history panel rendered collapsed when user has history entries
- Expanding panel shows table with correct columns (username, name, date)
- History entries displayed in reverse chronological order
- Null username displays gracefully (e.g., "(no username)")
- Null first/last name displays gracefully

## Files to Create

| File | Project | Purpose |
|------|---------|---------|
| `UsernameHistoryDto.cs` | Data | EF Core entity |
| `UsernameHistoryRecord.cs` | Telegram/Models | Domain model |
| `UsernameHistoryMappings.cs` | Telegram/Repositories/Mappings | Dto <-> Model |
| `IUsernameHistoryRepository.cs` | Telegram/Repositories | Interface |
| `UsernameHistoryRepository.cs` | Telegram/Repositories | Implementation |
| EF migration | Data/Migrations | Table + indexes |
| `MessageProcessingServiceProfileDiffTests.cs` | UnitTests | Capture logic tests |
| `UsernameHistoryRepositoryTests.cs` | IntegrationTests | DB repository tests |
| `UserDetailDialogHistoryTests.cs` | ComponentTests | Name history UI tests |

## Files to Modify

| File | Change |
|------|--------|
| `AppDbContext.cs` | Add `DbSet<UsernameHistoryDto>`, fluent config |
| `UserActionType.cs` (Data) | Add `ProfileChange = 10` |
| `UserActionType.cs` (Telegram) | Add `ProfileChange = 10` |
| `MessageProcessingService.cs` | Insert history + action in `ProfileDiffDetected` block |
| `TelegramUserRepository.cs` | Extend search queries with history subquery |
| `UserDetailDialog.razor` | Add collapsed name history expansion panel |
| `Users.razor` | Update search placeholder text |
| `ServiceCollectionExtensions.cs` (Telegram) | Register `IUsernameHistoryRepository` |
