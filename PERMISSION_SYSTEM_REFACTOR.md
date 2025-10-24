# Permission System Refactor: Chat-Scoped Access Control

## Overview

This document outlines the changes needed to implement chat-scoped permissions where Admin users only see chats they're Telegram admins in, while GlobalAdmin and Owner retain full access.

## Current vs New Permission Model

### Current Model (All users see all chats)

```csharp
public enum PermissionLevel
{
    ReadOnly = 0,   // Can view but not modify (rarely used)
    Admin = 1,      // Can modify settings (sees all chats)
    Owner = 2       // Full system access (sees all chats)
}
```

**Problem:** All authenticated users see ALL chats regardless of their Telegram membership/admin status.

### New Model (Chat-scoped for Admin role)

```csharp
public enum PermissionLevel
{
    Admin = 0,          // Chat-specific moderation (only sees chats they're Telegram admin in)
    GlobalAdmin = 1,    // All chats moderation (no Telegram account required)
    Owner = 2           // All chats + system settings (unchanged)
}
```

**Benefits:**
- ✅ No database migration needed (enum values stay same, just renamed)
- ✅ Owner stays at top (level 2)
- ✅ Removes rarely-used ReadOnly role
- ✅ Chat-specific moderators only see their chats
- ✅ Uses existing `chat_admins` table (no sync needed)

## Permission Matrix

| Role | Access Scope | Telegram Link Required | Can Moderate | Can Manage Users | Can Change Settings |
|------|-------------|----------------------|--------------|------------------|-------------------|
| **Admin** | Only chats they're Telegram admin in | ✅ Yes | ✅ Yes | ❌ No | ❌ No |
| **GlobalAdmin** | All chats | ❌ No | ✅ Yes | ❌ No | ❌ No |
| **Owner** | All chats | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes |

## Implementation Steps

### Step 1: Update Enum Definition (5 min)

**File:** `/src/TelegramGroupsAdmin.Data/Models/UserRecord.cs` (lines 11-21)

```csharp
public enum PermissionLevel
{
    Admin = 0,          // Renamed from ReadOnly - chat-specific moderation
    GlobalAdmin = 1,    // Renamed from Admin - all chats
    Owner = 2           // Unchanged - full system access
}
```

**Also update comments throughout codebase** - search for "ReadOnly" references.

---

### Step 2: Update Role Name Mapping (5 min)

**File:** `/src/TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs` (around line 150)

Find the `GetRoleName()` method and update:

```csharp
private static string GetRoleName(PermissionLevel level)
{
    return level switch
    {
        PermissionLevel.Admin => "Admin",              // was "ReadOnly"
        PermissionLevel.GlobalAdmin => "GlobalAdmin",  // was "Admin"
        PermissionLevel.Owner => "Owner",
        _ => "Admin"
    };
}
```

**Also update any `[Authorize(Roles = "...")]` attributes:**
- Search codebase for `Roles = "Admin,Owner"`
- Update to `Roles = "GlobalAdmin,Owner"` where appropriate (analytics, audit logs, user management pages)

---

### Step 3: Add Chat Filtering Repository Method (30 min)

**File:** `/src/TelegramGroupsAdmin.Telegram/Repositories/ManagedChatsRepository.cs`

Add new method:

```csharp
public async Task<List<ManagedChatRecord>> GetUserAccessibleChatsAsync(
    string webUserId,
    int permissionLevel,
    CancellationToken cancellationToken = default)
{
    // GlobalAdmin and Owner see everything (no Telegram account check needed)
    if (permissionLevel >= (int)PermissionLevel.GlobalAdmin)
    {
        return await GetAllChatsAsync(cancellationToken);
    }

    // Admin users must be Telegram admins in specific chats
    // Get all Telegram accounts linked to this web user
    var telegramIds = await context.TelegramUserMappings
        .Where(m => m.WebUserId == webUserId)
        .Select(m => m.TelegramUserId)
        .ToListAsync(cancellationToken);

    // If no Telegram account linked, they see nothing
    if (!telegramIds.Any())
    {
        return new List<ManagedChatRecord>();
    }

    // Return only chats where they're an active Telegram admin
    var chats = await context.ManagedChats
        .Where(chat => context.ChatAdmins
            .Any(ca => telegramIds.Contains(ca.TelegramId)
                    && ca.ChatId == chat.ChatId
                    && ca.IsActive == true))
        .OrderBy(c => c.Title)
        .ToListAsync(cancellationToken);

    return chats.Select(c => c.ToModel()).ToList();
}
```

**Update interface:** `/src/TelegramGroupsAdmin.Telegram/Repositories/IManagedChatsRepository.cs`

```csharp
Task<List<ManagedChatRecord>> GetUserAccessibleChatsAsync(
    string webUserId,
    int permissionLevel,
    CancellationToken cancellationToken = default);
```

---

### Step 4: Update Chats Page (15 min)

**File:** `/src/TelegramGroupsAdmin/Components/Pages/Chats.razor` (around line 119-125)

**Current code:**
```csharp
private async Task LoadChatsAsync()
{
    _loading = true;
    try
    {
        var chats = await ManagedChatsRepository.GetAllChatsAsync();
        // ...
    }
}
```

**New code:**
```csharp
private async Task LoadChatsAsync()
{
    _loading = true;
    try
    {
        // Get current user ID and permission level
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var permissionLevel = await AuthHelper.GetCurrentPermissionLevelAsync();

        if (string.IsNullOrEmpty(userId))
        {
            _chats = new List<ManagedChatRecord>();
            return;
        }

        // Use new filtered method
        var chats = await ManagedChatsRepository.GetUserAccessibleChatsAsync(
            userId,
            permissionLevel);

        // Rest of existing code...
        _chats = chats.OrderBy(c => c.Title).ToList();
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Error loading chats");
        Snackbar.Add("Failed to load chats", Severity.Error);
    }
    finally
    {
        _loading = false;
    }
}
```

**Add at top of component if not already present:**
```csharp
[Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
[Inject] private BlazorAuthHelper AuthHelper { get; set; } = default!;
```

---

### Step 5: Update Messages Page (15 min)

**File:** `/src/TelegramGroupsAdmin/Components/Pages/Messages.razor` (around line 439-443)

**Current code:**
```csharp
private async Task LoadChatsAsync()
{
    try
    {
        _managedChats = await ChatsRepository.GetAllAsync();
        ApplyChatFilter();
    }
    // ...
}
```

**New code:**
```csharp
private async Task LoadChatsAsync()
{
    try
    {
        // Get current user ID and permission level
        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var permissionLevel = await AuthHelper.GetCurrentPermissionLevelAsync();

        if (string.IsNullOrEmpty(userId))
        {
            _managedChats = new List<ManagedChatRecord>();
            ApplyChatFilter();
            return;
        }

        // Use new filtered method
        _managedChats = await ChatsRepository.GetUserAccessibleChatsAsync(
            userId,
            permissionLevel);

        ApplyChatFilter();
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Error loading chats");
        Snackbar.Add("Failed to load chats", Severity.Error);
    }
}
```

**Add at top if not present:**
```csharp
[Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
[Inject] private BlazorAuthHelper AuthHelper { get; set; } = default!;
```

**Also add check for empty chat list in UI** (add after line ~180):
```razor
@if (!_managedChats.Any())
{
    <MudAlert Severity="Severity.Info" Class="my-4">
        @if (_userPermissionLevel == 0)
        {
            <text>No chats available. You need to be a Telegram admin in at least one managed chat.
            Make sure your <a href="/profile">Telegram account is linked</a>.</text>
        }
        else
        {
            <text>No managed chats found.</text>
        }
    </MudAlert>
}
```

---

### Step 6: Update User Management UI (10 min)

**File:** `/src/TelegramGroupsAdmin/Components/Pages/Users.razor`

Find the permission level dropdown (search for "PermissionLevel" or "ReadOnly"):

**Update dropdown options:**
```razor
<MudSelect T="PermissionLevel" Label="Permission Level" @bind-Value="_editUser.PermissionLevel">
    <MudSelectItem Value="PermissionLevel.Admin">Admin (chat-specific)</MudSelectItem>
    <MudSelectItem Value="PermissionLevel.GlobalAdmin">Global Admin (all chats)</MudSelectItem>
    <MudSelectItem Value="PermissionLevel.Owner">Owner (full access)</MudSelectItem>
</MudSelect>
```

**Update display in user list table:**
```razor
@switch (user.PermissionLevel)
{
    case PermissionLevel.Admin:
        <MudChip Size="Size.Small" Color="Color.Default">Admin</MudChip>
        break;
    case PermissionLevel.GlobalAdmin:
        <MudChip Size="Size.Small" Color="Color.Primary">Global Admin</MudChip>
        break;
    case PermissionLevel.Owner:
        <MudChip Size="Size.Small" Color="Color.Secondary">Owner</MudChip>
        break;
}
```

---

### Step 7: Update Authorization Attributes (10 min)

**Search for:** `[Authorize(Roles = "Admin,Owner")]` in all `.razor` files

**Pages that should require GlobalAdmin or higher:**
- `/Components/Pages/Users.razor` → `[Authorize(Roles = "GlobalAdmin,Owner")]`
- `/Components/Pages/Analytics.razor` → `[Authorize(Roles = "GlobalAdmin,Owner")]`
- `/Components/Pages/Audit.razor` → `[Authorize(Roles = "GlobalAdmin,Owner")]`
- `/Components/Pages/Reports.razor` → `[Authorize(Roles = "GlobalAdmin,Owner")]`

**Settings page** - needs special consideration:
- `/Components/Pages/Settings.razor` → `[Authorize(Roles = "Owner")]` (only Owner can change infra settings)

---

### Step 8: Update Comments and Documentation (10 min)

**Search and replace throughout codebase:**
- `"ReadOnly"` → `"Admin"` (in comments, XML docs)
- Update `/src/CLAUDE.md` if it mentions permission levels
- Update any README or wiki pages

---

## Testing Checklist

### Preparation
- [ ] Backup database before testing
- [ ] Identify test users at each permission level
- [ ] Ensure test users have Telegram accounts linked (for Admin role testing)

### Admin Role (Level 0) Tests
- [ ] Login as Admin user
- [ ] Verify they only see chats where they're Telegram admin
- [ ] Verify they can take moderation actions in their chats
- [ ] Verify they cannot access `/users` page
- [ ] Verify they cannot access `/analytics` page
- [ ] Verify they cannot access Settings page (or it's read-only)
- [ ] Remove user as Telegram admin from a chat, verify chat disappears from UI
- [ ] Add user as Telegram admin to a chat, verify chat appears in UI

### GlobalAdmin Role (Level 1) Tests
- [ ] Login as GlobalAdmin user (doesn't need Telegram account)
- [ ] Verify they see ALL chats
- [ ] Verify they can take moderation actions in any chat
- [ ] Verify they can access `/analytics`, `/audit`, `/reports`
- [ ] Verify they cannot access `/users` page
- [ ] Verify they cannot modify infrastructure settings

### Owner Role (Level 2) Tests
- [ ] Login as Owner
- [ ] Verify they see ALL chats
- [ ] Verify they can access all pages
- [ ] Verify they can manage users (create, edit, delete)
- [ ] Verify they can change settings

### Edge Cases
- [ ] Admin user with NO Telegram account linked → should see zero chats + helpful message
- [ ] Admin user with multiple Telegram accounts linked → should see chats from ALL accounts
- [ ] Admin user who is admin in zero chats → should see zero chats
- [ ] Chat with no admins in system → GlobalAdmin/Owner should still see it
- [ ] New user registration → defaults to Admin role

### UI/UX
- [ ] User list shows correct role labels (Admin, GlobalAdmin, Owner)
- [ ] Edit user dialog shows correct dropdown options
- [ ] Chat list shows "No chats available" message for Admin with no linked account
- [ ] Message filtering only shows accessible chats in dropdown

---

## Migration Steps for Existing System

### Before Deployment

1. **Document current user levels:**
   ```sql
   SELECT email, permission_level
   FROM users
   WHERE status = 1  -- Active users
   ORDER BY permission_level;
   ```

2. **Plan user role adjustments:**
   - Users who should only see their chats: keep at level 1 → will become "Admin" (need to downgrade manually AFTER deploy)
   - Users who need all chats: keep at level 1 → become "GlobalAdmin" automatically
   - Owners at level 2: unchanged

### After Deployment

1. **Update user levels as needed:**
   ```sql
   -- Downgrade specific users who should be chat-specific
   UPDATE users
   SET permission_level = 0
   WHERE email IN ('user1@example.com', 'user2@example.com');
   ```

2. **Verify Telegram account linkages:**
   ```sql
   -- Check which web users have Telegram accounts linked
   SELECT u.email, u.permission_level, COUNT(tum.telegram_user_id) as linked_accounts
   FROM users u
   LEFT JOIN telegram_user_mappings tum ON u.id = tum.web_user_id
   WHERE u.permission_level = 0  -- New Admin role
   GROUP BY u.email, u.permission_level;
   ```

3. **Notify affected users:**
   - Admins now see only their chats
   - Must have Telegram account linked
   - Instructions for linking if needed

---

## Rollback Plan

If issues arise:

1. **Code rollback:** Revert to previous git commit
2. **No database changes needed** - enum values unchanged
3. **User levels unchanged** - only display labels changed

---

## Future Enhancements

Potential improvements to consider later:

1. **Manual chat access grants** - Allow Owner to grant specific chat access outside Telegram admin status
2. **Per-chat role definitions** - Different moderation permissions per chat
3. **Audit logging** - Track when users gain/lose chat access
4. **UI indicator** - Show which chats are accessible via Telegram admin vs manual grant
5. **Permission caching** - Cache accessible chat lists for performance

---

## Key Files Modified

### Core Logic
- `/src/TelegramGroupsAdmin.Data/Models/UserRecord.cs` - Enum definition
- `/src/TelegramGroupsAdmin/Endpoints/AuthEndpoints.cs` - Role name mapping
- `/src/TelegramGroupsAdmin.Telegram/Repositories/ManagedChatsRepository.cs` - Filtering logic
- `/src/TelegramGroupsAdmin.Telegram/Repositories/IManagedChatsRepository.cs` - Interface

### UI Components
- `/src/TelegramGroupsAdmin/Components/Pages/Chats.razor` - Chat list filtering
- `/src/TelegramGroupsAdmin/Components/Pages/Messages.razor` - Message filtering
- `/src/TelegramGroupsAdmin/Components/Pages/Users.razor` - User management
- Various pages with `[Authorize(Roles = "...")]` attributes

### Documentation
- This file (`PERMISSION_SYSTEM_REFACTOR.md`)
- `/src/CLAUDE.md` - Update if it mentions permissions

---

## Questions/Decisions Needed

- [ ] Should Settings page be Owner-only or GlobalAdmin+Owner?
- [ ] Should we show a banner/tooltip explaining chat scoping to new Admins?
- [ ] Should we add a "Request Access" feature for Admins who want to see additional chats?
- [ ] Should we log when chat access is granted/revoked automatically via Telegram admin changes?

---

## Implementation Time Estimate

- **Code changes:** 90-120 minutes
- **Testing:** 30-45 minutes
- **Deployment + user updates:** 15-30 minutes
- **Total:** ~2-3 hours

---

## Support/Troubleshooting

### "I can't see any chats as an Admin"
1. Check if Telegram account is linked: `/profile` page
2. Check if user is actually an admin in any managed chats:
   ```sql
   SELECT ca.chat_id, mc.title
   FROM chat_admins ca
   JOIN managed_chats mc ON ca.chat_id = mc.chat_id
   JOIN telegram_user_mappings tum ON ca.telegram_id = tum.telegram_user_id
   JOIN users u ON tum.web_user_id = u.id
   WHERE u.email = 'user@example.com' AND ca.is_active = true;
   ```

### "Chat disappeared after user was removed as Telegram admin"
- This is expected behavior
- Option 1: Re-add as Telegram admin
- Option 2: Upgrade user to GlobalAdmin if they need full access

### "GlobalAdmin users can't see a newly added chat"
- Verify chat is in `managed_chats` table
- Check if bot has admin permissions in the chat
- Refresh chat cache if implemented

---

**Document Created:** 2025-10-24
**Author:** System analysis + user requirements
**Status:** Ready for implementation
