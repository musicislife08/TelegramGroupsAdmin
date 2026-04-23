# Welcome Bypass Follow-up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the multi-agent review findings to the welcome bypass feature before opening the PR to `develop`.

**Architecture:** Collapse `BypassDecision` to 2 values (None/Admin/Trusted), centralize HTML encoding, consolidate duplicate `UserActionType`/`PermissionLevel` enums into `Core`, move announcement validation from the inert mapping layer to `WelcomeService` with proper logging, and bundle hygiene/test fixes. §5.1 of the spec documents a deferred wiring gap to track via a separate GitHub issue before merging.

**Tech Stack:** .NET 10, C# 14, EF Core 10, PostgreSQL 18, Blazor Server, MudBlazor v9, Quartz.NET, NUnit + NSubstitute, bUnit, Testcontainers.PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-04-20-welcome-bypass-followup-design.md`

**Branch:** `feat/trusted-user-bypass-welcome` (already checked out; all commits land here)

---

## Key type contracts (stable across all tasks)

Subagents MUST use these exact names/shapes. Drift = bug.

```csharp
// Core/Models/PermissionLevel.cs (existing — do not modify)
public enum PermissionLevel { Admin = 0, GlobalAdmin = 1, Owner = 2 }

// Core/Models/UserActionType.cs (new, promoted from Data)
public enum UserActionType {
    Ban = 0, Unban = 1, Warn = 2, Kick = 3, Delete = 4,
    Mute = 5, RestorePermissions = 6, Trust = 7, Untrust = 8,
    ProfileScan = 9, WelcomeBypass = 11
}

// Telegram/Services/Welcome/BypassDecision.cs (shrunk)
public enum BypassDecision { None = 0, Admin = 1, Trusted = 2 }

// Core/Utilities/TelegramHtmlEncoder.cs (new)
public static class TelegramHtmlEncoder {
    public static string Encode(string? value);
}

// Telegram/Services/Moderation/Handlers/IAuditHandler.cs (changed signature)
Task LogWelcomeBypassAsync(
    UserIdentity user, ChatIdentity chat, BypassDecision decision,
    string reasonDetail, CancellationToken ct = default);

// Telegram/Repositories/ITelegramUserMappingRepository.cs (retyped return)
Task<PermissionLevel?> GetPermissionLevelByTelegramIdAsync(long telegramId, CancellationToken ct = default);

// Configuration/Models/Welcome/TrustedBypassConfig.cs (two templates)
public class TrustedBypassConfig {
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";
    public const int MinAnnouncementTtlSeconds = 0;
    public const int MaxAnnouncementTemplateLength = 3500;

    public bool Enabled { get; set; } = false;
    public string AnnouncementMessageAdmin { get; set; } = /* default admin */;
    public string AnnouncementMessageTrusted { get; set; } = /* default trusted */;
    public int AnnouncementTtlSeconds { get; set; } = 30;
}
```

---

## Task 1: Add `TelegramHtmlEncoder` helper

**Files:**
- Create: `TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs`
- Create: `TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramHtmlEncoderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramHtmlEncoderTests.cs
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

[TestFixture]
public class TelegramHtmlEncoderTests
{
    [Test]
    public void Encode_Null_ReturnsEmpty()
        => Assert.That(TelegramHtmlEncoder.Encode(null), Is.EqualTo(string.Empty));

    [Test]
    public void Encode_Empty_ReturnsEmpty()
        => Assert.That(TelegramHtmlEncoder.Encode(""), Is.EqualTo(string.Empty));

    [Test]
    public void Encode_PlainText_ReturnsUnchanged()
        => Assert.That(TelegramHtmlEncoder.Encode("Hello"), Is.EqualTo("Hello"));

    [Test]
    public void Encode_HtmlTags_AreEscaped()
        => Assert.That(TelegramHtmlEncoder.Encode("<b>x</b>"), Is.EqualTo("&lt;b&gt;x&lt;/b&gt;"));

    [Test]
    public void Encode_AmpersandAndQuotes_AreEscaped()
        => Assert.That(TelegramHtmlEncoder.Encode("a & \"b\""), Is.EqualTo("a &amp; &quot;b&quot;"));
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~TelegramHtmlEncoderTests`
Expected: Compile error — `TelegramHtmlEncoder` does not exist.

- [ ] **Step 3: Implement the encoder**

```csharp
// TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs
using System.Net;

namespace TelegramGroupsAdmin.Core.Utilities;

public static class TelegramHtmlEncoder
{
    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~TelegramHtmlEncoderTests`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs \
        TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramHtmlEncoderTests.cs
git commit -m "feat(core): add TelegramHtmlEncoder utility for Telegram HTML-mode output"
```

---

## Task 2: Migrate `NotificationHandler.EscapeHtml` to the new encoder

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs`

- [ ] **Step 1: Locate the inline helper**

Run: `grep -n "EscapeHtml" TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs`
Expected output includes a line near 253 with `private static string EscapeHtml(string? text) =>` (the local helper) and multiple call sites like line 130/166/334.

- [ ] **Step 2: Add using + replace call sites and delete the inline helper**

At the top of `NotificationHandler.cs`, add:
```csharp
using TelegramGroupsAdmin.Core.Utilities;
```

Replace every `EscapeHtml(X)` with `TelegramHtmlEncoder.Encode(X)` throughout the file.

Delete the inline method (the `private static string EscapeHtml(string? text) => ...` definition).

- [ ] **Step 3: Run build + existing tests for this file**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~NotificationHandler`
Expected: All pre-existing tests still pass (behavior unchanged).

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs
git commit -m "refactor(notifications): use TelegramHtmlEncoder instead of inline helper"
```

---

## Task 3: Migrate `NotificationRenderer.EscapeHtml` to the new encoder

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs`

- [ ] **Step 1: Locate the inline helper**

Run: `grep -n "EscapeHtml" TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs`
Expected: many call sites and a private static helper definition.

- [ ] **Step 2: Add using + replace call sites and delete the inline helper**

At the top of the file add:
```csharp
using TelegramGroupsAdmin.Core.Utilities;
```

Replace every `EscapeHtml(X)` with `TelegramHtmlEncoder.Encode(X)` throughout the file.

Delete the inline `private static string EscapeHtml(...)` method.

- [ ] **Step 3: Run build + related tests**

Run: `dotnet build TelegramGroupsAdmin`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~NotificationRenderer`
Expected: pre-existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs
git commit -m "refactor(notifications): NotificationRenderer uses TelegramHtmlEncoder"
```

---

## Task 4: Consolidate `UserActionType` into `Core`

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/UserActionType.cs`
- Delete: `TelegramGroupsAdmin.Data/Models/UserActionType.cs`
- Delete: `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`
- Modify: all files with `using TelegramGroupsAdmin.Data.Models;` or `using TelegramGroupsAdmin.Telegram.Models;` that resolve to this enum
- Verify EF Core keeps int column mapping in `TelegramGroupsAdmin.Data/AppDbContext.cs`

- [ ] **Step 1: Create the canonical Core enum**

```csharp
// TelegramGroupsAdmin.Core/Models/UserActionType.cs
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Moderation action types recorded in the user_actions audit table.
/// Integer values are stable — persisted to the database.
/// </summary>
public enum UserActionType
{
    Ban = 0,
    Unban = 1,
    Warn = 2,
    Kick = 3,
    Delete = 4,
    Mute = 5,
    RestorePermissions = 6,
    Trust = 7,
    Untrust = 8,
    ProfileScan = 9,
    WelcomeBypass = 11,
}
```

Note: If the current copies have other values (e.g., `10 = something`), open both `TelegramGroupsAdmin.Data/Models/UserActionType.cs` and `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`, confirm they are byte-identical, then copy the full canonical list here instead of the stub above. The values MUST match exactly (persisted integers).

- [ ] **Step 2: Delete the two duplicates**

```bash
rm TelegramGroupsAdmin.Data/Models/UserActionType.cs
rm TelegramGroupsAdmin.Telegram/Models/UserActionType.cs
```

- [ ] **Step 3: Retarget every reference**

Run to find references:
```bash
grep -rln "TelegramGroupsAdmin\.Data\.Models\.UserActionType\|TelegramGroupsAdmin\.Telegram\.Models\.UserActionType" --include="*.cs" .
grep -rln "using TelegramGroupsAdmin\.Data\.Models;" --include="*.cs" . | xargs grep -l "UserActionType" 2>/dev/null
grep -rln "using TelegramGroupsAdmin\.Telegram\.Models;" --include="*.cs" . | xargs grep -l "UserActionType" 2>/dev/null
```

For each file returned:
- If the file has a fully-qualified `TelegramGroupsAdmin.Data.Models.UserActionType` reference, replace with `TelegramGroupsAdmin.Core.Models.UserActionType` (or remove the qualification and add `using TelegramGroupsAdmin.Core.Models;`).
- If the file imports `using TelegramGroupsAdmin.Data.Models;` or `using TelegramGroupsAdmin.Telegram.Models;` solely for the enum (verify nothing else from that namespace is used), replace the using with `using TelegramGroupsAdmin.Core.Models;`.
- If the file imports those namespaces for other reasons, add `using TelegramGroupsAdmin.Core.Models;` alongside.

- [ ] **Step 4: Verify EF Core column mapping still persists as int**

Open `TelegramGroupsAdmin.Data/AppDbContext.cs`, search for `UserActionType`:
```bash
grep -n "UserActionType" TelegramGroupsAdmin.Data/AppDbContext.cs
```
Expected: an existing `HasConversion<int>()` or similar on the relevant `modelBuilder` entity configuration for `user_actions`. If the conversion exists and references the enum type, update any fully-qualified reference to `TelegramGroupsAdmin.Core.Models.UserActionType`. If no conversion exists, EF Core will default to int-based persistence for a numeric enum, which is what we want — leave as-is.

- [ ] **Step 5: Build solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 errors.

- [ ] **Step 6: Run impacted test projects**

Run: `dotnet test TelegramGroupsAdmin.UnitTests TelegramGroupsAdmin.IntegrationTests -v minimal`
Expected: no new failures. (Pre-existing fails from other tasks, if any, remain acceptable at this stage.)

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/UserActionType.cs \
        TelegramGroupsAdmin.Data/AppDbContext.cs \
        $(git status -s | grep -E '\.cs$' | awk '{print $2}')
git status  # review the staged set
git commit -m "refactor(core): consolidate UserActionType into Core/Models"
```

---

## Task 5: Consolidate `PermissionLevel` + retype repository method

**Files:**
- Delete: `TelegramGroupsAdmin.Data/Models/PermissionLevel.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserMappingRepository.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserMappingRepository.cs`
- Modify: callers of `GetPermissionLevelByTelegramIdAsync` (at minimum `WelcomeBypassResolver.cs`; search for others)
- Modify: `TelegramGroupsAdmin.UnitTests` tests that stub this method

- [ ] **Step 1: Delete the Data-project duplicate**

```bash
rm TelegramGroupsAdmin.Data/Models/PermissionLevel.cs
```

- [ ] **Step 2: Retarget references**

Run:
```bash
grep -rln "TelegramGroupsAdmin\.Data\.Models\.PermissionLevel" --include="*.cs" .
```

Update each match to use `TelegramGroupsAdmin.Core.Models.PermissionLevel`.

- [ ] **Step 3: Retype the repository method**

Open `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserMappingRepository.cs`. Find:
```csharp
Task<int?> GetPermissionLevelByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default);
```
Change to:
```csharp
Task<PermissionLevel?> GetPermissionLevelByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default);
```

Add/keep `using TelegramGroupsAdmin.Core.Models;` at the top of the interface file.

Open the implementation `TelegramUserMappingRepository.cs`. Update the method signature to return `Task<PermissionLevel?>`. Inside the method, cast the int loaded from the DB to the enum:

```csharp
public async Task<PermissionLevel?> GetPermissionLevelByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
{
    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
    var levelInt = await context.TelegramUserMappings
        .AsNoTracking()
        .Where(m => m.TelegramId == telegramId && m.IsActive)
        .Join(context.Users, m => m.UserId, u => u.Id,
              (m, u) => (int?)u.PermissionLevel)
        .FirstOrDefaultAsync(cancellationToken);

    return levelInt is null ? null : (PermissionLevel)levelInt.Value;
}
```

(Adjust the exact EF query shape to match the existing implementation — the change is only the return type and the cast. Data tables still store int.)

- [ ] **Step 4: Update callers**

Run:
```bash
grep -rln "GetPermissionLevelByTelegramIdAsync" --include="*.cs" .
```

For each caller that previously did `if (permissionLevel is (int)PermissionLevel.GlobalAdmin or (int)PermissionLevel.Owner)` or similar, simplify to:
```csharp
if (permissionLevel >= PermissionLevel.GlobalAdmin) { ... }
```

(The current `WelcomeBypassResolver` will be fully rewritten in Task 8, so only fix compile errors here; the full rewrite takes care of semantics.)

Mocked tests that return `int?` need to return `PermissionLevel?` instead:
- Before: `mappingRepo.GetPermissionLevelByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(1);`
- After: `mappingRepo.GetPermissionLevelByTelegramIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(PermissionLevel.GlobalAdmin);`

- [ ] **Step 5: Build solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 errors.

- [ ] **Step 6: Run unit tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests -v minimal`
Expected: no new failures.

- [ ] **Step 7: Commit**

```bash
git add -A
git status
git commit -m "refactor: remove Data/PermissionLevel duplicate; retype repo method"
```

---

## Task 6: Collapse `BypassDecision` to `None` / `Admin` / `Trusted`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs`
- Modify: test files that reference `BypassDecision.ChatAdmin` or `BypassDecision.WebAdmin`

- [ ] **Step 1: Shrink the enum**

Replace the current contents of `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs` with:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Reason a welcome-flow bypass fired for a joining user.
/// </summary>
public enum BypassDecision
{
    /// <summary>No bypass — user proceeds through the normal welcome flow.</summary>
    None = 0,

    /// <summary>
    /// User is admin-identified: either a Telegram chat admin/creator in any tracked chat,
    /// or a linked web admin at GlobalAdmin or Owner permission level.
    /// </summary>
    Admin = 1,

    /// <summary>
    /// User has <c>IsTrusted = true</c> and the per-chat trusted-bypass toggle is enabled.
    /// </summary>
    Trusted = 2,
}
```

- [ ] **Step 2: Find all references that will break**

Run:
```bash
grep -rln "BypassDecision\.ChatAdmin\|BypassDecision\.WebAdmin" --include="*.cs" .
```

- [ ] **Step 3: Update references**

For each file in the grep output, replace `BypassDecision.ChatAdmin` and `BypassDecision.WebAdmin` with `BypassDecision.Admin`. This includes tests, audit-handler reason switches, metrics switches, and WelcomeService. Some files will merge duplicate switch arms — that's expected and will be cleaned up in later tasks (Audit in Task 9, Metrics in Task 11, Service in Task 14).

- [ ] **Step 4: Build solution**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 errors. (Switches with duplicate `Admin` arms will compile as a warning at most; that's OK — later tasks collapse them.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(welcome): collapse BypassDecision to None/Admin/Trusted"
```

---

## Task 7: Expand `TrustedBypassConfig` shape (two templates + constants)

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/TrustedBypassConfigData.cs`
- Modify: `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Configuration/TrustedBypassConfigTests.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs`

- [ ] **Step 1: Update the business model**

Replace `TrustedBypassConfig.cs` with:

```csharp
namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

public class TrustedBypassConfig
{
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";
    public const int MinAnnouncementTtlSeconds = 0;
    // 3500 = 4096 (Telegram wire limit) − ~600 chars headroom for
    // {username}/{chat_name} expansion and worst-case HTML encoding.
    public const int MaxAnnouncementTemplateLength = 3500;
    internal const int DefaultAnnouncementTtlSeconds = 30;
    internal const string DefaultAnnouncementMessageAdmin =
        UsernameVariable + " welcomed automatically — admin.";
    internal const string DefaultAnnouncementMessageTrusted =
        UsernameVariable + " welcomed automatically — trusted from other groups.";

    public bool Enabled { get; set; } = false;
    public string AnnouncementMessageAdmin { get; set; } = DefaultAnnouncementMessageAdmin;
    public string AnnouncementMessageTrusted { get; set; } = DefaultAnnouncementMessageTrusted;
    public int AnnouncementTtlSeconds { get; set; } = DefaultAnnouncementTtlSeconds;
}
```

- [ ] **Step 2: Update the Data DTO to match**

Replace `TrustedBypassConfigData.cs` with (keep its existing XML docs and namespace):

```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data-layer DTO for the trusted-bypass section of the welcome-config
/// JSONB blob. Nullable on <see cref="WelcomeConfigData"/> because rows
/// predating the feature will not have the object.
/// </summary>
public class TrustedBypassConfigData
{
    public bool Enabled { get; set; }
    public string AnnouncementMessageAdmin { get; set; } = string.Empty;
    public string AnnouncementMessageTrusted { get; set; } = string.Empty;
    public int AnnouncementTtlSeconds { get; set; }
}
```

- [ ] **Step 3: Update the mapping roundtrip**

Open `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs`. In the `extension(WelcomeConfigData data)` block (`ToModel`), replace the `TrustedBypass = ...` initializer with:

```csharp
TrustedBypass = data.TrustedBypass is null
    ? new TrustedBypassConfig()
    : new TrustedBypassConfig
    {
        Enabled = data.TrustedBypass.Enabled,
        AnnouncementMessageAdmin = data.TrustedBypass.AnnouncementMessageAdmin ?? string.Empty,
        AnnouncementMessageTrusted = data.TrustedBypass.AnnouncementMessageTrusted ?? string.Empty,
        AnnouncementTtlSeconds = data.TrustedBypass.AnnouncementTtlSeconds,
    }
```

In the `extension(WelcomeConfig model)` block (`ToData`), replace the `TrustedBypass = ...` initializer with:

```csharp
TrustedBypass = new TrustedBypassConfigData
{
    Enabled = model.TrustedBypass.Enabled,
    AnnouncementMessageAdmin = model.TrustedBypass.AnnouncementMessageAdmin,
    AnnouncementMessageTrusted = model.TrustedBypass.AnnouncementMessageTrusted,
    AnnouncementTtlSeconds = model.TrustedBypass.AnnouncementTtlSeconds,
}
```

Remove any leftover `DefaultAnnouncementMessage` fallback logic — no default injection at this layer.

- [ ] **Step 4: Update config and mapping tests**

Open `TrustedBypassConfigTests.cs`. Update assertions referencing the old single `AnnouncementMessage` field to cover both `AnnouncementMessageAdmin` and `AnnouncementMessageTrusted` (defaults, property initializers, and the new `MaxAnnouncementTemplateLength` / `MinAnnouncementTtlSeconds` constants).

Example additions:
```csharp
[Test]
public void Defaults_Enabled_IsFalse()
    => Assert.That(new TrustedBypassConfig().Enabled, Is.False);

[Test]
public void Defaults_AdminTemplate_ReferencesUsernameVariable()
    => Assert.That(new TrustedBypassConfig().AnnouncementMessageAdmin,
        Does.Contain(TrustedBypassConfig.UsernameVariable));

[Test]
public void Defaults_TrustedTemplate_ReferencesUsernameVariable()
    => Assert.That(new TrustedBypassConfig().AnnouncementMessageTrusted,
        Does.Contain(TrustedBypassConfig.UsernameVariable));

[Test]
public void Constants_Match_Spec()
{
    Assert.That(TrustedBypassConfig.MinAnnouncementTtlSeconds, Is.EqualTo(0));
    Assert.That(TrustedBypassConfig.MaxAnnouncementTemplateLength, Is.EqualTo(3500));
    Assert.That(TrustedBypassConfig.UsernameVariable, Is.EqualTo("{username}"));
    Assert.That(TrustedBypassConfig.ChatNameVariable, Is.EqualTo("{chat_name}"));
}
```

Open `WelcomeConfigMappingsTests.cs`. Update the roundtrip test so it seeds both template fields and asserts both come back:

```csharp
[Test]
public void ToModel_PopulatesBothTemplateFields()
{
    var data = new WelcomeConfigData
    {
        MainWelcomeMessage = "hi",
        TrustedBypass = new TrustedBypassConfigData
        {
            Enabled = true,
            AnnouncementMessageAdmin = "admin msg {username}",
            AnnouncementMessageTrusted = "trusted msg {username}",
            AnnouncementTtlSeconds = 45,
        }
    };
    var model = data.ToModel();
    Assert.Multiple(() =>
    {
        Assert.That(model.TrustedBypass.Enabled, Is.True);
        Assert.That(model.TrustedBypass.AnnouncementMessageAdmin, Is.EqualTo("admin msg {username}"));
        Assert.That(model.TrustedBypass.AnnouncementMessageTrusted, Is.EqualTo("trusted msg {username}"));
        Assert.That(model.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(45));
    });
}

[Test]
public void ToModel_NullTrustedBypass_ReturnsDefaults()
{
    var data = new WelcomeConfigData { MainWelcomeMessage = "hi", TrustedBypass = null };
    var model = data.ToModel();
    Assert.That(model.TrustedBypass.Enabled, Is.False);
}
```

Remove any old assertion that references the removed `AnnouncementMessage` single field.

- [ ] **Step 5: Build + test**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TrustedBypassConfig|FullyQualifiedName~WelcomeConfigMappings" -v minimal`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs \
        TelegramGroupsAdmin.Data/Models/Configs/TrustedBypassConfigData.cs \
        TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs \
        TelegramGroupsAdmin.UnitTests/Configuration/TrustedBypassConfigTests.cs \
        TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs
git commit -m "feat(welcome): TrustedBypassConfig gets per-decision announcement templates"
```

---

## Task 8: Rework `WelcomeBypassResolver` (new rules, drop `IBotUserService`)

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeBypassResolverTests.cs`

**Output contract change:** The resolver returns `(BypassDecision, string? reasonDetail)` so `WelcomeService` can pass the human-readable forensic string to `AuditHandler.LogWelcomeBypassAsync`.

- [ ] **Step 1: Define the return record**

At the top of `WelcomeBypassResolver.cs` (in the same namespace), add:

```csharp
public readonly record struct BypassResolution(BypassDecision Decision, string? ReasonDetail)
{
    public static BypassResolution None() => new(BypassDecision.None, null);
}
```

Update the interface `IWelcomeBypassResolver`:

```csharp
// IWelcomeBypassResolver.cs
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

public interface IWelcomeBypassResolver
{
    Task<BypassResolution> ResolveAsync(
        UserIdentity user,
        ChatIdentity chat,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Update failing tests first (TDD)**

In `WelcomeBypassResolverTests.cs`, update existing tests to:
- Use `BypassResolution` as the result type
- Replace `ChatAdmin` → `Admin` expectations and add `ReasonDetail` assertions
- Replace any old `IBotUserService.GetChatMemberAsync` mock setup with `IChatAdminsRepository.GetAdminChatsAsync` mock setup

Example (full rewrite of 3 core cases):

```csharp
[Test]
public async Task ResolveAsync_UserIsChatAdminElsewhere_ReturnsAdmin()
{
    _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(new List<long> { 1001L, 1002L });
    _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
        .Returns((PermissionLevel?)null);

    var result = await _resolver.ResolveAsync(User, Chat, default);

    Assert.That(result.Decision, Is.EqualTo(BypassDecision.Admin));
    Assert.That(result.ReasonDetail, Does.Contain("Telegram chat admin"));
    Assert.That(result.ReasonDetail, Does.Contain("2"));
}

[Test]
public async Task ResolveAsync_UserIsGlobalAdmin_ReturnsAdmin()
{
    _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(new List<long>());
    _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(PermissionLevel.GlobalAdmin);

    var result = await _resolver.ResolveAsync(User, Chat, default);

    Assert.That(result.Decision, Is.EqualTo(BypassDecision.Admin));
    Assert.That(result.ReasonDetail, Does.Contain("web admin"));
    Assert.That(result.ReasonDetail, Does.Contain("GlobalAdmin"));
}

[Test]
public async Task ResolveAsync_UserIsTrusted_ToggleEnabled_ReturnsTrusted()
{
    _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(new List<long>());
    _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
        .Returns((PermissionLevel?)null);
    _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
        .Returns(new WelcomeConfig { TrustedBypass = { Enabled = true } });
    _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

    var result = await _resolver.ResolveAsync(User, Chat, default);

    Assert.That(result.Decision, Is.EqualTo(BypassDecision.Trusted));
    Assert.That(result.ReasonDetail, Does.Contain("Trusted"));
}

[Test]
public async Task ResolveAsync_UserIsTrusted_ToggleOff_ReturnsNone()
{
    _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(new List<long>());
    _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
        .Returns((PermissionLevel?)null);
    _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
        .Returns(new WelcomeConfig { TrustedBypass = { Enabled = false } });
    _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

    var result = await _resolver.ResolveAsync(User, Chat, default);

    Assert.That(result.Decision, Is.EqualTo(BypassDecision.None));
    Assert.That(result.ReasonDetail, Is.Null);
}

[Test]
public async Task ResolveAsync_NothingMatches_ReturnsNone()
{
    _chatAdminsRepo.GetAdminChatsAsync(UserId, Arg.Any<CancellationToken>())
        .Returns(new List<long>());
    _mappingRepo.GetPermissionLevelByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
        .Returns((PermissionLevel?)PermissionLevel.Admin);  // lowest — not enough
    _configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
        .Returns(new WelcomeConfig { TrustedBypass = { Enabled = true } });
    _userRepo.IsTrustedAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

    var result = await _resolver.ResolveAsync(User, Chat, default);

    Assert.That(result.Decision, Is.EqualTo(BypassDecision.None));
    Assert.That(result.ReasonDetail, Is.Null);
}
```

Update the `[SetUp]` to instantiate the needed dependencies (drop `IBotUserService`; add `IChatAdminsRepository` if not already present via the service scope). Use a `StubServiceScopeFactory` pattern matching what already exists — the existing tests will show the shape.

- [ ] **Step 3: Run tests — expect fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeBypassResolver -v minimal`
Expected: compile or assertion failures (new return type, new mock dependency).

- [ ] **Step 4: Implement the new resolver**

Replace the body of `WelcomeBypassResolver.ResolveAsync` with:

```csharp
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration;

// ... inside the class ...

private const string AdminBypassChatAdminFormat =
    "Bypass: {User} is chat admin in {AdminChatCount} tracked chats (joining {Chat})";
private const string AdminBypassWebAdminFormat =
    "Bypass: {User} is linked web admin ({Level}) (joining {Chat})";
private const string TrustedBypassFormat =
    "Bypass: {User} is trusted, per-chat toggle enabled (joining {Chat})";

public async Task<BypassResolution> ResolveAsync(
    UserIdentity user,
    ChatIdentity chat,
    CancellationToken cancellationToken)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
    var mappingRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
    var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
    var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

    var adminChats = await chatAdminsRepo.GetAdminChatsAsync(user.Id, cancellationToken);
    if (adminChats.Count > 0)
    {
        _logger.LogDebug(AdminBypassChatAdminFormat,
            user.ToLogDebug(), adminChats.Count, chat.ToLogDebug());
        return new BypassResolution(
            BypassDecision.Admin,
            $"Telegram chat admin ({adminChats.Count} chats)");
    }

    var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, cancellationToken);
    if (permissionLevel >= PermissionLevel.GlobalAdmin)
    {
        _logger.LogDebug(AdminBypassWebAdminFormat,
            user.ToLogDebug(), permissionLevel, chat.ToLogDebug());
        return new BypassResolution(
            BypassDecision.Admin,
            $"Linked web admin ({permissionLevel})");
    }

    var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id);
    if (config?.TrustedBypass.Enabled == true)
    {
        var isTrusted = await userRepo.IsTrustedAsync(user.Id, cancellationToken);
        if (isTrusted)
        {
            _logger.LogDebug(TrustedBypassFormat, user.ToLogDebug(), chat.ToLogDebug());
            return new BypassResolution(BypassDecision.Trusted, "Trusted user");
        }
    }

    return BypassResolution.None();
}
```

**Note on `GetEffectiveAsync` signature:** the production API is `GetEffectiveAsync<T>(ConfigType configType, long chatId)` — **no CancellationToken parameter**. Mock setups in tests must match:

```csharp
_configService.GetEffectiveAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
    .Returns(new WelcomeConfig { ... });
```

Remove the constructor field `_botUserService` if present (and drop its parameter from the constructor). Remove any `using` no longer referenced. Preserve the existing `[SetUp]` pattern for `IServiceScopeFactory`.

- [ ] **Step 5: Run resolver tests — expect pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeBypassResolver -v minimal`
Expected: all resolver tests pass.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs \
        TelegramGroupsAdmin.Telegram/Services/Welcome/IWelcomeBypassResolver.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeBypassResolverTests.cs
git commit -m "refactor(welcome): resolver checks chat-admin-anywhere via ChatAdminsRepository"
```

---

## Task 9: Update `AuditHandler.LogWelcomeBypassAsync` signature

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/IAuditHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/AuditHandlerTests.cs`

- [ ] **Step 1: Update the interface**

In `IAuditHandler.cs`, replace the existing `LogWelcomeBypassAsync` signature with:

```csharp
Task LogWelcomeBypassAsync(
    UserIdentity user,
    ChatIdentity chat,
    BypassDecision decision,
    string reasonDetail,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Update the implementation**

In `AuditHandler.cs`, replace the `LogWelcomeBypassAsync` method body with:

```csharp
public async Task LogWelcomeBypassAsync(
    UserIdentity user,
    ChatIdentity chat,
    BypassDecision decision,
    string reasonDetail,
    CancellationToken cancellationToken = default)
{
    var record = CreateRecord(
        user.Id,
        UserActionType.WelcomeBypass,
        Actor.WelcomeBypass,
        reasonDetail,
        chatId: chat.Id);
    await _userActionsRepository.InsertAsync(record, cancellationToken);

    _logger.LogDebug(
        "Recorded {ActionType} action for {User} in {Chat} (decision: {Decision}, reason: {Reason})",
        UserActionType.WelcomeBypass, user.ToLogDebug(), chat.ToLogDebug(), decision, reasonDetail);
}
```

Delete the previous `BypassReasonChatAdmin` / `BypassReasonWebAdmin` / `BypassReasonTrusted` / `BypassReasonFallback` private constants (they're now supplied by the resolver via `reasonDetail`). Also delete the `switch` that mapped `BypassDecision` to those reason constants.

- [ ] **Step 3: Update audit handler tests**

Open `AuditHandlerTests.cs`. Existing tests for `LogWelcomeBypassAsync` will break (signature change). Update them so callers pass `reasonDetail` and the test asserts on the record's `Reason` property:

```csharp
[Test]
public async Task LogWelcomeBypassAsync_PersistsReasonDetail_FromCaller()
{
    const string expectedReason = "Telegram chat admin (3 chats)";
    await _handler.LogWelcomeBypassAsync(TestUser, TestChat, BypassDecision.Admin, expectedReason);
    await _userActionsRepository.Received(1).InsertAsync(
        Arg.Is<UserActionRecord>(r =>
            r.ActionType == UserActionType.WelcomeBypass &&
            r.Reason == expectedReason &&
            r.ChatId == TestChat.Id &&
            r.MessageId == null),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task LogWelcomeBypassAsync_TrustedDecision_PersistsReason()
{
    const string expectedReason = "Trusted user";
    await _handler.LogWelcomeBypassAsync(TestUser, TestChat, BypassDecision.Trusted, expectedReason);
    await _userActionsRepository.Received(1).InsertAsync(
        Arg.Is<UserActionRecord>(r => r.Reason == expectedReason),
        Arg.Any<CancellationToken>());
}
```

Delete any tests asserting on the old per-decision reason-string constants — those constants no longer exist.

- [ ] **Step 4: Run tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AuditHandler -v minimal`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/IAuditHandler.cs \
        TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/AuditHandlerTests.cs
git commit -m "refactor(audit): LogWelcomeBypassAsync takes resolver-supplied reasonDetail"
```

---

## Task 10: Fix `AuditHandler.LogRestrictAsync` log line

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs`

- [ ] **Step 1: Replace the `LogRecorded` call with an inline chat-aware log**

In `LogRestrictAsync`, change the last line from:

```csharp
LogRecorded(UserActionType.Mute, user, executor);
```

to:

```csharp
_logger.LogDebug(
    "Recorded {ActionType} action for {User} in {Chat} by {Executor}",
    UserActionType.Mute, user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());
```

`chat.ToLogDebug()` on a null `ChatIdentity?` handles the null case correctly (existing extension method behavior).

- [ ] **Step 2: Build + run audit tests**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~AuditHandler -v minimal`
Expected: all pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs
git commit -m "fix(audit): include chat in LogRestrictAsync debug log"
```

---

## Task 11: Update `WelcomeMetrics` (collapse outcomes + Meter as field + $"" interpolation)

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs`

- [ ] **Step 1: Rewrite the class**

Full file replacement. Keep the class header comment if present; verify the meter name stays `"TelegramGroupsAdmin.Welcome"`.

```csharp
using System.Diagnostics.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.Telegram.Metrics;

public sealed class WelcomeMetrics
{
    private const string OutcomeBypassAdmin = "skipped_bypass_admin";
    private const string OutcomeBypassTrusted = "skipped_bypass_trusted";
    private const string BypassOutcomeNoneUnreachable =
        "RecordBypassOutcome must not be called with BypassDecision.None";

    private readonly Meter _meter = new("TelegramGroupsAdmin.Welcome");

    private readonly Counter<long> _bypassOutcomes;
    private readonly Histogram<double> _bypassDurationMs;

    public WelcomeMetrics()
    {
        _bypassOutcomes = _meter.CreateCounter<long>(
            "tga.welcome.bypass.outcomes_total",
            description: "Count of welcome-flow bypass decisions");
        _bypassDurationMs = _meter.CreateHistogram<double>(
            "tga.welcome.bypass.duration",
            unit: "ms",
            description: "Duration of the bypass resolver + downstream actions");
    }

    public void RecordBypassOutcome(BypassDecision decision, double elapsedMs)
    {
        var outcome = decision switch
        {
            BypassDecision.Admin => OutcomeBypassAdmin,
            BypassDecision.Trusted => OutcomeBypassTrusted,
            BypassDecision.None => throw new InvalidOperationException(BypassOutcomeNoneUnreachable),
            _ => throw new InvalidOperationException($"Unmapped bypass decision: {decision}"),
        };

        _bypassOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _bypassDurationMs.Record(elapsedMs, new KeyValuePair<string, object?>("outcome", outcome));
    }
}
```

- Note: if the existing class has other counters/histograms beyond bypass metrics, preserve them verbatim — only the bypass outcome code changes.
- The old `BypassOutcomeUnmappedFormat` constant is gone; the throw uses `$"..."` interpolation directly.
- `_meter` becomes a field, matching `PipelineMetrics`.

- [ ] **Step 2: Build**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs
git commit -m "refactor(metrics): WelcomeMetrics collapses bypass outcomes; Meter as field"
```

---

## Task 12: `ChatMetrics` + `ReportMetrics` — `Meter` as field

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs`

- [ ] **Step 1: Update `ChatMetrics`**

In `ChatMetrics.cs`, find the line:

```csharp
var meter = new Meter("TelegramGroupsAdmin.Chat");
```

Replace with:

```csharp
private readonly Meter _meter = new("TelegramGroupsAdmin.Chat");
```

(Move it from a constructor local into a field initializer above the constructor.)

Then replace every `meter.Create<Counter|Histogram|ObservableGauge>...` reference inside the constructor with `_meter.Create...`.

- [ ] **Step 2: Repeat for `ReportMetrics`**

Same transformation on `ReportMetrics.cs`. The meter name string in the constructor will be `"TelegramGroupsAdmin.Report"` (or similar) — keep whatever name the current file uses, only change the storage pattern.

- [ ] **Step 3: Build**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Metrics/ChatMetrics.cs \
        TelegramGroupsAdmin.Telegram/Metrics/ReportMetrics.cs
git commit -m "refactor(metrics): ChatMetrics + ReportMetrics store Meter as field"
```

---

## Task 13: `WelcomeService` — use `BackgroundJobNames` constants

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`

- [ ] **Step 1: Locate the literal strings**

Run:
```bash
grep -n '"WelcomeTimeout"\|"DeleteMessage"' TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs
```
Expected output: one line near 552 containing `"WelcomeTimeout"` and one line near 792 containing `"DeleteMessage"`.

- [ ] **Step 2: Replace with constants**

Add near the top of the file (if not already present):
```csharp
using TelegramGroupsAdmin.Core.BackgroundJobs;
```

Replace `"WelcomeTimeout"` → `BackgroundJobNames.WelcomeTimeout`.
Replace `"DeleteMessage"` → `BackgroundJobNames.DeleteMessage`.

- [ ] **Step 3: Confirm the constants exist**

Run:
```bash
grep -n "WelcomeTimeout\|DeleteMessage" TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs
```
Expected: both constants defined as `public const string`.

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs
git commit -m "refactor(welcome): use BackgroundJobNames constants for job identifiers"
```

---

## Task 14: `WelcomeService.PostBypassAnnouncementIfConfiguredAsync` rewrite

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

This task rewrites the announcement helper **and** updates its caller to pass the new `BypassResolution` through.

- [ ] **Step 1: Update the call site (`HandleChatMemberUpdateAsync` bypass block)**

In `WelcomeService.cs`, locate the existing Step 2.5 bypass block (where `bypassResolver.ResolveAsync(...)` is called). The resolver now returns `BypassResolution` (Task 8). Update the block to:

```csharp
var resolution = await bypassResolver.ResolveAsync(user, chat, cancellationToken);
if (resolution.Decision != BypassDecision.None)
{
    var swBypass = Stopwatch.StartNew();

    await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);

    await _auditHandler.LogWelcomeBypassAsync(
        user, chat, resolution.Decision, resolution.ReasonDetail ?? string.Empty, cancellationToken);

    await PostBypassAnnouncementIfConfiguredAsync(
        config, user, chat, resolution.Decision, cancellationToken);

    _welcomeMetrics.RecordBypassOutcome(resolution.Decision, swBypass.Elapsed.TotalMilliseconds);
    return;
}
```

- [ ] **Step 2: Rewrite `PostBypassAnnouncementIfConfiguredAsync`**

Replace the private method body with:

```csharp
private async Task PostBypassAnnouncementIfConfiguredAsync(
    WelcomeConfig config,
    User user,
    Chat chat,
    BypassDecision decision,
    CancellationToken cancellationToken)
{
    if (!config.TrustedBypass.Enabled) return;

    var template = decision switch
    {
        BypassDecision.Admin => config.TrustedBypass.AnnouncementMessageAdmin,
        BypassDecision.Trusted => config.TrustedBypass.AnnouncementMessageTrusted,
        _ => null,
    };

    if (string.IsNullOrWhiteSpace(template)) return;

    if (template.Length > TrustedBypassConfig.MaxAnnouncementTemplateLength)
    {
        _logger.LogWarning(
            "TrustedBypass {Decision} template exceeds {Max} chars (was {Actual}); truncating. Chat: {Chat}",
            decision,
            TrustedBypassConfig.MaxAnnouncementTemplateLength,
            template.Length,
            new ChatIdentity(chat.Id, chat.Title).ToLogInfo());
        template = template[..TrustedBypassConfig.MaxAnnouncementTemplateLength];
    }

    var ttl = Math.Max(
        TrustedBypassConfig.MinAnnouncementTtlSeconds,
        config.TrustedBypass.AnnouncementTtlSeconds);

    // HTML-encode user-controlled substitutions (fixes security finding H1)
    var displayName = TelegramDisplayName.Format(user);
    var mention = !string.IsNullOrWhiteSpace(user.Username)
        ? $"@{TelegramHtmlEncoder.Encode(user.Username)}"
        : $"<a href=\"tg://user?id={user.Id}\">{TelegramHtmlEncoder.Encode(displayName)}</a>";

    var text = template
        .Replace(TrustedBypassConfig.UsernameVariable, mention)
        .Replace(TrustedBypassConfig.ChatNameVariable, TelegramHtmlEncoder.Encode(chat.Title));

    var sent = await _botMessageService.SendAndSaveMessageAsync(
        chat.Id,
        text,
        parseMode: ParseMode.Html,
        cancellationToken: cancellationToken);

    if (sent?.MessageId is not int messageId) return;

    var payload = new DeleteMessagePayload(chat.Id, messageId);
    await _jobScheduler.ScheduleJobAsync(
        BackgroundJobNames.DeleteMessage,
        payload,
        delaySeconds: ttl,
        deduplicationKey: null,
        cancellationToken: cancellationToken);
}
```

Add at the top of the file (if not present):
```csharp
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
```

- [ ] **Step 3: Update/augment WelcomeService tests for the new shape**

Open `WelcomeServiceTests.cs`. For each existing test that exercised a bypass path, update the mock setup on `IWelcomeBypassResolver.ResolveAsync` to return `new BypassResolution(BypassDecision.Admin, "Telegram chat admin (2 chats)")` or `new BypassResolution(BypassDecision.Trusted, "Trusted user")` instead of the old `BypassDecision` value.

Replace the old `announcement sent` assertion with one that verifies `IBotMessageService.SendAndSaveMessageAsync` is called with the decision's template (`config.TrustedBypass.AnnouncementMessageTrusted` for Trusted, etc.).

Hostile-HTML regression tests and clamping tests come in Tasks 18 and 19; they live in this same file.

- [ ] **Step 4: Build + run service tests**

Run: `dotnet build TelegramGroupsAdmin.Telegram`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeService -v minimal`
Expected: pre-existing bypass tests pass with the new shape.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs
git commit -m "feat(welcome): per-decision templates, HTML encoding, consumer-side clamping"
```

---

## Task 15: `WelcomeSystemConfig.razor` updates

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor`
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`

This task bundles all Razor edits: rename, second template field, previews, MaxLength/Counter, migration preserve, delete dead override.

- [ ] **Step 1: Rename the panel**

Change the outer section header (~line 194) from `Trusted User Bypass` to `Auto-admit Trusted Users`.
Change the panel `TitleContent` `MudText` (~line 211) to match: `Auto-admit Trusted Users`.

- [ ] **Step 2: Replace the single announcement field with two + previews**

Inside the panel's `ChildContent`, after the existing toggle/switch block, replace the old `MudTextField` bound to `_config.TrustedBypass.AnnouncementMessage` with two fields + previews. Concrete markup:

```razor
<MudText Typo="Typo.subtitle2" Class="mb-1">Admin Bypass Announcement</MudText>
<MudTextField @bind-Value="_config.TrustedBypass.AnnouncementMessageAdmin"
              Label="Template (admin)"
              Lines="3"
              MaxLength="@TrustedBypassConfig.MaxAnnouncementTemplateLength"
              Counter="@TrustedBypassConfig.MaxAnnouncementTemplateLength"
              Variant="Variant.Filled"
              Disabled="!_config.TrustedBypass.Enabled"
              HelperText="@($"Optional variables: {TrustedBypassConfig.UsernameVariable}, {TrustedBypassConfig.ChatNameVariable}. Rendered message is subject to Telegram's 4096-byte limit.")" />
<TelegramMessagePreview MessageText="@_config.TrustedBypass.AnnouncementMessageAdmin"
                        Username="example_admin"
                        ChatName="Example Chat"
                        Class="mt-2 mb-4" />

<MudText Typo="Typo.subtitle2" Class="mb-1">Trusted User Announcement</MudText>
<MudTextField @bind-Value="_config.TrustedBypass.AnnouncementMessageTrusted"
              Label="Template (trusted)"
              Lines="3"
              MaxLength="@TrustedBypassConfig.MaxAnnouncementTemplateLength"
              Counter="@TrustedBypassConfig.MaxAnnouncementTemplateLength"
              Variant="Variant.Filled"
              Disabled="!_config.TrustedBypass.Enabled"
              HelperText="@($"Optional variables: {TrustedBypassConfig.UsernameVariable}, {TrustedBypassConfig.ChatNameVariable}. Rendered message is subject to Telegram's 4096-byte limit.")" />
<TelegramMessagePreview MessageText="@_config.TrustedBypass.AnnouncementMessageTrusted"
                        Username="example_trusted"
                        ChatName="Example Chat"
                        Class="mt-2 mb-4" />
```

- The parameter names for `TelegramMessagePreview` (`MessageText`, `Username`, `ChatName`) should match the component's existing public API — if the subagent finds different parameter names in `TelegramMessagePreview.razor`, use those. The key intent is a chat-bubble preview with `{username}` and `{chat_name}` substituted to example values.

Add `@using TelegramGroupsAdmin.Configuration.Models.Welcome` at the top of the `.razor` file if not already present (needed to reference `TrustedBypassConfig.MaxAnnouncementTemplateLength` etc.).

- [ ] **Step 3: Preserve `TrustedBypass` + `JoinSecurity` in migration branch**

In `LoadConfig()` method (~lines 554-574), inside the `"Preserve old settings"` block, add:

```csharp
_config.TrustedBypass = config.TrustedBypass;
_config.JoinSecurity = config.JoinSecurity;
```

Keep existing preserved-fields list intact.

- [ ] **Step 4: Delete dead `OnAfterRenderAsync` override**

In the `@code` block (~lines 531-539), delete the entire `protected override Task OnAfterRenderAsync(bool firstRender) { ... return Task.CompletedTask; }` method.

- [ ] **Step 5: Update component tests**

In `WelcomeSystemConfigTests.cs`, find tests referencing `AnnouncementMessage` (the old single-field name). Update the field path and assertions to target `AnnouncementMessageAdmin` / `AnnouncementMessageTrusted`. Add one new test for the migration-preserve behavior:

```csharp
[Test]
public void LoadConfig_MigrationBranch_PreservesTrustedBypassEnabled()
{
    var legacyConfig = new WelcomeConfig
    {
        MainWelcomeMessage = string.Empty, // triggers migration
        TrustedBypass = { Enabled = true, AnnouncementTtlSeconds = 60 },
    };
    _configService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
        .Returns(legacyConfig);

    var cut = Context.Render<WelcomeSystemConfig>();
    // After migration LoadConfig runs in OnInitializedAsync.
    Assert.That(cut.Instance.GetConfigForTest().TrustedBypass.Enabled, Is.True);
    Assert.That(cut.Instance.GetConfigForTest().TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(60));
}
```

If there is no existing test accessor, expose `_config` via an internal method `internal WelcomeConfig? GetConfigForTest() => _config;` on the razor's code-behind partial. If bUnit's existing pattern uses `cut.FindComponent<T>()` or similar to avoid internal accessors, prefer that over adding the accessor. The subagent should look at sibling `WelcomeSystemConfigTests` cases for the existing pattern and match it.

- [ ] **Step 6: Build + run component tests**

Run: `dotnet build TelegramGroupsAdmin`
Expected: 0 errors.

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter FullyQualifiedName~WelcomeSystemConfig -v minimal`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor \
        TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -m "feat(ui): Trusted Bypass panel — rename, two templates, previews, migration preserve"
```

---

## Task 16: Integration test — CK constraint on `user_actions`

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Repositories/UserActionsRepositoryConstraintTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.Infrastructure; // if the project has a fixture/base class — match local convention
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

[TestFixture]
public class UserActionsRepositoryConstraintTests : IntegrationTestBase  // use the same base class as peer repo integration tests
{
    [Test]
    public async Task Insert_ChatScopedAuditRow_WithoutMessageId_Succeeds()
    {
        var repo = GetRequiredService<IUserActionsRepository>();
        var record = new UserActionRecord(
            Id: 0,
            UserId: SeededUserId,
            ActionType: UserActionType.WelcomeBypass,
            MessageId: null,
            ChatId: SeededChatId,
            IssuedBy: Actor.WelcomeBypass,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Reason: "Trusted user");

        Assert.DoesNotThrowAsync(async () =>
            await repo.InsertAsync(record, CancellationToken.None));
    }

    [Test]
    public async Task Insert_OrphanMessageRow_ThrowsConstraintViolation()
    {
        var repo = GetRequiredService<IUserActionsRepository>();
        var record = new UserActionRecord(
            Id: 0,
            UserId: SeededUserId,
            ActionType: UserActionType.Delete,
            MessageId: 123456,
            ChatId: null,  // violates: message_id NOT NULL requires chat_id NOT NULL
            IssuedBy: Actor.Unknown,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Reason: null);

        var ex = Assert.ThrowsAsync<DbUpdateException>(async () =>
            await repo.InsertAsync(record, CancellationToken.None));
        Assert.That(ex!.InnerException?.Message,
            Does.Contain("user_actions").IgnoreCase
                .Or.Contain("check").IgnoreCase
                .Or.Contain("constraint").IgnoreCase);
    }
}
```

Adjust the base class (`IntegrationTestBase`) and seed helpers (`SeededUserId`, `SeededChatId`) to match this repo's existing conventions — the subagent should look at `WelcomeFlowBypassIntegrationTests.cs` for the exact pattern. If seeded data doesn't exist, create a minimal user+chat in `[SetUp]`.

- [ ] **Step 2: Run the test**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~UserActionsRepositoryConstraint -v minimal`
Expected: both tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Repositories/UserActionsRepositoryConstraintTests.cs
git commit -m "test(repo): cover user_actions CK constraint success and orphan-message failure"
```

---

## Task 17: Unit test — `WelcomeMetrics.RecordBypassOutcome(None)` throws

**Files:**
- Create or modify: `TelegramGroupsAdmin.UnitTests/Telegram/Metrics/WelcomeMetricsTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using NUnit.Framework;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Metrics;

[TestFixture]
public class WelcomeMetricsTests
{
    [Test]
    public void RecordBypassOutcome_None_ThrowsInvalidOperationException()
    {
        var metrics = new WelcomeMetrics();
        Assert.Throws<InvalidOperationException>(() =>
            metrics.RecordBypassOutcome(BypassDecision.None, 0.0));
    }

    [Test]
    public void RecordBypassOutcome_Admin_DoesNotThrow()
    {
        var metrics = new WelcomeMetrics();
        Assert.DoesNotThrow(() =>
            metrics.RecordBypassOutcome(BypassDecision.Admin, 12.5));
    }

    [Test]
    public void RecordBypassOutcome_Trusted_DoesNotThrow()
    {
        var metrics = new WelcomeMetrics();
        Assert.DoesNotThrow(() =>
            metrics.RecordBypassOutcome(BypassDecision.Trusted, 12.5));
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeMetrics -v minimal`
Expected: all 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Telegram/Metrics/WelcomeMetricsTests.cs
git commit -m "test(metrics): contract tests for WelcomeMetrics.RecordBypassOutcome"
```

---

## Task 18: Unit tests — template substitution + HTML encoding

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

- [ ] **Step 1: Add the three tests**

Append the following to the existing `WelcomeServiceTests` class (helper setup patterns like `CreateJoinUpdate`, `TestUser`, `TestChat` should already exist — match the existing test-file conventions for mock wiring).

```csharp
[Test]
public async Task PostBypassAnnouncement_Substitutes_Username_AsEncodedMention()
{
    // Arrange: trusted user with username "alice"
    SetupTrustedBypass(
        adminTemplate: TrustedBypassConfig.UsernameVariable + " arrived",
        trustedTemplate: TrustedBypassConfig.UsernameVariable + " arrived");
    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    await _welcomeService.HandleChatMemberUpdateAsync(
        CreateJoinUpdate(userId: 1, username: "alice", firstName: "Alice"), CancellationToken.None);

    await _botMessageService.Received(1).SendAndSaveMessageAsync(
        Arg.Any<long>(),
        Arg.Is<string>(t => t.Contains("@alice") && t.Contains("arrived")),
        Arg.Is<ParseMode>(m => m == ParseMode.Html),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task PostBypassAnnouncement_Substitutes_ChatName_Encoded()
{
    SetupTrustedBypass(
        adminTemplate: "Joined " + TrustedBypassConfig.ChatNameVariable,
        trustedTemplate: "Joined " + TrustedBypassConfig.ChatNameVariable);
    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    // hostile chat title with HTML
    var update = CreateJoinUpdate(chatId: 99, chatTitle: "<b>pwn</b>");
    await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

    await _botMessageService.Received(1).SendAndSaveMessageAsync(
        Arg.Any<long>(),
        Arg.Is<string>(t => t.Contains("&lt;b&gt;pwn&lt;/b&gt;") && !t.Contains("<b>pwn</b>")),
        Arg.Any<ParseMode>(),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task PostBypassAnnouncement_HostileFirstName_IsEncoded()
{
    SetupTrustedBypass(
        adminTemplate: TrustedBypassConfig.UsernameVariable + " joined",
        trustedTemplate: TrustedBypassConfig.UsernameVariable + " joined");
    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    // user with no username, hostile first name
    var update = CreateJoinUpdate(
        userId: 7, username: null, firstName: "<b>FAKE ADMIN</b>");
    await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

    await _botMessageService.Received(1).SendAndSaveMessageAsync(
        Arg.Any<long>(),
        Arg.Is<string>(t =>
            t.Contains("&lt;b&gt;FAKE ADMIN&lt;/b&gt;") &&
            !t.Contains("<b>FAKE ADMIN</b>")),
        Arg.Any<ParseMode>(),
        Arg.Any<CancellationToken>());
}

// Helper — if not already present in the fixture
private void SetupTrustedBypass(string adminTemplate, string trustedTemplate)
{
    var config = new WelcomeConfig
    {
        MainWelcomeMessage = "welcome",
        TrustedBypass =
        {
            Enabled = true,
            AnnouncementMessageAdmin = adminTemplate,
            AnnouncementMessageTrusted = trustedTemplate,
            AnnouncementTtlSeconds = 30,
        }
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(
        Arg.Any<ConfigType>(), Arg.Any<long>()).Returns(config);
}
```

If the existing `CreateJoinUpdate` helper doesn't accept `firstName` / `chatTitle` parameters, extend it to do so (add optional parameters with sensible defaults).

- [ ] **Step 2: Run**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeServiceTests.PostBypassAnnouncement -v minimal`
Expected: all 3 new tests pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs
git commit -m "test(welcome): cover announcement substitution + HTML encoding"
```

---

## Task 19: Unit tests — consumer clamping

**Files:**
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

- [ ] **Step 1: Add the three clamping tests**

```csharp
[Test]
public async Task PostBypassAnnouncement_OverLengthTemplate_TruncatesAndLogsWarning()
{
    var longTemplate = new string('x', 5000);
    SetupTrustedBypass(adminTemplate: longTemplate, trustedTemplate: longTemplate);
    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    await _welcomeService.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

    // Text sent must not exceed the template cap
    await _botMessageService.Received(1).SendAndSaveMessageAsync(
        Arg.Any<long>(),
        Arg.Is<string>(t => t.Length <= TrustedBypassConfig.MaxAnnouncementTemplateLength),
        Arg.Any<ParseMode>(),
        Arg.Any<CancellationToken>());

    // Warning logged with "truncating" keyword
    _logger.Received(1).Log(
        LogLevel.Warning,
        Arg.Any<EventId>(),
        Arg.Is<object>(o => o.ToString()!.Contains("truncating")),
        Arg.Any<Exception?>(),
        Arg.Any<Func<object, Exception?, string>>());
}

[Test]
public async Task PostBypassAnnouncement_NegativeTtl_ClampsToZero()
{
    var config = new WelcomeConfig
    {
        MainWelcomeMessage = "welcome",
        TrustedBypass =
        {
            Enabled = true,
            AnnouncementMessageTrusted = "hello {username}",
            AnnouncementTtlSeconds = -5,
        }
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(
        Arg.Any<ConfigType>(), Arg.Any<long>()).Returns(config);

    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    await _welcomeService.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

    await _jobScheduler.Received(1).ScheduleJobAsync(
        BackgroundJobNames.DeleteMessage,
        Arg.Any<DeleteMessagePayload>(),
        delaySeconds: 0,
        Arg.Any<string?>(),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task PostBypassAnnouncement_EmptyTemplate_SkipsSendAndSchedule()
{
    SetupTrustedBypass(adminTemplate: "", trustedTemplate: "");
    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Trusted, "Trusted user"));

    await _welcomeService.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

    await _botMessageService.DidNotReceive().SendAndSaveMessageAsync(
        Arg.Any<long>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    await _jobScheduler.DidNotReceive().ScheduleJobAsync(
        Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
}

[Test]
public async Task PostBypassAnnouncement_EnabledFalse_SkipsAnnouncement()
{
    var config = new WelcomeConfig
    {
        MainWelcomeMessage = "welcome",
        TrustedBypass = { Enabled = false }, // default templates, but disabled
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(
        Arg.Any<ConfigType>(), Arg.Any<long>()).Returns(config);

    _resolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(new BypassResolution(BypassDecision.Admin, "Telegram chat admin (2 chats)"));

    await _welcomeService.HandleChatMemberUpdateAsync(CreateJoinUpdate(), CancellationToken.None);

    // Admin bypass still fires Activate+Audit, but no announcement when Enabled=false
    await _botMessageService.DidNotReceive().SendAndSaveMessageAsync(
        Arg.Any<long>(), Arg.Any<string>(), Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
}
```

If `_logger` isn't already a `Substitute.For<ILogger<WelcomeService>>()` in the fixture, make it one and assert against it using NSubstitute's logger pattern. If the existing tests use a different logger-assertion pattern, match that.

- [ ] **Step 2: Run**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter FullyQualifiedName~WelcomeServiceTests.PostBypassAnnouncement -v minimal`
Expected: all 4 new tests plus the 3 from Task 18 pass.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs
git commit -m "test(welcome): cover consumer clamping (template length, negative ttl, empty, disabled)"
```

---

## Task 20: Integration test — Trusted user + toggle-off ⇒ None

**Files:**
- Modify: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`

- [ ] **Step 1: Add the scenario**

Append to the existing fixture (match the existing scenario style — setup, act, assert):

```csharp
[Test]
public async Task TrustedUser_ToggleOff_FallsThroughToNormalFlow_NoAuditRow()
{
    // Arrange: trusted user + per-chat toggle OFF
    await SeedTrustedUserAsync(TestUserId);
    await SaveWelcomeConfigAsync(new WelcomeConfig
    {
        MainWelcomeMessage = "welcome",
        TrustedBypass = { Enabled = false },
    });

    var update = BuildChatMemberJoinUpdate(TestUserId, TestChatId);

    // Act
    await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

    // Assert: no WelcomeBypass audit row
    await using var context = await _contextFactory.CreateDbContextAsync();
    var bypassRows = await context.UserActions
        .Where(a => a.UserId == TestUserId && a.ChatId == TestChatId &&
                    a.ActionType == UserActionType.WelcomeBypass)
        .ToListAsync();
    Assert.That(bypassRows, Is.Empty);

    // Normal welcome path: user was muted (restrict handler called)
    // If the integration test has a fake/real bot API verifier, assert it saw a restrict call.
    // Otherwise assert the user_actions table has a Mute row or the WelcomeResponses table has an open row.
    var welcomeResponseRow = await context.WelcomeResponses
        .FirstOrDefaultAsync(r => r.UserId == TestUserId && r.ChatId == TestChatId);
    Assert.That(welcomeResponseRow, Is.Not.Null,
        "Expected a WelcomeResponse row indicating the normal welcome flow ran.");
}
```

(If `SeedTrustedUserAsync`, `SaveWelcomeConfigAsync`, `BuildChatMemberJoinUpdate` don't yet exist as helpers, extract them from the pre-existing tests' inline setup — or add them matching the local style.)

- [ ] **Step 2: Run**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter FullyQualifiedName~WelcomeFlowBypassIntegrationTests.TrustedUser_ToggleOff -v minimal`
Expected: test passes.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs
git commit -m "test(welcome): integration scenario for trusted user with toggle off"
```

---

## Final verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build TelegramGroupsAdmin.sln`
Expected: 0 errors, 0 warnings introduced by this plan's changes.

- [ ] **Step 2: Full test run**

Run: `dotnet test TelegramGroupsAdmin.sln --configuration Release -v minimal`
Expected: all tests pass (baseline was 285 passing per pre-plan validation run).

- [ ] **Step 3: File the tracking issue for §5.1 wiring gap**

Before opening the PR, file a GitHub issue per §5.1 of the spec:

```bash
gh issue create \
  --title "refactor: wire ConfigService through the Data-DTO/Mapping layer" \
  --label "enhancement,backend,technical-debt" \
  --body "$(cat <<'BODY'
## Context
Discovered during the welcome-bypass follow-up review (see `docs/superpowers/specs/2026-04-20-welcome-bypass-followup-design.md` §5.1).

`ConfigService.GetAsync<T>` / `SaveAsync<T>` use `JsonSerializer` directly against the business model `T`, bypassing the `*ConfigData` DTO and `*ConfigMappings` layer that was designed to separate the wire-stable JSON shape from the business model.

## Affected configs (confirmed bypassed)
- `WelcomeConfig`

## To verify
- `LogConfig`, `ModerationConfig`, `UrlFilterConfig`, `TelegramBotConfig`, `ServiceMessageDeletionConfig`, `BanCelebrationConfig`

## Already wired (no action needed)
- `ContentDetectionConfig` (via `ContentDetectionConfigRepository` and EF Core `OwnsOne().ToJson()`)
- `UserApiConfig` (via `SystemConfigRepository.GetUserApiConfigAsync`)

## Resolution options (to be decided during issue discussion)
- (a) Teach `ConfigService` to route through a mapper registry when one is registered for `T`.
- (b) Migrate each affected config to its own dedicated repository (like `UserApiConfig`).
BODY
)"
```

Verify the label names exist in TGA's custom set via `gh label list` before running (labels may have slightly different casing/names). If `technical-debt` does not exist, drop it from `--label`.

- [ ] **Step 4: Ready to open PR**

After all tasks complete and the tracking issue is filed, proceed with opening the pull request against `develop`. The PR body should reference the tracking issue. PR creation is outside this plan's scope — invoke `superpowers:finishing-a-development-branch` or use the standard repo PR workflow.
