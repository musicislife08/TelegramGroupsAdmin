# Unified Permission Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Telegram bot and web UI share one permission model — a single `PermissionLevel` tier ladder (`Member`/`Admin`/`GlobalAdmin`/`Owner`) plus a chat-scope rule — fixing the audited deviations without a DB migration.

**Architecture:** Extend the existing `PermissionLevel` enum with `Member = -1` (computed, never persisted; same int values for the stored tiers, so web claims/policies are unchanged). Introduce one pure resolver that maps `(web tier, is-chat-admin)` → effective tier, used by `CommandRouter`. Re-type bot command thresholds to the enum, render all labels from the enum's `[Display]` name (deleting three duplicate mappers), fix web magic-int checks, and contain the analytics/dashboard cross-chat leak with interim Admin-tier hiding (proper per-chat scoping deferred to issue #510).

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9, EF Core 10, NUnit, Playwright (E2E).

**Spec:** `docs/superpowers/specs/2026-06-01-unified-permission-model-design.md`

---

## File Structure

**Modify:**
- `TelegramGroupsAdmin.Core/Models/PermissionLevel.cs` — add `Member = -1`.
- `TelegramGroupsAdmin.Core/Models/PermissionLevelExtensions.cs` (Create) — `GetDisplayName()` (single source of label text).
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/PermissionResolver.cs` (Create) — pure resolver.
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs` — resolver wiring, enum gating, label, delete `GetPermissionName`.
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/IBotCommand.cs` — `MinPermissionLevel`/`ExecuteAsync` typed `PermissionLevel`.
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/*.cs` (15 files) — thresholds + signature.
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/HelpCommand.cs` — footer + filter, delete `GetPermissionName`.
- `TelegramGroupsAdmin.Telegram/Repositories/IChatAdminsRepository.cs` + `ChatAdminsRepository.cs` — delete int `GetPermissionLevelAsync`.
- `TelegramGroupsAdmin.Telegram/Constants/ModerationConstants.cs` — delete `AdminPermissionLevel`.
- `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs` — add `PolicyOwnerOnly`.
- `TelegramGroupsAdmin/ServiceCollectionExtensions.cs` — use the constant.
- `TelegramGroupsAdmin/Components/Pages/Audit.razor` — Roles → Policy.
- `TelegramGroupsAdmin/Components/Shared/NotificationPreferencesCard.razor` + `Components/Layout/NavMenu.razor` — magic-int → enum.
- `TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs` + `Components/Pages/Login.razor` — dedup `GetRoleName`.
- `TelegramGroupsAdmin/Components/Pages/Analytics.razor` — hide 3 leaky tabs for Admin.
- `TelegramGroupsAdmin/Components/Pages/Home.razor` — scope/hide dashboard widgets for Admin.
- `TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs` (+ interface) — multi-chat pending-count overload.

**Test:**
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/PermissionResolverTests.cs` (Create).
- `TelegramGroupsAdmin.UnitTests/Core/Models/PermissionLevelTests.cs` (Create).
- `TelegramGroupsAdmin.E2ETests/Tests/Dashboard/DashboardTests.cs` + `Tests/Analytics/AnalyticsTests.cs` — update/add.

---

## Task 1: Add `Member` to `PermissionLevel` + `GetDisplayName()`

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Models/PermissionLevel.cs`
- Create: `TelegramGroupsAdmin.Core/Models/PermissionLevelExtensions.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Models/PermissionLevelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `TelegramGroupsAdmin.UnitTests/Core/Models/PermissionLevelTests.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

[TestFixture]
public class PermissionLevelTests
{
    [Test]
    public void Member_IsMinusOne_AndBelowAdmin()
    {
        Assert.That((int)PermissionLevel.Member, Is.EqualTo(-1));
        Assert.That(PermissionLevel.Member, Is.LessThan(PermissionLevel.Admin));
    }

    [TestCase(PermissionLevel.Member, "Member")]
    [TestCase(PermissionLevel.Admin, "Admin")]
    [TestCase(PermissionLevel.GlobalAdmin, "GlobalAdmin")]
    [TestCase(PermissionLevel.Owner, "Owner")]
    public void GetDisplayName_ReturnsDisplayAttributeName(PermissionLevel level, string expected)
    {
        Assert.That(level.GetDisplayName(), Is.EqualTo(expected));
    }

    [Test]
    public void StoredTiers_KeepExistingIntValues()
    {
        // Web claims/policies depend on these — must not change.
        Assert.That((int)PermissionLevel.Admin, Is.EqualTo(0));
        Assert.That((int)PermissionLevel.GlobalAdmin, Is.EqualTo(1));
        Assert.That((int)PermissionLevel.Owner, Is.EqualTo(2));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PermissionLevelTests"`
Expected: FAIL — `PermissionLevel.Member` does not exist / `GetDisplayName` not defined.

- [ ] **Step 3: Add `Member` to the enum**

In `TelegramGroupsAdmin.Core/Models/PermissionLevel.cs`, add as the first member (keep existing members and their `[Display]` attributes unchanged):

```csharp
    /// <summary>No privileges — regular member. Computed at request time; never persisted.</summary>
    [Display(Name = "Member")]
    Member = -1,
```

- [ ] **Step 4: Create the `GetDisplayName` extension**

Create `TelegramGroupsAdmin.Core/Models/PermissionLevelExtensions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Single source of truth for the human-readable name of a <see cref="PermissionLevel"/>.
/// Replaces the previously duplicated GetPermissionName / GetRoleName mappers.
/// </summary>
public static class PermissionLevelExtensions
{
    public static string GetDisplayName(this PermissionLevel level)
    {
        var member = typeof(PermissionLevel).GetMember(level.ToString()).FirstOrDefault();
        var display = member?.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? level.ToString();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PermissionLevelTests"`
Expected: PASS (4 cases).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/PermissionLevel.cs TelegramGroupsAdmin.Core/Models/PermissionLevelExtensions.cs TelegramGroupsAdmin.UnitTests/Core/Models/PermissionLevelTests.cs
git commit -m "feat(permissions): add Member tier and GetDisplayName to PermissionLevel"
```

---

## Task 2: Pure permission resolver + truth-table tests

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/BotCommands/PermissionResolver.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/PermissionResolverTests.cs`

- [ ] **Step 1: Write the failing test (the canonical truth table)**

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/PermissionResolverTests.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.BotCommands;

[TestFixture]
public class PermissionResolverTests
{
    // webTier, isChatAdminOrCreator -> effective
    [TestCase(null, false, PermissionLevel.Member)]                       // unknown user
    [TestCase(null, true, PermissionLevel.Admin)]                        // native TG admin/creator
    [TestCase(PermissionLevel.Admin, false, PermissionLevel.Member)]     // web Admin in a chat they don't administer
    [TestCase(PermissionLevel.Admin, true, PermissionLevel.Admin)]       // web Admin who is also a chat admin
    [TestCase(PermissionLevel.GlobalAdmin, false, PermissionLevel.GlobalAdmin)] // global, any chat
    [TestCase(PermissionLevel.GlobalAdmin, true, PermissionLevel.GlobalAdmin)]
    [TestCase(PermissionLevel.Owner, false, PermissionLevel.Owner)]      // global, any chat
    [TestCase(PermissionLevel.Owner, true, PermissionLevel.Owner)]
    public void Resolve_MatchesCanonicalModel(PermissionLevel? webTier, bool isChatAdmin, PermissionLevel expected)
    {
        Assert.That(PermissionResolver.Resolve(webTier, isChatAdmin), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: FAIL — `PermissionResolver` not defined.

- [ ] **Step 3: Implement the resolver**

Create `TelegramGroupsAdmin.Telegram/Services/BotCommands/PermissionResolver.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Resolves a user's effective permission tier in a specific chat from the two sources
/// of authority: their stored web tier (if any) and their Telegram admin status in that chat.
///
/// GlobalAdmin/Owner apply globally (any chat). Admin is chat-scoped — it applies only
/// where the user is a Telegram admin/creator. Everyone else is Member.
/// This naturally yields the MAX of the two sources.
/// </summary>
public static class PermissionResolver
{
    public static PermissionLevel Resolve(PermissionLevel? webTier, bool isChatAdminOrCreator)
        => webTier is PermissionLevel.GlobalAdmin or PermissionLevel.Owner
            ? webTier.Value
            : isChatAdminOrCreator
                ? PermissionLevel.Admin
                : PermissionLevel.Member;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~PermissionResolverTests"`
Expected: PASS (8 cases).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/PermissionResolver.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/PermissionResolverTests.cs
git commit -m "feat(permissions): add pure PermissionResolver with canonical truth table"
```

---

## Task 3: Type the bot command contract to `PermissionLevel`

This is a compiler-driven sweep: change the interface, then every command + caller the compiler flags. Public commands → `Member`, moderation commands → `Admin`.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/IBotCommand.cs`
- Modify: all 15 files in `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/`
- Modify: `TelegramGroupsAdmin.Telegram/Constants/ModerationConstants.cs`

- [ ] **Step 1: Change the interface**

In `IBotCommand.cs`, add `using TelegramGroupsAdmin.Core.Models;` and change:

```csharp
    /// <summary>Minimum permission tier required to run this command.</summary>
    PermissionLevel MinPermissionLevel { get; }
```

and the execute signature:

```csharp
    Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Update every command's threshold and signature**

For each command file, add `using TelegramGroupsAdmin.Core.Models;` if missing, change `int userPermissionLevel` → `PermissionLevel userPermission` in `ExecuteAsync` (rename internal references), and set `MinPermissionLevel`:

Public (`=> PermissionLevel.Member`): `HelpCommand`, `StartCommand`, `MyStatusCommand`, `ReportCommand`, `LinkCommand`, `InviteCommand`.

Moderation (`=> PermissionLevel.Admin`): `BanCommand`, `DeleteCommand`, `SpamCommand`, `MuteCommand`, `TempBanCommand`, `TrustCommand`, `UnbanCommand`, `WarnCommand`.

Example (`BanCommand.cs`):

```csharp
    public PermissionLevel MinPermissionLevel => PermissionLevel.Admin; // chat admin or higher
```

Example (`HelpCommand.cs`):

```csharp
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member; // everyone
```

For `MuteCommand` and `TempBanCommand`, replace `ModerationConstants.AdminPermissionLevel` with `PermissionLevel.Admin`.

- [ ] **Step 3: Delete the obsolete constant**

In `TelegramGroupsAdmin.Telegram/Constants/ModerationConstants.cs`, delete:

```csharp
    public const int AdminPermissionLevel = 1;
```

(Confirm no remaining references: `grep -rn "AdminPermissionLevel" --include="*.cs"` returns nothing.)

- [ ] **Step 4: Build to surface every remaining call site**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: errors only in `CommandRouter.cs` (handled in Task 4) and `HelpCommand.cs` body (Task 5). Fix any command-body references to the renamed parameter so only the router/help errors remain.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/IBotCommand.cs TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/ TelegramGroupsAdmin.Telegram/Constants/ModerationConstants.cs
git commit -m "refactor(permissions): type IBotCommand on PermissionLevel; drop AdminPermissionLevel"
```

---

## Task 4: Wire `CommandRouter` to the resolver + delete the int chat-admin method

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IChatAdminsRepository.cs`, `ChatAdminsRepository.cs`

- [ ] **Step 1: Replace `GetPermissionLevelAsync` (lines ~178–217)**

Replace the private resolver and the `GetPermissionName` method with:

```csharp
    private async Task<PermissionLevel> GetPermissionLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        var mappingRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
        var webTier = await mappingRepository.GetPermissionLevelByTelegramIdAsync(telegramId, cancellationToken);

        var chatAdminsRepository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
        var isChatAdmin = await chatAdminsRepository.IsAdminAsync(chatId, telegramId, cancellationToken);

        var effective = PermissionResolver.Resolve(webTier, isChatAdmin);
        _logger.LogDebug(
            "Resolved permission for {TelegramId} in chat {ChatId}: {Tier} (web={WebTier}, chatAdmin={IsChatAdmin})",
            telegramId, chatId, effective, webTier, isChatAdmin);
        return effective;
    }
```

(`GetPermissionName` is deleted — labels now come from `PermissionLevel.GetDisplayName()`.)

- [ ] **Step 2: Update the gating block (lines ~88–136)**

Replace the resolution + gating with the enum-based version (removes the `bypassPermissionCheck`/`Math.Max` special-case):

```csharp
            var permissionLevel = await GetPermissionLevelAsync(message.Chat.Id, message.From.Id, cancellationToken);

            if (permissionLevel < command.MinPermissionLevel)
            {
                _logger.LogWarning(
                    "User {User} attempted /{Command} without sufficient permission (has {UserLevel}, needs {RequiredLevel})",
                    TelegramDisplayName.Format(message.From.FirstName, message.From.LastName, message.From.Username, message.From.Id),
                    commandName, permissionLevel, command.MinPermissionLevel);

                var permissionMessage = command.MinPermissionLevel >= PermissionLevel.Admin
                    ? "❌ This command is only available to group administrators."
                    : "❌ You don't have permission to use this command.";

                return new CommandResult(TelegramMessage.Plain(permissionMessage), true);
            }
```

And the execute call (line ~136) now passes the enum:

```csharp
            var result = await command.ExecuteAsync(message, args, permissionLevel, cancellationToken);
```

Add `using TelegramGroupsAdmin.Core.Models;` to `CommandRouter.cs` if missing.

- [ ] **Step 3: Delete the now-unused int chat-admin method**

In `IChatAdminsRepository.cs` remove:

```csharp
    Task<int> GetPermissionLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);
```

In `ChatAdminsRepository.cs` remove its implementation (the `admin.IsCreator ? 2 : 1` method — this was deviation #2's source).

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: PASS (HelpCommand body still references old label helper — fixed next task; if HelpCommand errors, proceed to Task 5 before building green).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs TelegramGroupsAdmin.Telegram/Repositories/IChatAdminsRepository.cs TelegramGroupsAdmin.Telegram/Repositories/ChatAdminsRepository.cs
git commit -m "refactor(permissions): resolve command authority via PermissionResolver; remove int chat-admin level"
```

---

## Task 5: Fix `HelpCommand` label + filtering; delete its duplicate mapper

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/HelpCommand.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/HelpCommandTests.cs` (create or extend)

- [ ] **Step 1: Write a failing test for the footer label**

Add to (or create) `HelpCommandTests.cs` a test that builds help for a `PermissionLevel.Admin` user and asserts the footer contains `Permission: Admin` (not `GlobalAdmin`). Use the existing command test pattern in that folder for construction. Example assertion:

```csharp
[Test]
public async Task Help_Footer_ShowsAdmin_ForAdminTier()
{
    var result = await _command.ExecuteAsync(_message, [], PermissionLevel.Admin);
    Assert.That(result.Message.Text, Does.Contain("Permission: Admin"));
    Assert.That(result.Message.Text, Does.Not.Contain("Permission: GlobalAdmin"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~HelpCommandTests"`
Expected: FAIL (currently maps Admin-tier int to a wrong label / won't compile against new signature).

- [ ] **Step 3: Update `HelpCommand`**

- Change `ExecuteAsync` to `PermissionLevel userPermission` (done in Task 3).
- Replace the command filter and footer:

```csharp
        var availableCommands = allCommands
            .Where(c => c.MinPermissionLevel <= userPermission)
            .ToList();
        // ...
        builder.LineBreak().Italic($"Permission: {userPermission.GetDisplayName()}");
```

- Delete the private `GetPermissionName` method at the bottom of the file.
- Add `using TelegramGroupsAdmin.Core.Models;` if missing.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~HelpCommandTests"`
Expected: PASS.

- [ ] **Step 5: Build the whole solution + run bot tests**

Run: `dotnet build` then `dotnet test TelegramGroupsAdmin.UnitTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/HelpCommand.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/HelpCommandTests.cs
git commit -m "fix(permissions): help footer labels tier via GetDisplayName; delete duplicate mapper (#507)"
```

---

## Task 6: Web — `PolicyOwnerOnly` constant + `Audit.razor` policy

**Files:**
- Modify: `TelegramGroupsAdmin/Constants/AuthenticationConstants.cs`
- Modify: `TelegramGroupsAdmin/ServiceCollectionExtensions.cs`
- Modify: `TelegramGroupsAdmin/Components/Pages/Audit.razor`

- [ ] **Step 1: Add the constant**

In `AuthenticationConstants.cs`, after `PolicyGlobalAdminOrOwner`:

```csharp
    /// <summary>Authorization policy name for owner-only access (infra/system settings).</summary>
    public const string PolicyOwnerOnly = "OwnerOnly";
```

- [ ] **Step 2: Use the constant in policy registration**

In `ServiceCollectionExtensions.cs:90`, change the literal:

```csharp
                .AddPolicy(AuthenticationConstants.PolicyOwnerOnly, policy =>
                    policy.RequireRole("Owner"));
```

- [ ] **Step 3: Update `Audit.razor` to use the policy**

In `Audit.razor:11`, change:

```razor
@attribute [Authorize(Policy = AuthenticationConstants.PolicyGlobalAdminOrOwner)]
```

Add `@using TelegramGroupsAdmin.Constants` if not already imported (check `_Imports.razor`).

- [ ] **Step 4: Find and update any other `"OwnerOnly"` literal**

Run: `grep -rn '"OwnerOnly"' --include="*.razor" --include="*.cs" TelegramGroupsAdmin/`
Replace each with `AuthenticationConstants.PolicyOwnerOnly` (e.g. `NotificationDebug.razor`).

- [ ] **Step 5: Build**

Run: `dotnet build TelegramGroupsAdmin`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin/Constants/AuthenticationConstants.cs TelegramGroupsAdmin/ServiceCollectionExtensions.cs TelegramGroupsAdmin/Components/Pages/Audit.razor TelegramGroupsAdmin/Components/Pages/NotificationDebug.razor
git commit -m "refactor(auth): add PolicyOwnerOnly constant; Audit page uses policy attribute"
```

---

## Task 7: Web — magic-int checks → enum comparisons

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/NotificationPreferencesCard.razor`
- Modify: `TelegramGroupsAdmin/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Fix `NotificationPreferencesCard.razor:331`**

Change `UserPermissionLevel < 2` to compare the enum. If `UserPermissionLevel` is an `int` parameter, cast it:

```csharp
            if (IsOwnerOnlyEvent(eventType) && (PermissionLevel)UserPermissionLevel < PermissionLevel.Owner)
```

Add `@using TelegramGroupsAdmin.Core.Models` if needed.

- [ ] **Step 2: Fix `NavMenu.razor:55`**

Change:

```csharp
                _isGlobalAdminOrOwner = (PermissionLevel)level >= PermissionLevel.GlobalAdmin;
```

Add `@using TelegramGroupsAdmin.Core.Models`. (The `/analytics` nav link stays visible to all — tab-level hiding handles Admin containment, not the nav link.)

- [ ] **Step 3: Build**

Run: `dotnet build TelegramGroupsAdmin`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/NotificationPreferencesCard.razor TelegramGroupsAdmin/Components/Layout/NavMenu.razor
git commit -m "refactor(auth): replace magic-int permission checks with PermissionLevel comparisons"
```

---

## Task 8: Web — dedup `GetRoleName` to `GetDisplayName`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs`
- Modify: `TelegramGroupsAdmin/Components/Pages/Login.razor`

- [ ] **Step 1: Replace `AuthCookieService.GetRoleName` usage**

At `AuthCookieService.cs:89`, change the role claim to use the display name and delete the private `GetRoleName` method:

```csharp
            new(ClaimTypes.Role, user.PermissionLevel.GetDisplayName()),
```

Add `using TelegramGroupsAdmin.Core.Models;` if missing. (Display names for Admin/GlobalAdmin/Owner are exactly the strings the policies' `RequireRole` expects.)

- [ ] **Step 2: Replace `Login.razor` `GetRoleName`**

At `Login.razor:180`, change the role claim construction to `result.PermissionLevel!.Value.GetDisplayName()` and delete the duplicate private `GetRoleName` (lines ~208–214). Add the `@using` if needed.

- [ ] **Step 3: Build + run auth unit tests**

Run: `dotnet build TelegramGroupsAdmin && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~Auth"`
Expected: PASS (role claims unchanged: "Admin"/"GlobalAdmin"/"Owner").

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Services/Auth/AuthCookieService.cs TelegramGroupsAdmin/Components/Pages/Login.razor
git commit -m "refactor(auth): derive role-name claim from PermissionLevel.GetDisplayName (dedup)"
```

---

## Task 9: Interim — hide the 3 leaky analytics tabs from Admin

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/Analytics.razor`

- [ ] **Step 1: Add the permission context to `Analytics.razor`**

In the `@code` block, add the cascading web user and a flag (mirror how other pages obtain it, e.g. `WebAdminAccounts.razor`):

```csharp
    [CascadingParameter] public WebUserIdentity? WebUser { get; set; }
    private bool CanSeeGlobalAnalytics => WebUser?.IsGlobalAdminOrHigher ?? false;
```

Add `@using TelegramGroupsAdmin.Core.Models` if needed.

- [ ] **Step 2: Conditionally render the leaky tab panels**

Wrap the three leaky `MudTabPanel`s (lines 15–17 Content Detection, 23–26 Performance, 28–31 Welcome Analytics) so they are emitted only for GlobalAdmin+. Leave Message Trends (19–21) unconditional:

```razor
        @if (CanSeeGlobalAnalytics)
        {
            <MudTabPanel Text="Content Detection" Icon="@Icons.Material.Filled.Shield">
                <ContentDetectionAnalytics />
            </MudTabPanel>
        }

        <MudTabPanel Text="Message Trends" Icon="@Icons.Material.Filled.TrendingUp">
            <MessageTrends />
        </MudTabPanel>

        @if (CanSeeGlobalAnalytics)
        {
            <MudTabPanel Text="Performance" Icon="@Icons.Material.Filled.Speed">
                <PerformanceMetrics />
            </MudTabPanel>

            <MudTabPanel Text="Welcome Analytics" Icon="@Icons.Material.Filled.People">
                <WelcomeAnalytics />
            </MudTabPanel>
        }
```

(Because the panels aren't emitted for Admin, their components never render and the unscoped repository methods are never invoked for an Admin — the leak is contained.)

- [ ] **Step 3: Verify tab-index logic still resolves**

If `_activeTabIndex` / `OnTabChanged` assumes 4 panels, confirm Message Trends (now possibly index 0 for Admin) still activates without an out-of-range default. If a fixed default index is used, guard it: default to the Message Trends panel for Admin.

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Components/Pages/Analytics.razor
git commit -m "fix(analytics): hide cross-chat leaky tabs from Admin tier (interim, #510 fixes properly)"
```

---

## Task 10: Multi-chat pending-count overload

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs` (+ its interface)
- Test: `TelegramGroupsAdmin.IntegrationTests/.../ReportsRepositoryTests.cs` (extend)

- [ ] **Step 1: Write the failing integration test**

In the existing `ReportsRepositoryTests`, add a test seeding pending reports across chats A, B, C and asserting the count filtered to `[A, B]` excludes C's. Follow the existing fixture pattern in that file.

```csharp
[Test]
public async Task GetPendingCountAsync_FiltersByAccessibleChats()
{
    // seed pending reports in chatA, chatB, chatC (use existing TestReportBuilder/fixture)
    var count = await _repository.GetPendingCountAsync(new[] { chatA, chatB });
    Assert.That(count, Is.EqualTo(/* pending in A + B */));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetPendingCountAsync_FiltersByAccessibleChats"`
Expected: FAIL — overload not defined.

- [ ] **Step 3: Add the overload**

In `IReportsRepository` add:

```csharp
    Task<int> GetPendingCountAsync(IReadOnlyCollection<long> chatIds, ReportType? type = null, CancellationToken cancellationToken = default);
```

In `ReportsRepository.cs` implement (mirror the existing single-chat method at line 103, but filter with `Contains`):

```csharp
    public async Task<int> GetPendingCountAsync(
        IReadOnlyCollection<long> chatIds,
        ReportType? type = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reports
            .AsNoTracking()
            .Where(r => r.Status == (int)ReportStatus.Pending);

        if (chatIds.Count > 0)
            query = query.Where(r => chatIds.Contains(r.ChatId));

        if (type.HasValue)
            query = query.Where(r => r.Type == (int)type.Value);

        return await query.CountAsync(cancellationToken);
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetPendingCountAsync_FiltersByAccessibleChats"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Repositories/ReportsRepository.cs TelegramGroupsAdmin.IntegrationTests
git commit -m "feat(reports): add accessible-chats overload for pending count"
```

---

## Task 11: Interim — dashboard scope-or-hide for Admin

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Pages/Home.razor`

- [ ] **Step 1: Add permission context + accessible chats**

In the `@code` block add the cascading user, a flag, and (for Admin) the accessible chat IDs loaded via `ManagedChatsRepository.GetUserAccessibleChatsAsync` (inject `IManagedChatsRepository` if not present):

```csharp
    [CascadingParameter] public WebUserIdentity? WebUser { get; set; }
    private bool _canSeeGlobalStats;
    private List<long> _accessibleChatIds = [];
```

In `OnInitializedAsync`/load, before the data tasks:

```csharp
        _canSeeGlobalStats = WebUser?.IsGlobalAdminOrHigher ?? false;
        if (!_canSeeGlobalStats && WebUser is not null)
        {
            var chats = await ManagedChatsRepository.GetUserAccessibleChatsAsync(WebUser.Id, WebUser.PermissionLevel);
            _accessibleChatIds = chats.Select(c => c.Identity.Id).ToList();
        }
```

- [ ] **Step 2: Scope the kept widgets; skip the hidden ones**

Change the data load (around line 243–258) so that for Admin only the scoped calls run:

```csharp
            var pendingReportsTask = _canSeeGlobalStats
                ? ReportsRepository.GetPendingCountAsync()
                : ReportsRepository.GetPendingCountAsync(_accessibleChatIds);

            var tabCountsTask = UserManagementService.GetUserTabCountsAsync(
                chatIds: _canSeeGlobalStats ? null : _accessibleChatIds, searchText: null);

            if (_canSeeGlobalStats)
            {
                var statsTask = MessageStatsService.GetStatsAsync();
                var detectionStatsTask = MessageStatsService.GetDetectionStatsAsync();
                var dailySpamTask = AnalyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);
                var recentActionsTask = UserActionsRepository.GetRecentAsync(MaxRecentActivityItems);
                // await + assign as today
            }
            // always await pendingReportsTask + tabCountsTask
```

(Adapt to the file's existing `Task.WhenAll` shape; the key change is that the four global calls are gated behind `_canSeeGlobalStats`.)

- [ ] **Step 3: Hide the global widgets in markup**

Wrap the Total Messages / Unique Users / Images / Data Range cards, the Spam Today card, and the entire Recent Activity panel in `@if (_canSeeGlobalStats)`. Keep Pending Reports, Active Bans, Trusted Users visible (they come from the scoped pending-count + tab-counts). Ensure the stats `<MudGrid>` (line 34) still renders its remaining cards for Admin so it is non-empty.

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin/Components/Pages/Home.razor
git commit -m "fix(dashboard): scope pending-reports/user-counts for Admin; hide global widgets (interim, #510)"
```

---

## Task 12: E2E — update and add permission tests

**Files:**
- Modify: `TelegramGroupsAdmin.E2ETests/Tests/Dashboard/DashboardTests.cs`
- Modify: `TelegramGroupsAdmin.E2ETests/Tests/Analytics/AnalyticsTests.cs`
- Modify (if a new locator is needed): `TelegramGroupsAdmin.E2ETests/PageObjects/HomePage.cs`, `PageObjects/AnalyticsPage.cs`

- [ ] **Step 1: Update `Dashboard_AccessibleByAdmin`**

Change it to assert the *scoped* view for an Admin rather than full stats:

```csharp
    [Test]
    public async Task Dashboard_AccessibleByAdmin_ShowsScopedCards_HidesGlobalWidgets()
    {
        await LoginAsAdminAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _homePage.GetPendingReportsCountAsync(), Is.Not.Null.And.Not.Empty,
                "Admin should still see the scoped Pending Reports card");
            Assert.That(await _homePage.IsTotalMessagesCardVisibleAsync(), Is.False,
                "Admin should NOT see the global Total Messages card");
            Assert.That(await _homePage.IsActivityFeedVisibleAsync(), Is.False,
                "Admin should NOT see the global Recent Activity feed");
        }
    }
```

Add `IsTotalMessagesCardVisibleAsync()` to `HomePage` if absent (mirror `GetTotalMessagesAsync`'s locator with `.IsVisibleAsync()`).

- [ ] **Step 2: Add the GlobalAdmin dashboard test**

```csharp
    [Test]
    public async Task Dashboard_GlobalAdmin_ShowsAllWidgets()
    {
        await LoginAsGlobalAdminAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();
        Assert.That(await _homePage.IsTotalMessagesCardVisibleAsync(), Is.True);
        Assert.That(await _homePage.IsActivityFeedVisibleAsync(), Is.True);
    }
```

- [ ] **Step 3: Add analytics tab-visibility tests**

In `AnalyticsTests.cs`, add a `GetTabNamesAsync` helper usage (already present per `Analytics_ShowsAllFourTabs`):

```csharp
    [Test]
    public async Task Analytics_Admin_SeesOnlyMessageTrends()
    {
        await LoginAsAdminAsync();
        await _analyticsPage.NavigateAsync();
        var tabs = await _analyticsPage.GetTabNamesAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tabs, Does.Contain("Message Trends"));
            Assert.That(tabs, Does.Not.Contain("Content Detection"));
            Assert.That(tabs, Does.Not.Contain("Performance"));
            Assert.That(tabs, Does.Not.Contain("Welcome Analytics"));
        }
    }

    [Test]
    public async Task Analytics_GlobalAdmin_SeesAllFourTabs()
    {
        await LoginAsGlobalAdminAsync();
        await _analyticsPage.NavigateAsync();
        var tabs = await _analyticsPage.GetTabNamesAsync();
        Assert.That(tabs.Count, Is.EqualTo(4));
    }
```

- [ ] **Step 4: Verify the existing `Analytics_PageLoads_ForAdmin` still passes**

It only asserts the tab container is visible; Message Trends still renders for Admin, so leave it. Run the analytics suite to confirm.

Run: `dotnet test TelegramGroupsAdmin.E2ETests --filter "FullyQualifiedName~AnalyticsTests|FullyQualifiedName~DashboardTests"`
Expected: PASS (updated + new + existing GlobalAdmin/Owner cases).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.E2ETests/
git commit -m "test(e2e): admin dashboard hides global widgets; analytics shows only Message Trends"
```

---

## Task 13: Full verification + `PermissionBoundaryTests` regression check

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: PASS, no warnings about unused `GetPermissionName`/`AdminPermissionLevel`.

- [ ] **Step 2: Run unit + integration suites**

Run: `dotnet test TelegramGroupsAdmin.UnitTests` and `dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: PASS.

- [ ] **Step 3: Run permission-boundary + navigation E2E**

Run: `dotnet test TelegramGroupsAdmin.E2ETests --filter "FullyQualifiedName~PermissionBoundaryTests|FullyQualifiedName~NavigationTests"`
Expected: PASS — the `NavMenu` enum refactor preserved the GlobalAdmin+ threshold, so Admin still sees Reports/Users and not Settings/Audit/Chats.

- [ ] **Step 4: Grep for orphans**

Run: `grep -rn "GetPermissionName\|AdminPermissionLevel\|\"OwnerOnly\"\|< 2\|>= 1" --include="*.cs" --include="*.razor" TelegramGroupsAdmin TelegramGroupsAdmin.Telegram`
Expected: no permission-related hits (only unrelated numeric comparisons, if any).

- [ ] **Step 5: Final commit (if any cleanup)**

```bash
git add -A
git commit -m "chore(permissions): final cleanup and verification for unified model"
```

---

## Notes for the implementer

- **No DB migration in this plan.** If you find yourself writing one, stop — stored `permission_level` values must stay `0/1/2`.
- **`Member` is never persisted.** It only appears as a *resolved* value for unprivileged Telegram users.
- **Default E2E user is `PermissionLevel.Admin`** (`TestUserBuilder`). Tests that don't care about tier inherit Admin; elevate explicitly with `LoginAsGlobalAdminAsync`/`LoginAsOwnerAsync` when a test needs broader access.
- **Per-chat analytics/dashboard scoping is deliberately deferred** to issue #510 (needs DB view migrations). This plan only *hides* those surfaces from Admins.
