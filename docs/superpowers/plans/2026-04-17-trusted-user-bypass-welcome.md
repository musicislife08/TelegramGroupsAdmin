# Trusted User Bypass for Welcome System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Telegram chat admins, linked web admins, and trusted users bypass the welcome consent flow and security checks, with an auto-deleting in-chat announcement and audit trail — all driven through a single `IWelcomeBypassResolver`.

**Architecture:** All three bypass paths (`ChatAdmin`, `WebAdmin`, `Trusted`) converge in a new resolver service called from `WelcomeService.HandleChatMemberUpdateAsync` as an early-exit step. The existing Step 1 silent-skip for Telegram chat admins is removed and its responsibility folded into the resolver so all privileged bypasses produce uniform audit entries, announcements, and metric labels. A new per-chat toggle on `WelcomeConfig.TrustedBypass` gates only the trusted-user path; the two admin paths are always on. All string literals introduced by the feature are declared as named constants per project convention.

**Tech Stack:** .NET 10.0, EF Core 10, PostgreSQL JSONB, NUnit + NSubstitute, Testcontainers.PostgreSQL, bUnit, MudBlazor 9, Quartz.NET, OpenTelemetry.

**Spec:** `docs/superpowers/specs/2026-04-17-trusted-user-bypass-welcome-design.md`

**Branch:** `feat/trusted-user-bypass-welcome`

---

## Task 1: SystemActorIds constants + Actor.cs refactor

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/SystemActorIds.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/Actor.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/SystemActorIdsTests.cs`

- [ ] **Step 1: Create the constants file**

Create `TelegramGroupsAdmin.Core/Models/SystemActorIds.cs`:
```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Canonical string identifiers for every system-issued <see cref="Actor"/>.
/// Single source of truth — referenced from Actor.cs, audit filters, SQL seeds, and tests.
/// </summary>
public static class SystemActorIds
{
    public const string AutoDetection = "auto_detection";
    public const string BotProtection = "bot_protection";
    public const string FileScanner = "file_scanner";
    public const string AutoTrust = "auto_trust";
    public const string Impersonation = "impersonation";
    public const string AutoBan = "auto_ban";
    public const string Cas = "cas";
    public const string LanguageWarning = "language_warning";
    public const string SystemSeed = "system_seed";
    public const string InitialSeed = "initial_seed";
    public const string WebAdmin = "web_admin";
    public const string ExamFlow = "exam_flow";
    public const string WelcomeFlow = "welcome_flow";
    public const string TempbanExpiry = "tempban_expiry";
    public const string Unknown = "unknown";
    public const string ProfileScan = "profile_scan";
    public const string UsernameBlacklist = "username_blacklist";
    public const string Bootstrap = "bootstrap";
    public const string ProfileDiffDetection = "profile_diff_detection";
    public const string WelcomeBypass = "welcome_bypass";
}
```

- [ ] **Step 2: Write the failing test for SystemActorIds consistency**

Create `TelegramGroupsAdmin.UnitTests/Core/SystemActorIdsTests.cs`:
```csharp
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core;

[TestFixture]
public class SystemActorIdsTests
{
    [Test]
    public void AllExistingActorStaticFields_ResolveToSystemActorIdsValues()
    {
        Assert.That(Actor.AutoDetection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoDetection));
        Assert.That(Actor.BotProtection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.BotProtection));
        Assert.That(Actor.FileScanner.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.FileScanner));
        Assert.That(Actor.AutoTrust.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoTrust));
        Assert.That(Actor.Impersonation.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Impersonation));
        Assert.That(Actor.AutoBan.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoBan));
        Assert.That(Actor.Cas.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Cas));
        Assert.That(Actor.LanguageWarning.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.LanguageWarning));
        Assert.That(Actor.SystemSeed.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.SystemSeed));
        Assert.That(Actor.ExamFlow.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ExamFlow));
        Assert.That(Actor.WelcomeFlow.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeFlow));
        Assert.That(Actor.TempbanExpiry.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.TempbanExpiry));
        Assert.That(Actor.Unknown.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Unknown));
        Assert.That(Actor.ProfileScan.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ProfileScan));
        Assert.That(Actor.UsernameBlacklist.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.UsernameBlacklist));
        Assert.That(Actor.Bootstrap.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Bootstrap));
        Assert.That(Actor.ProfileDiffDetection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ProfileDiffDetection));
    }

    [Test]
    public void FromSystem_ResolvesDisplayName_ForEveryKnownConstant()
    {
        Assert.That(Actor.FromSystem(SystemActorIds.AutoDetection).DisplayName, Is.EqualTo("Auto-Detection"));
        Assert.That(Actor.FromSystem(SystemActorIds.WelcomeBypass).DisplayName, Is.EqualTo("Welcome Bypass"));
        Assert.That(Actor.FromSystem(SystemActorIds.BotProtection).DisplayName, Is.EqualTo("Bot Protection"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~SystemActorIdsTests" --no-build 2>&1 | tail -20`
Expected: FAIL — `Actor.FromSystem(SystemActorIds.WelcomeBypass).DisplayName` returns `"welcome_bypass"` (the fallback) because `WelcomeBypass` isn't in the display-name switch yet.

- [ ] **Step 4: Refactor Actor.cs to use SystemActorIds + add WelcomeBypass**

Modify `TelegramGroupsAdmin.Core/Models/Actor.cs`. Replace the static-field block and display-name switch with:
```csharp
// Common system actors (eliminates magic strings)
public static readonly Actor AutoDetection = FromSystem(SystemActorIds.AutoDetection);
public static readonly Actor BotProtection = FromSystem(SystemActorIds.BotProtection);
public static readonly Actor FileScanner = FromSystem(SystemActorIds.FileScanner);
public static readonly Actor AutoTrust = FromSystem(SystemActorIds.AutoTrust);
public static readonly Actor Impersonation = FromSystem(SystemActorIds.Impersonation);
public static readonly Actor AutoBan = FromSystem(SystemActorIds.AutoBan);
public static readonly Actor Cas = FromSystem(SystemActorIds.Cas);
public static readonly Actor LanguageWarning = FromSystem(SystemActorIds.LanguageWarning);
public static readonly Actor SystemSeed = FromSystem(SystemActorIds.SystemSeed);
public static readonly Actor ExamFlow = FromSystem(SystemActorIds.ExamFlow);
public static readonly Actor WelcomeFlow = FromSystem(SystemActorIds.WelcomeFlow);
public static readonly Actor TempbanExpiry = FromSystem(SystemActorIds.TempbanExpiry);
public static readonly Actor Unknown = FromSystem(SystemActorIds.Unknown);
public static readonly Actor ProfileScan = FromSystem(SystemActorIds.ProfileScan);
public static readonly Actor UsernameBlacklist = FromSystem(SystemActorIds.UsernameBlacklist);
public static readonly Actor Bootstrap = FromSystem(SystemActorIds.Bootstrap);
public static readonly Actor ProfileDiffDetection = FromSystem(SystemActorIds.ProfileDiffDetection);
public static readonly Actor WelcomeBypass = FromSystem(SystemActorIds.WelcomeBypass);
```

And update the `FromSystem` display-name switch:
```csharp
var displayName = systemIdentifier switch
{
    SystemActorIds.AutoDetection => "Auto-Detection",
    SystemActorIds.BotProtection => "Bot Protection",
    SystemActorIds.FileScanner => "File Scanner",
    SystemActorIds.AutoTrust => "Auto-Trust",
    SystemActorIds.Impersonation => "Impersonation Detection",
    SystemActorIds.AutoBan => "Auto-Ban",
    SystemActorIds.Cas => "CAS Anti-Spam",
    SystemActorIds.LanguageWarning => "Language Warning",
    SystemActorIds.SystemSeed => "System Seed",
    SystemActorIds.InitialSeed => "Initial Seed",
    SystemActorIds.WebAdmin => "Web Admin (Legacy)",
    SystemActorIds.ExamFlow => "Exam Flow",
    SystemActorIds.WelcomeFlow => "Welcome Flow",
    SystemActorIds.TempbanExpiry => "Tempban Expiry",
    SystemActorIds.Unknown => "Unknown",
    SystemActorIds.ProfileScan => "Profile Scan",
    SystemActorIds.UsernameBlacklist => "Username Blacklist",
    SystemActorIds.Bootstrap => "CLI Bootstrap",
    SystemActorIds.ProfileDiffDetection => "Profile Change Detection",
    SystemActorIds.WelcomeBypass => "Welcome Bypass",
    _ => systemIdentifier
};
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~SystemActorIdsTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models/SystemActorIds.cs TelegramGroupsAdmin.Core/Models/Actor.cs TelegramGroupsAdmin.UnitTests/Core/SystemActorIdsTests.cs
git commit -F- <<'EOF'
refactor: centralize system actor ID strings in SystemActorIds

Extract the 19 system-actor string literals scattered across Actor.cs
(static field declarations + FromSystem display-name switch) into a
dedicated SystemActorIds constants class. Adds Actor.WelcomeBypass and
SystemActorIds.WelcomeBypass as the new entries used by the trusted
bypass feature.

EOF
```

---

## Task 2: UserActionType.WelcomeBypass enum value

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/UserActionType.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`

- [ ] **Step 1: Add WelcomeBypass to the Data-layer enum**

Modify `TelegramGroupsAdmin.Data/Models/UserActionType.cs`. Add at the end (after `ProfileChange = 10`):
```csharp
/// <summary>
/// User auto-admitted past the welcome flow due to privileged status
/// (Telegram chat admin, linked web admin) or trusted status.
/// </summary>
WelcomeBypass = 11
```

- [ ] **Step 2: Add WelcomeBypass to the Telegram-layer enum**

Modify `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`. Add the same entry:
```csharp
/// <summary>
/// User auto-admitted past the welcome flow due to privileged status
/// (Telegram chat admin, linked web admin) or trusted status.
/// </summary>
WelcomeBypass = 11
```

- [ ] **Step 3: Verify both enums compile together**

Run: `dotnet build TelegramGroupsAdmin.Data TelegramGroupsAdmin.Telegram --no-restore 2>&1 | tail -5`
Expected: `Build succeeded` with zero errors/warnings.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Data/Models/UserActionType.cs TelegramGroupsAdmin.Telegram/Models/UserActionType.cs
git commit -F- <<'EOF'
feat: add UserActionType.WelcomeBypass enum value

Adds the new action type (id 11) to both the Data DTO enum and the
Telegram domain enum. Documents that it covers chat admin, web admin,
and trusted user bypass paths.

EOF
```

---

## Task 3: TrustedBypassConfig class

**Files:**
- Create: `TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Configuration/TrustedBypassConfigTests.cs`

- [ ] **Step 1: Write the failing test**

Create `TelegramGroupsAdmin.UnitTests/Configuration/TrustedBypassConfigTests.cs`:
```csharp
using System.Text.Json;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class TrustedBypassConfigTests
{
    [Test]
    public void DefaultConstruction_ProducesExpectedDefaults()
    {
        var config = new TrustedBypassConfig();

        Assert.That(config.Enabled, Is.False);
        Assert.That(config.AnnouncementMessage, Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementMessage));
        Assert.That(config.AnnouncementTtlSeconds, Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementTtlSeconds));
    }

    [Test]
    public void DefaultAnnouncementMessage_ContainsUsernameVariable()
    {
        Assert.That(TrustedBypassConfig.DefaultAnnouncementMessage,
            Does.Contain(TrustedBypassConfig.UsernameVariable));
    }

    [Test]
    public void JsonRoundTrip_PreservesDefaults()
    {
        var original = new TrustedBypassConfig();
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<TrustedBypassConfig>(json)!;

        Assert.That(roundTripped.Enabled, Is.EqualTo(original.Enabled));
        Assert.That(roundTripped.AnnouncementMessage, Is.EqualTo(original.AnnouncementMessage));
        Assert.That(roundTripped.AnnouncementTtlSeconds, Is.EqualTo(original.AnnouncementTtlSeconds));
    }

    [Test]
    public void JsonRoundTrip_PreservesCustomValues()
    {
        var original = new TrustedBypassConfig
        {
            Enabled = true,
            AnnouncementMessage = "custom {username}",
            AnnouncementTtlSeconds = 45,
        };
        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<TrustedBypassConfig>(json)!;

        Assert.That(roundTripped.Enabled, Is.True);
        Assert.That(roundTripped.AnnouncementMessage, Is.EqualTo("custom {username}"));
        Assert.That(roundTripped.AnnouncementTtlSeconds, Is.EqualTo(45));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TrustedBypassConfigTests" --no-build 2>&1 | tail -10`
Expected: FAIL — `TrustedBypassConfig` type does not exist.

- [ ] **Step 3: Create the config class**

Create `TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs`:
```csharp
namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for the trusted user / privileged bypass feature of the welcome system.
/// Stored inside the Welcome JSONB config row under <see cref="WelcomeConfig.TrustedBypass"/>.
/// </summary>
public class TrustedBypassConfig
{
    // Public so UI helper text and service code reference the same token.
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";

    // Internal so tests, UI reset-to-default, and .Default factories share one source of truth.
    internal const string DefaultAnnouncementMessage =
        UsernameVariable + " welcomed automatically — trusted from other groups.";
    internal const int DefaultAnnouncementTtlSeconds = 30;

    /// <summary>
    /// Master toggle for the trusted-user bypass.
    /// When true, users with <c>IsTrusted = true</c> skip the welcome consent flow and all security checks.
    /// Web admins (GlobalAdmin/Owner with linked TelegramUserId) and Telegram chat admins
    /// always bypass regardless of this flag.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Message posted in chat when a bypass occurs.
    /// Variables: <see cref="UsernameVariable"/>, <see cref="ChatNameVariable"/>.
    /// </summary>
    public string AnnouncementMessage { get; set; } = DefaultAnnouncementMessage;

    /// <summary>
    /// Seconds until the announcement is auto-deleted. Range: 10-300.
    /// </summary>
    public int AnnouncementTtlSeconds { get; set; } = DefaultAnnouncementTtlSeconds;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TrustedBypassConfigTests"`
Expected: PASS — all 4 cases green.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs TelegramGroupsAdmin.UnitTests/Configuration/TrustedBypassConfigTests.cs
git commit -F- <<'EOF'
feat: add TrustedBypassConfig for welcome-system bypass feature

New nested config object with Enabled toggle, customizable
announcement message, and TTL. Template variables {username} and
{chat_name} are exposed as public const so UI helper text and
service code reference the same strings.

EOF
```

---

## Task 4: Wire TrustedBypass onto WelcomeConfig + mappings

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/WelcomeConfig.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/WelcomeConfigData.cs`
- Modify: `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Configuration/ContentDetectionConfigMappingsTests.cs` (or the welcome-specific mapping test file if it exists — check with `ls TelegramGroupsAdmin.UnitTests/Configuration/`)

- [ ] **Step 1: Check existing mapping test location**

Run: `find TelegramGroupsAdmin.UnitTests -name "WelcomeConfigMapping*" -o -name "WelcomeMapping*"`

If a dedicated welcome mapping test file exists, use it. Otherwise create:
`TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs`

- [ ] **Step 2: Write the failing test for WelcomeConfig defaults + null-safety**

Create or extend the welcome mapping test file:
```csharp
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Mappings;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.UnitTests.Configuration;

[TestFixture]
public class WelcomeConfigMappingsTests
{
    [Test]
    public void NewWelcomeConfig_HasTrustedBypassPopulated()
    {
        var config = new WelcomeConfig();

        Assert.That(config.TrustedBypass, Is.Not.Null);
        Assert.That(config.TrustedBypass.Enabled, Is.False);
        Assert.That(config.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(30));
    }

    [Test]
    public void ToModel_NullTrustedBypass_YieldsDefaults()
    {
        var data = new WelcomeConfigData
        {
            TrustedBypass = null,
        };

        var model = data.ToModel();

        Assert.That(model.TrustedBypass, Is.Not.Null);
        Assert.That(model.TrustedBypass.Enabled, Is.False);
        Assert.That(model.TrustedBypass.AnnouncementMessage,
            Is.EqualTo(TrustedBypassConfig.DefaultAnnouncementMessage));
    }

    [Test]
    public void ToDto_ThenToModel_RoundTripsTrustedBypass()
    {
        var original = new WelcomeConfig
        {
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessage = "hello {username}",
                AnnouncementTtlSeconds = 42,
            }
        };

        var dto = original.ToData();
        var roundTripped = dto.ToModel();

        Assert.That(roundTripped.TrustedBypass.Enabled, Is.True);
        Assert.That(roundTripped.TrustedBypass.AnnouncementMessage, Is.EqualTo("hello {username}"));
        Assert.That(roundTripped.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(42));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests" --no-build 2>&1 | tail -20`
Expected: FAIL — `WelcomeConfig.TrustedBypass` property does not exist.

- [ ] **Step 4: Add TrustedBypass property to WelcomeConfig**

Modify `TelegramGroupsAdmin.Configuration/Models/Welcome/WelcomeConfig.cs`. Add (near the other nested config properties like `JoinSecurity`):
```csharp
/// <summary>
/// Trusted user / privileged bypass configuration.
/// When enabled, trusted users skip the welcome flow. Chat admins and linked web
/// admins always bypass regardless of this toggle.
/// </summary>
public TrustedBypassConfig TrustedBypass { get; set; } = new();
```

- [ ] **Step 5: Add TrustedBypass property to WelcomeConfigData DTO**

Modify `TelegramGroupsAdmin.Data/Models/Configs/WelcomeConfigData.cs`. Add a corresponding nullable property:
```csharp
/// <summary>
/// Nullable for backward compatibility — existing JSONB blobs may not have this key yet.
/// </summary>
public TrustedBypassConfigData? TrustedBypass { get; set; }
```

Create `TelegramGroupsAdmin.Data/Models/Configs/TrustedBypassConfigData.cs`:
```csharp
namespace TelegramGroupsAdmin.Data.Models.Configs;

public class TrustedBypassConfigData
{
    public bool Enabled { get; set; }
    public string AnnouncementMessage { get; set; } = string.Empty;
    public int AnnouncementTtlSeconds { get; set; }
}
```

- [ ] **Step 6: Add round-trip mappings**

Modify `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs`. Add within the existing `ToModel` method (return expression):
```csharp
TrustedBypass = data.TrustedBypass is null
    ? new TrustedBypassConfig()
    : new TrustedBypassConfig
    {
        Enabled = data.TrustedBypass.Enabled,
        AnnouncementMessage = string.IsNullOrEmpty(data.TrustedBypass.AnnouncementMessage)
            ? TrustedBypassConfig.DefaultAnnouncementMessage
            : data.TrustedBypass.AnnouncementMessage,
        AnnouncementTtlSeconds = data.TrustedBypass.AnnouncementTtlSeconds <= 0
            ? TrustedBypassConfig.DefaultAnnouncementTtlSeconds
            : data.TrustedBypass.AnnouncementTtlSeconds,
    },
```

Add within the existing `ToData` method (return expression):
```csharp
TrustedBypass = new TrustedBypassConfigData
{
    Enabled = model.TrustedBypass.Enabled,
    AnnouncementMessage = model.TrustedBypass.AnnouncementMessage,
    AnnouncementTtlSeconds = model.TrustedBypass.AnnouncementTtlSeconds,
},
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Models/Welcome/WelcomeConfig.cs TelegramGroupsAdmin.Data/Models/Configs/WelcomeConfigData.cs TelegramGroupsAdmin.Data/Models/Configs/TrustedBypassConfigData.cs TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs
git commit -F- <<'EOF'
feat: wire TrustedBypassConfig onto WelcomeConfig with null-safe mappings

Adds TrustedBypass property to WelcomeConfig and WelcomeConfigData,
with round-trip mappings that gracefully default missing or empty
values. Existing JSONB blobs without the new key load with correct
defaults — no migration needed.

EOF
```

---

## Task 5: BypassDecision enum + IWelcomeBypassResolver interface

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/Welcome/IWelcomeBypassResolver.cs`

- [ ] **Step 1: Create the enum**

Create `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs`:
```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Reason a welcome-flow bypass fired for a joining user.
/// </summary>
public enum BypassDecision
{
    /// <summary>No bypass — user proceeds through normal welcome flow.</summary>
    None = 0,

    /// <summary>User is a Telegram chat administrator or creator.</summary>
    ChatAdmin = 1,

    /// <summary>User is linked to a web admin with GlobalAdmin or Owner permission level.</summary>
    WebAdmin = 2,

    /// <summary>User has IsTrusted = true and the per-chat bypass toggle is enabled.</summary>
    Trusted = 3,
}
```

- [ ] **Step 2: Create the interface**

Create `TelegramGroupsAdmin.Telegram/Services/Welcome/IWelcomeBypassResolver.cs`:
```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Decides whether a joining user bypasses the welcome flow.
/// Evaluates three rules in priority order: Telegram chat admin, linked web admin, trusted user.
/// </summary>
public interface IWelcomeBypassResolver
{
    Task<BypassDecision> ResolveAsync(
        UserIdentity user,
        ChatIdentity chat,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Verify compile**

Run: `dotnet build TelegramGroupsAdmin.Telegram --no-restore 2>&1 | tail -5`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs TelegramGroupsAdmin.Telegram/Services/Welcome/IWelcomeBypassResolver.cs
git commit -F- <<'EOF'
feat: add BypassDecision enum + IWelcomeBypassResolver interface

Enum has four variants: None, ChatAdmin, WebAdmin, Trusted. The
resolver contract takes a UserIdentity and ChatIdentity and returns
the priority-ordered decision.

EOF
```

---

## Task 6: WelcomeBypassResolver implementation + unit tests

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs`
- Create: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeBypassResolverTests.cs`

- [ ] **Step 1: Write the failing test — chat admin case**

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeBypassResolverTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

[TestFixture]
public class WelcomeBypassResolverTests
{
    private const long TestUserId = 11111L;
    private const long TestChatId = -22222L;

    private IBotUserService _botUserService = null!;
    private ITelegramUserMappingRepository _mappingRepo = null!;
    private ITelegramUserRepository _userRepo = null!;
    private IConfigService _configService = null!;
    private WelcomeBypassResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _botUserService = Substitute.For<IBotUserService>();
        _mappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _userRepo = Substitute.For<ITelegramUserRepository>();
        _configService = Substitute.For<IConfigService>();

        var services = new ServiceCollection();
        services.AddSingleton(_botUserService);
        services.AddSingleton(_mappingRepo);
        services.AddSingleton(_userRepo);
        services.AddSingleton(_configService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _resolver = new WelcomeBypassResolver(scopeFactory, NullLogger<WelcomeBypassResolver>.Instance);
    }

    private UserIdentity TestUser() => UserIdentity.FromId(TestUserId);
    private ChatIdentity TestChat() => ChatIdentity.FromId(TestChatId);

    private void StubChatMember(ChatMemberStatus status)
    {
        var member = status switch
        {
            ChatMemberStatus.Administrator => (ChatMember)new ChatMemberAdministrator { User = new User { Id = TestUserId } },
            ChatMemberStatus.Creator => new ChatMemberOwner { User = new User { Id = TestUserId } },
            ChatMemberStatus.Member => new ChatMemberMember { User = new User { Id = TestUserId } },
            _ => new ChatMemberMember { User = new User { Id = TestUserId } },
        };
        _botUserService.GetChatMemberAsync(TestChatId, TestUserId, Arg.Any<CancellationToken>()).Returns(member);
    }

    [Test]
    public async Task Resolve_ChatAdmin_ReturnsChatAdmin()
    {
        StubChatMember(ChatMemberStatus.Administrator);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.ChatAdmin));
    }

    [Test]
    public async Task Resolve_ChatCreator_ReturnsChatAdmin()
    {
        StubChatMember(ChatMemberStatus.Creator);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.ChatAdmin));
    }

    [Test]
    public async Task Resolve_LinkedOwner_ReturnsWebAdmin()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)PermissionLevel.Owner);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.WebAdmin));
    }

    [Test]
    public async Task Resolve_LinkedGlobalAdmin_ReturnsWebAdmin()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)PermissionLevel.GlobalAdmin);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.WebAdmin));
    }

    [Test]
    public async Task Resolve_LinkedChatLevelAdmin_FallsThroughToTrustCheck()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)PermissionLevel.Admin);  // Level 0 — not considered a web admin for bypass
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = false } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(false);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_UnlinkedTrustedUser_ToggleOn_ReturnsTrusted()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.Trusted));
    }

    [Test]
    public async Task Resolve_TrustedUser_ToggleOff_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = false } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_UnlinkedUntrustedUser_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true } });
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(false);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }

    [Test]
    public async Task Resolve_NullConfig_FallsBackToDefaultToggleOff_ReturnsNone()
    {
        StubChatMember(ChatMemberStatus.Member);
        _mappingRepo.GetPermissionLevelByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, TestChatId)
            .Returns((WelcomeConfig?)null);
        _userRepo.IsTrustedAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var decision = await _resolver.ResolveAsync(TestUser(), TestChat(), CancellationToken.None);

        Assert.That(decision, Is.EqualTo(BypassDecision.None));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeBypassResolverTests" --no-build 2>&1 | tail -20`
Expected: FAIL — `WelcomeBypassResolver` type does not exist.

- [ ] **Step 3: Implement the resolver**

Create `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Evaluates the three bypass rules in priority order: Telegram chat admin,
/// linked web admin, trusted user (toggle-gated). Returns the first match.
/// Singleton via <see cref="IServiceScopeFactory"/> for scoped-service access.
/// </summary>
public sealed class WelcomeBypassResolver(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeBypassResolver> logger) : IWelcomeBypassResolver
{
    // Log format strings live here because they are only used by this class.
    private const string LogFormatChatAdmin =
        "Welcome bypass: {User} in {Chat} - Telegram chat admin/creator";
    private const string LogFormatWebAdmin =
        "Welcome bypass: {User} in {Chat} - linked web admin (level {Level})";
    private const string LogFormatTrusted =
        "Welcome bypass: {User} in {Chat} - trusted user, bypass enabled";

    public async Task<BypassDecision> ResolveAsync(
        UserIdentity user, ChatIdentity chat, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Rule 1: Telegram chat admin / creator (always on)
        var userService = sp.GetRequiredService<IBotUserService>();
        var chatMember = await userService.GetChatMemberAsync(chat.Id, user.Id, cancellationToken);
        if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
        {
            logger.LogInformation(LogFormatChatAdmin, user.ToLogInfo(), chat.ToLogInfo());
            return BypassDecision.ChatAdmin;
        }

        // Rule 2: Linked web admin (always on)
        var mappingRepo = sp.GetRequiredService<ITelegramUserMappingRepository>();
        var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, cancellationToken);
        if (permissionLevel is (int)PermissionLevel.GlobalAdmin or (int)PermissionLevel.Owner)
        {
            logger.LogInformation(LogFormatWebAdmin, user.ToLogInfo(), chat.ToLogInfo(), permissionLevel);
            return BypassDecision.WebAdmin;
        }

        // Rule 3: Trusted user (toggle-gated)
        var configService = sp.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id)
                     ?? new WelcomeConfig();
        if (!config.TrustedBypass.Enabled)
        {
            return BypassDecision.None;
        }

        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();
        if (await userRepo.IsTrustedAsync(user.Id, cancellationToken))
        {
            logger.LogInformation(LogFormatTrusted, user.ToLogInfo(), chat.ToLogInfo());
            return BypassDecision.Trusted;
        }

        return BypassDecision.None;
    }
}
```

- [ ] **Step 4: Run tests to verify all 9 cases pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeBypassResolverTests"`
Expected: PASS — 9/9.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/Welcome/WelcomeBypassResolverTests.cs
git commit -F- <<'EOF'
feat: implement WelcomeBypassResolver with priority-ordered rules

Evaluates chat-admin, linked web-admin, and trusted-user paths in
priority order. Short-circuits on first match. Full unit test
coverage of all 9 decision-matrix cases including the null-config
fallback.

EOF
```

---

## Task 7: DI registration for WelcomeBypassResolver

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Register the resolver**

Modify `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`. Find the existing `AddSingleton<IWelcomeAdmissionHandler, WelcomeAdmissionHandler>()` call and add right after it:
```csharp
services.AddSingleton<IWelcomeBypassResolver, WelcomeBypassResolver>();
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build TelegramGroupsAdmin.Telegram --no-restore 2>&1 | tail -5`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs
git commit -F- <<'EOF'
chore: register IWelcomeBypassResolver as singleton

EOF
```

---

## Task 8: IAuditHandler.LogWelcomeBypassAsync signature + implementation

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/IAuditHandler.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/AuditHandlerTests.cs` (check if exists, else create)

- [ ] **Step 1: Locate or create the audit handler test file**

Run: `find TelegramGroupsAdmin.UnitTests -name "AuditHandlerTests.cs"`

If the file exists, extend it. If not, the tests in Step 2 are a new file at that path.

- [ ] **Step 2: Write the failing test**

Create or extend `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/AuditHandlerTests.cs` with:
```csharp
[Test]
public async Task LogWelcomeBypassAsync_ChatAdmin_WritesExpectedRow()
{
    UserActionRecord? captured = null;
    _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
        .Returns(1L);

    await _handler.LogWelcomeBypassAsync(
        UserIdentity.FromId(100),
        ChatIdentity.FromId(-200),
        BypassDecision.ChatAdmin,
        CancellationToken.None);

    Assert.That(captured, Is.Not.Null);
    Assert.That(captured!.ActionType, Is.EqualTo(UserActionType.WelcomeBypass));
    Assert.That(captured.IssuedBy.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeBypass));
    Assert.That(captured.ChatId, Is.EqualTo(-200));
    Assert.That(captured.Reason, Is.EqualTo("Telegram chat admin/creator"));
}

[Test]
public async Task LogWelcomeBypassAsync_WebAdmin_WritesExpectedRow()
{
    UserActionRecord? captured = null;
    _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
        .Returns(1L);

    await _handler.LogWelcomeBypassAsync(
        UserIdentity.FromId(100),
        ChatIdentity.FromId(-200),
        BypassDecision.WebAdmin,
        CancellationToken.None);

    Assert.That(captured!.Reason, Is.EqualTo("Linked web admin (GlobalAdmin/Owner)"));
}

[Test]
public async Task LogWelcomeBypassAsync_Trusted_WritesExpectedRow()
{
    UserActionRecord? captured = null;
    _userActionsRepo.InsertAsync(Arg.Do<UserActionRecord>(r => captured = r), Arg.Any<CancellationToken>())
        .Returns(1L);

    await _handler.LogWelcomeBypassAsync(
        UserIdentity.FromId(100),
        ChatIdentity.FromId(-200),
        BypassDecision.Trusted,
        CancellationToken.None);

    Assert.That(captured!.Reason, Is.EqualTo("Trusted user, bypass enabled"));
}
```

If the file is new, add a complete fixture skeleton at the top:
```csharp
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Handlers;

[TestFixture]
public class AuditHandlerTests
{
    private IUserActionsRepository _userActionsRepo = null!;
    private AuditHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userActionsRepo = Substitute.For<IUserActionsRepository>();
        _handler = new AuditHandler(_userActionsRepo, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditHandler>.Instance);
    }

    // ... tests above ...
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~AuditHandlerTests.LogWelcomeBypass" --no-build 2>&1 | tail -10`
Expected: FAIL — `LogWelcomeBypassAsync` method does not exist.

- [ ] **Step 4: Add the interface method**

Modify `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/IAuditHandler.cs`. Add:
```csharp
/// <summary>
/// Log a welcome-flow bypass to the audit trail.
/// Called whenever the bypass resolver returns a non-None decision.
/// </summary>
Task LogWelcomeBypassAsync(
    UserIdentity user,
    ChatIdentity chat,
    BypassDecision decision,
    CancellationToken cancellationToken = default);
```

Add the using at the top:
```csharp
using TelegramGroupsAdmin.Telegram.Services.Welcome;
```

- [ ] **Step 5: Add the implementation**

Modify `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs`. Add the class-top constants (near other `private const` fields if any, or at the top of the class):
```csharp
// Bypass reason strings — only emitted from this class.
private const string BypassReasonChatAdmin = "Telegram chat admin/creator";
private const string BypassReasonWebAdmin  = "Linked web admin (GlobalAdmin/Owner)";
private const string BypassReasonTrusted   = "Trusted user, bypass enabled";
private const string BypassReasonFallback  = "Bypass";
```

Add the method implementation (following the shape of the existing `LogRestorePermissionsAsync`):
```csharp
public async Task LogWelcomeBypassAsync(
    UserIdentity user,
    ChatIdentity chat,
    BypassDecision decision,
    CancellationToken cancellationToken = default)
{
    var reason = decision switch
    {
        BypassDecision.ChatAdmin => BypassReasonChatAdmin,
        BypassDecision.WebAdmin  => BypassReasonWebAdmin,
        BypassDecision.Trusted   => BypassReasonTrusted,
        _                        => BypassReasonFallback,
    };

    var record = new UserActionRecord(
        Id: 0,
        UserId: user.Id,
        ActionType: UserActionType.WelcomeBypass,
        MessageId: null,
        ChatId: chat.Id,
        IssuedBy: Actor.WelcomeBypass,
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: null,
        Reason: reason);

    await _userActionsRepo.InsertAsync(record, cancellationToken);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~AuditHandlerTests.LogWelcomeBypass"`
Expected: PASS — all 3 cases green.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/IAuditHandler.cs TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/Handlers/AuditHandlerTests.cs
git commit -F- <<'EOF'
feat: add AuditHandler.LogWelcomeBypassAsync

Writes a user_actions row with ActionType=WelcomeBypass and a reason
string selected from BypassDecision. Reason strings are private
const on AuditHandler since only this class emits them.

EOF
```

---

## Task 9: WelcomeMetrics.RecordBypassOutcome

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs`

- [ ] **Step 1: Add the recording method and labels**

Modify `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs`. Add near the top of the class (with other private constants or at the top):
```csharp
// Outcome labels for bypass-flow results.
// Private — callers use the typed RecordBypassOutcome method below.
private const string OutcomeBypassChatAdmin = "skipped_bypass_chatadmin";
private const string OutcomeBypassWebAdmin  = "skipped_bypass_webadmin";
private const string OutcomeBypassTrusted   = "skipped_bypass_trusted";

// Error messages for the impossible-to-reach switch arms.
private const string BypassOutcomeNoneUnreachable =
    "Reached outcome mapping with BypassDecision.None";
private const string BypassOutcomeUnmappedFormat =
    "Unmapped bypass decision: {0}";
```

Add the recording method (near the existing `RecordWelcomeOutcome` method):
```csharp
/// <summary>
/// Record a welcome bypass with the correct outcome label for the given decision.
/// Throws on None or unmapped decisions — the caller is responsible for guarding the None case upstream.
/// </summary>
public void RecordBypassOutcome(BypassDecision decision, double elapsedMs)
{
    var outcome = decision switch
    {
        BypassDecision.ChatAdmin => OutcomeBypassChatAdmin,
        BypassDecision.WebAdmin  => OutcomeBypassWebAdmin,
        BypassDecision.Trusted   => OutcomeBypassTrusted,
        BypassDecision.None      => throw new InvalidOperationException(BypassOutcomeNoneUnreachable),
        _                        => throw new InvalidOperationException(
                                        string.Format(BypassOutcomeUnmappedFormat, decision)),
    };
    RecordWelcomeOutcome(outcome, elapsedMs);
}
```

Add the using at the top:
```csharp
using TelegramGroupsAdmin.Telegram.Services.Welcome;
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build TelegramGroupsAdmin.Telegram --no-restore 2>&1 | tail -5`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs
git commit -F- <<'EOF'
feat: add WelcomeMetrics.RecordBypassOutcome

Typed recording method that routes BypassDecision → outcome label.
Labels are private const on the class so no caller sees raw strings.
Throws on None (guarded upstream) and unmapped variants.

EOF
```

---

## Task 10: WelcomeService integration — remove Step 1, add Step 2.5, announcement helper

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

- [ ] **Step 1: Write failing tests for the new bypass branch**

Extend `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`. Use existing test fixture setup as a template. Add these test methods (adapt to whatever field names the existing fixture uses — `_bypassResolver`, `_auditHandler`, `_messageService`, `_jobScheduler` etc. must be added to the fixture's SetUp if not already there):

```csharp
[Test]
public async Task HandleChatMemberUpdate_BypassChatAdmin_SkipsSecurityAndConsentFlow()
{
    _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(BypassDecision.ChatAdmin);
    var update = BuildJoinChatMemberUpdate();  // existing helper in the fixture

    await _service.HandleChatMemberUpdateAsync(update, CancellationToken.None);

    await _botRestrictHandler.DidNotReceive().RestrictAsync(Arg.Any<RestrictIntent>(), Arg.Any<CancellationToken>());
    await _casCheckService.DidNotReceive().CheckUserAsync(Arg.Any<UserIdentity>(), Arg.Any<CasConfig>(), Arg.Any<CancellationToken>());
    await _auditHandler.Received(1).LogWelcomeBypassAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), BypassDecision.ChatAdmin, Arg.Any<CancellationToken>());
}

[Test]
public async Task HandleChatMemberUpdate_BypassTrusted_PostsAnnouncementAndSchedulesDelete()
{
    _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(BypassDecision.Trusted);
    var config = new WelcomeConfig
    {
        TrustedBypass = new TrustedBypassConfig
        {
            Enabled = true,
            AnnouncementMessage = "hello {username}",
            AnnouncementTtlSeconds = 30,
        }
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long>()).Returns(config);
    _messageService.SendAndSaveMessageAsync(Arg.Any<long>(), Arg.Any<string>(), parseMode: Arg.Any<ParseMode>(), cancellationToken: Arg.Any<CancellationToken>())
        .Returns(new Message { MessageId = 5001 });

    await _service.HandleChatMemberUpdateAsync(BuildJoinChatMemberUpdate(), CancellationToken.None);

    await _messageService.Received(1).SendAndSaveMessageAsync(
        chatId: Arg.Any<long>(),
        text: Arg.Is<string>(s => s.Contains("hello")),
        parseMode: ParseMode.Html,
        cancellationToken: Arg.Any<CancellationToken>());
    await _jobScheduler.Received(1).ScheduleAsync(
        Arg.Is(BackgroundJobNames.DeleteMessage),
        Arg.Any<DeleteMessageJobPayload>(),
        Arg.Any<DateTimeOffset>(),
        Arg.Any<CancellationToken>());
}

[Test]
public async Task HandleChatMemberUpdate_BypassTrusted_EmptyMessage_DoesNotPostAnnouncement()
{
    _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(BypassDecision.Trusted);
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long>())
        .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true, AnnouncementMessage = "  ", AnnouncementTtlSeconds = 30 } });

    await _service.HandleChatMemberUpdateAsync(BuildJoinChatMemberUpdate(), CancellationToken.None);

    await _messageService.DidNotReceive().SendAndSaveMessageAsync(
        Arg.Any<long>(), Arg.Any<string>(), parseMode: Arg.Any<ParseMode>(), cancellationToken: Arg.Any<CancellationToken>());
    await _jobScheduler.DidNotReceive().ScheduleAsync(
        Arg.Any<string>(), Arg.Any<object>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
}

[Test]
public async Task HandleChatMemberUpdate_BypassTrusted_ZeroTtl_DoesNotPostAnnouncement()
{
    _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(BypassDecision.Trusted);
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long>())
        .Returns(new WelcomeConfig { TrustedBypass = new TrustedBypassConfig { Enabled = true, AnnouncementMessage = "x", AnnouncementTtlSeconds = 0 } });

    await _service.HandleChatMemberUpdateAsync(BuildJoinChatMemberUpdate(), CancellationToken.None);

    await _messageService.DidNotReceive().SendAndSaveMessageAsync(
        Arg.Any<long>(), Arg.Any<string>(), parseMode: Arg.Any<ParseMode>(), cancellationToken: Arg.Any<CancellationToken>());
}

[Test]
public async Task HandleChatMemberUpdate_PreBanned_BeatsBypass_BansAndReturns()
{
    _bypassResolver.ResolveAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
        .Returns(BypassDecision.Trusted);  // Trusted would normally bypass...
    _telegramUserRepo.GetOrCreateAsync(Arg.Any<UserIdentity>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
        .Returns(new TelegramUser { Identity = UserIdentity.FromId(1), IsBanned = true });  // ...but user is globally pre-banned.

    await _service.HandleChatMemberUpdateAsync(BuildJoinChatMemberUpdate(), CancellationToken.None);

    await _bypassResolver.DidNotReceive().ResolveAsync(
        Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
    await _moderationService.Received().SyncBanToChatAsync(Arg.Any<SyncBanIntent>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeServiceTests.HandleChatMemberUpdate_Bypass" --no-build 2>&1 | tail -20`
Expected: FAIL — bypass logic doesn't exist yet.

- [ ] **Step 3: Modify WelcomeService**

Modify `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`:

**3a. Add class-top error-message constants** (near any existing `private const` fields or at the top of the class):
```csharp
// Error messages for the impossible-to-reach switch arms in bypass outcome mapping.
private const string BypassOutcomeNoneUnreachable =
    "Reached outcome mapping with BypassDecision.None";
```

**3b. Inject new dependencies.** Find the existing primary constructor or fields list. Add:
- `IWelcomeBypassResolver bypassResolver` — new
- `IAuditHandler auditHandler` — new if not already injected
- `IJobScheduler jobScheduler` — new if not already injected

**3c. Delete the existing Step 1 admin skip block.** Locate the existing block (was around line 134-146):
```csharp
// Step 1: Check if user is an admin/owner - skip all checks for admins
var chatMember = await userService.GetChatMemberAsync(chatMemberUpdate.Chat.Id, user.Id, cancellationToken);
if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
{
    logger.LogInformation(
        "Skipping welcome for admin/owner: {User} in {Chat}",
        user.ToLogInfo(),
        chatMemberUpdate.Chat.ToLogInfo());
    welcomeMetrics.RecordWelcomeOutcome("skipped_admin", Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    return;
}
```

Remove it entirely — the resolver now handles this in Rule 1.

**3d. Insert the new Step 2.5 bypass block.** Immediately after the pre-banned early-out (the `existingUser.IsBanned` check, formerly Step 2) and before Step 3 (RestrictUserPermissionsAsync), insert:

```csharp
// Step 2.5: Unified privileged/trusted bypass (chat admin, web admin, or trusted user).
var bypassDecision = await bypassResolver.ResolveAsync(
    UserIdentity.From(user), ChatIdentity.From(chatMemberUpdate.Chat), cancellationToken);
if (bypassDecision != BypassDecision.None)
{
    await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);
    await auditHandler.LogWelcomeBypassAsync(
        UserIdentity.From(user), ChatIdentity.From(chatMemberUpdate.Chat),
        bypassDecision, cancellationToken);
    await PostBypassAnnouncementIfConfiguredAsync(
        chatMemberUpdate.Chat, user, config, cancellationToken);

    welcomeMetrics.RecordBypassOutcome(bypassDecision,
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    return;
}
```

**3e. Add the private announcement helper** (at the bottom of the class, near other private helpers):
```csharp
private async Task PostBypassAnnouncementIfConfiguredAsync(
    Chat chat, User user, WelcomeConfig config, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(config.TrustedBypass.AnnouncementMessage))
        return;
    if (config.TrustedBypass.AnnouncementTtlSeconds <= 0)
        return;

    var text = config.TrustedBypass.AnnouncementMessage
        .Replace(TrustedBypassConfig.UsernameVariable, TelegramDisplayName.FormatMention(user))
        .Replace(TrustedBypassConfig.ChatNameVariable, chat.Title ?? string.Empty);

    var announcement = await messageService.SendAndSaveMessageAsync(
        chatId: chat.Id,
        text: text,
        parseMode: ParseMode.Html,
        cancellationToken: cancellationToken);

    await jobScheduler.ScheduleAsync(
        BackgroundJobNames.DeleteMessage,
        new DeleteMessageJobPayload(chat.Id, announcement.MessageId),
        runAt: DateTimeOffset.UtcNow.AddSeconds(config.TrustedBypass.AnnouncementTtlSeconds),
        cancellationToken: cancellationToken);
}
```

**3f. Add usings** at the top of WelcomeService.cs:
```csharp
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Core.BackgroundJobs;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeServiceTests"`
Expected: PASS — new 5 bypass tests plus all existing tests unchanged.

- [ ] **Step 5: Full UnitTests project sanity check**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --no-build`
Expected: all existing tests still pass (regression guard).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs
git commit -F- <<'EOF'
feat: unify privileged bypass paths in WelcomeService

Remove the existing silent Step 1 admin/creator skip and fold its
responsibility into the new unified Step 2.5 resolver-driven bypass.
Chat admins, linked web admins, and trusted users now all run through
the same audit-entry + announcement + metric flow. Behavior parity
for all three paths; only the audit reason and metric label differ.

EOF
```

---

## Task 11: UI — new Trusted User Bypass expansion panel

**Files:**
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor`

- [ ] **Step 1: Add the new expansion panel**

Modify `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor`. Find the end of the "Security on Join" section's `<MudExpansionPanels>` block. Below that section (but inside the same `MudGrid`), add:

```razor
@* Trusted User Bypass Section *@
<MudItem xs="12">
    <MudDivider Class="my-2" />
    <MudText Typo="Typo.subtitle2" Class="mb-2">
        <MudIcon Icon="@Icons.Material.Filled.VerifiedUser" Size="Size.Small" Class="mr-1" />
        Trusted User Bypass
    </MudText>
    <MudText Typo="Typo.caption" Class="mud-text-secondary mb-3">
        When enabled, trusted users skip the welcome flow and all security checks.
        Linked web administrators (Owner, GlobalAdmin) and Telegram chat admins
        always bypass regardless of this toggle.
    </MudText>
</MudItem>

<MudItem xs="12">
    <MudExpansionPanels>
        <MudExpansionPanel Expanded="false">
            <TitleContent>
                <div class="d-flex align-center gap-2">
                    <MudSwitch @bind-Value="_config.TrustedBypass.Enabled"
                               Color="Color.Primary"
                               @onclick:stopPropagation="true" />
                    <MudText>Trusted User Bypass</MudText>
                    <MudChip T="string" Size="Size.Small"
                             Color="@(_config.TrustedBypass.Enabled ? Color.Success : Color.Default)">
                        @(_config.TrustedBypass.Enabled ? "Enabled" : "Disabled")
                    </MudChip>
                </div>
            </TitleContent>
            <ChildContent>
                <MudText Typo="Typo.caption" Class="mud-text-secondary mb-3">
                    An auto-deleting announcement is posted in the chat each time a user is
                    bypassed — whether trusted, a linked web admin, or a chat admin — so other
                    admins know this user was auto-admitted.
                </MudText>
                <MudGrid>
                    <MudItem xs="12">
                        <MudTextField @bind-Value="_config.TrustedBypass.AnnouncementMessage"
                                      Label="Announcement Message"
                                      Lines="3"
                                      Disabled="!_config.TrustedBypass.Enabled"
                                      HelperText="@($"Variables: {TrustedBypassConfig.UsernameVariable}, {TrustedBypassConfig.ChatNameVariable}")" />
                    </MudItem>
                    <MudItem xs="12" md="4">
                        <MudNumericField T="int"
                                         @bind-Value="_config.TrustedBypass.AnnouncementTtlSeconds"
                                         Label="Auto-delete after (seconds)"
                                         Min="10"
                                         Max="300"
                                         Disabled="!_config.TrustedBypass.Enabled"
                                         HelperText="10-300 seconds" />
                    </MudItem>
                </MudGrid>
            </ChildContent>
        </MudExpansionPanel>
    </MudExpansionPanels>
</MudItem>
```

- [ ] **Step 2: Ensure the using for TrustedBypassConfig is present**

Confirm the `@using TelegramGroupsAdmin.Configuration.Models.Welcome` directive is already at the top of the file (it should be — it's where `WelcomeConfig` lives).

- [ ] **Step 3: Verify compile**

Run: `dotnet build TelegramGroupsAdmin --no-restore 2>&1 | tail -10`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor
git commit -F- <<'EOF'
feat(ui): add Trusted User Bypass panel to Welcome System config

New expansion panel below Security on Join with toggle, editable
announcement message, and configurable 10-300s TTL. Helper text
shows the {username} / {chat_name} variables via public const
references on TrustedBypassConfig. Fields are disabled when toggle
is off, matching the CAS/Impersonation/Blacklist pattern.

EOF
```

---

## Task 12: Component tests for the new UI panel

**Files:**
- Modify: `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs`

- [ ] **Step 1: Write failing tests**

Extend `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs` with:

```csharp
[Test]
public void TrustedBypassPanel_Renders_WhenConfigLoaded()
{
    var config = new WelcomeConfig();
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long?>()).Returns(config);

    var cut = RenderComponent<WelcomeSystemConfig>();

    Assert.That(cut.Markup, Does.Contain("Trusted User Bypass"));
}

[Test]
public void TrustedBypassPanel_FieldsDisabled_WhenToggleOff()
{
    var config = new WelcomeConfig
    {
        TrustedBypass = new TrustedBypassConfig { Enabled = false }
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long?>()).Returns(config);

    var cut = RenderComponent<WelcomeSystemConfig>();

    var disabledInputs = cut.FindAll("input[disabled], textarea[disabled]");
    Assert.That(disabledInputs.Count, Is.GreaterThan(0));
}

[Test]
public async Task Save_PersistsTrustedBypass_ThroughConfigService()
{
    var config = new WelcomeConfig
    {
        TrustedBypass = new TrustedBypassConfig
        {
            Enabled = true,
            AnnouncementMessage = "custom message",
            AnnouncementTtlSeconds = 45,
        }
    };
    _configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, Arg.Any<long?>()).Returns(config);

    var cut = RenderComponent<WelcomeSystemConfig>();
    // Trigger the save flow — adapt to however the existing tests trigger save.
    await cut.InvokeAsync(() => cut.Instance.SaveAsync());

    await _configService.Received(1).SetAsync<WelcomeConfig>(
        ConfigType.Welcome,
        Arg.Is<WelcomeConfig>(c =>
            c.TrustedBypass.Enabled == true &&
            c.TrustedBypass.AnnouncementMessage == "custom message" &&
            c.TrustedBypass.AnnouncementTtlSeconds == 45),
        Arg.Any<long?>(),
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run tests to verify they pass (UI already exists from Task 11)**

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~WelcomeSystemConfigTests" --no-build`
Expected: PASS — 3 new tests green.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -F- <<'EOF'
test(ui): add bUnit coverage for Trusted User Bypass panel

Verifies the new panel renders, fields are disabled when the toggle
is off, and saving propagates the nested TrustedBypass config
through ConfigService.SetAsync.

EOF
```

---

## Task 13: SQL fixture for linked web admin seed

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/07_base_telegram_user_mappings.sql`

- [ ] **Step 1: Inspect the telegram_user_mappings schema**

Run: `grep -n "telegram_user_mappings" TelegramGroupsAdmin.Data/AppDbContext.cs | head -10`

Read enough of `AppDbContext.cs` to confirm column names, FK structure, and the `is_active` column. Expected columns based on the spec: `id` (PK), `user_id` (FK to users.id), `telegram_user_id` (FK to telegram_users.telegram_user_id), `is_active` (bool), `created_at` (timestamptz).

- [ ] **Step 2: Create the SQL fixture**

Create `TelegramGroupsAdmin.IntegrationTests/TestData/SQL/07_base_telegram_user_mappings.sql`:
```sql
-- Link the test Owner web user (user-owner-1) from 01_base_web_users.sql to
-- the test Telegram user (id 100) from 00_base_telegram_users.sql.
-- Used by WelcomeFlowBypassIntegrationTests to simulate a linked web admin joining a chat.
INSERT INTO telegram_user_mappings (user_id, telegram_user_id, is_active, created_at)
VALUES ('user-owner-1', 100, true, NOW())
ON CONFLICT DO NOTHING;
```

Note: if the test base-data web user ID differs (check `01_base_web_users.sql`), substitute the correct one.

- [ ] **Step 3: Verify the fixture runs**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ExistingAnyIntegrationTest" --no-build 2>&1 | tail -10`

Just run any currently-passing integration test to confirm the fixture loads (it's loaded by filename order via the existing fixture-loader).

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/TestData/SQL/07_base_telegram_user_mappings.sql
git commit -F- <<'EOF'
test: add SQL fixture seeding a linked web admin mapping

07_base_telegram_user_mappings.sql links test Owner web user to test
Telegram user id 100. Used by the welcome bypass integration tests to
exercise the WebAdmin decision path.

EOF
```

---

## Task 14: Integration tests — full welcome flow with bypass

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`

- [ ] **Step 1: Write the tests**

Create `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Services;

[TestFixture]
public class WelcomeFlowBypassIntegrationTests : IntegrationTestBase  // or whatever base class existing integration tests use
{
    private IWelcomeService _welcomeService = null!;
    private IConfigService _configService = null!;
    private AppDbContext _db = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        await ResetDatabaseAsync();
        await SeedBaseFixturesAsync();  // loads 00..07 sql files

        _welcomeService = Services.GetRequiredService<IWelcomeService>();
        _configService = Services.GetRequiredService<IConfigService>();
        _db = Services.GetRequiredService<AppDbContext>();
    }

    [Test]
    public async Task ChatAdminJoin_WritesAuditRow_PostsAnnouncement_NoWelcomeResponse()
    {
        // Pre-req: test telegram user 100 is configured as chat admin in the test managed chat (in 02_base_managed_chats.sql or via test harness).
        var update = BuildChatMemberUpdatedForJoin(
            telegramUserId: 100,   // chat admin in this chat
            chatId: -1000);

        await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        var auditRows = await _db.UserActions
            .Where(a => a.TelegramUserId == 100 && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(auditRows, Has.Count.EqualTo(1));
        Assert.That(auditRows[0].SystemIdentifier, Is.EqualTo("welcome_bypass"));

        var welcomeResponses = await _db.WelcomeResponses
            .Where(w => w.TelegramUserId == 100 && w.ChatId == -1000)
            .ToListAsync();
        Assert.That(welcomeResponses, Is.Empty, "No welcome_responses row should be written for bypass");
    }

    [Test]
    public async Task WebAdminJoin_LinkedOwner_WritesAuditAndPostsAnnouncement()
    {
        // Pre-req: fixture 07_base_telegram_user_mappings.sql links user-owner-1 <-> tg 100.
        var update = BuildChatMemberUpdatedForJoin(
            telegramUserId: 100,
            chatId: -1000);

        await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        var auditRows = await _db.UserActions
            .Where(a => a.TelegramUserId == 100 && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(auditRows, Has.Count.EqualTo(1));
        // Reason string distinguishes ChatAdmin vs WebAdmin cases. If the test user is BOTH a chat admin and a linked owner,
        // the ChatAdmin path wins (priority 1). Adjust test setup so only one role applies per test case.
    }

    [Test]
    public async Task TrustedUserJoin_ToggleOn_Bypasses_CreatesAuditAndAnnouncement()
    {
        // Seed: make user 200 trusted + enable bypass in the global welcome config.
        await _db.TelegramUsers.Where(u => u.TelegramUserId == 200).ExecuteUpdateAsync(
            s => s.SetProperty(u => u.IsTrusted, true));

        var config = new WelcomeConfig
        {
            Enabled = true,
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessage = "welcome back {username}",
                AnnouncementTtlSeconds = 30,
            }
        };
        await _configService.SetAsync(ConfigType.Welcome, config, chatId: null, CancellationToken.None);

        var update = BuildChatMemberUpdatedForJoin(telegramUserId: 200, chatId: -1000);

        await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        var auditRows = await _db.UserActions
            .Where(a => a.TelegramUserId == 200 && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(auditRows, Has.Count.EqualTo(1));
        Assert.That(auditRows[0].Reason, Is.EqualTo("Trusted user, bypass enabled"));

        var messages = await _db.Messages
            .Where(m => m.ChatId == -1000 && m.MessageText != null && m.MessageText.Contains("welcome back"))
            .ToListAsync();
        Assert.That(messages, Is.Not.Empty, "Announcement message should exist in messages table");
    }

    [Test]
    public async Task PreBannedTrustedUser_DoesNotBypass_StillBanned()
    {
        // Seed: user 200 is trusted AND is_banned = true.
        await _db.TelegramUsers.Where(u => u.TelegramUserId == 200).ExecuteUpdateAsync(
            s => s.SetProperty(u => u.IsTrusted, true).SetProperty(u => u.IsBanned, true));

        var config = new WelcomeConfig
        {
            TrustedBypass = new TrustedBypassConfig { Enabled = true }
        };
        await _configService.SetAsync(ConfigType.Welcome, config, chatId: null, CancellationToken.None);

        var update = BuildChatMemberUpdatedForJoin(telegramUserId: 200, chatId: -1000);

        await _welcomeService.HandleChatMemberUpdateAsync(update, CancellationToken.None);

        var auditRows = await _db.UserActions
            .Where(a => a.TelegramUserId == 200 && a.ActionType == (int)UserActionType.WelcomeBypass)
            .ToListAsync();
        Assert.That(auditRows, Is.Empty, "Pre-banned user must not produce a bypass audit row");
    }

    private static ChatMemberUpdated BuildChatMemberUpdatedForJoin(long telegramUserId, long chatId)
    {
        var user = new Telegram.Bot.Types.User { Id = telegramUserId, FirstName = "Test" };
        return new ChatMemberUpdated
        {
            Chat = new Chat { Id = chatId, Title = "Test Chat", Type = ChatType.Supergroup },
            From = user,
            Date = DateTime.UtcNow,
            OldChatMember = new ChatMemberLeft { User = user },
            NewChatMember = new ChatMemberMember { User = user },
        };
    }
}
```

- [ ] **Step 2: Run integration tests (in background — ~20 min for full suite)**

Per TGA convention, run tests in background with file output:
```bash
dotnet test TelegramGroupsAdmin.IntegrationTests \
    --filter "FullyQualifiedName~WelcomeFlowBypassIntegrationTests" \
    --logger "console;verbosity=detailed" \
    > /tmp/bypass-integration-test.log 2>&1 &
```

Then poll for completion. Expected: PASS — all 4 scenarios.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs
git commit -F- <<'EOF'
test(integration): add welcome-flow bypass coverage with real Postgres

Tests chat-admin, web-admin (via linked mapping), trusted-user, and
pre-banned-trusted ordering-invariant scenarios. Asserts audit rows
in user_actions, announcement rows in messages, and absence of
welcome_responses rows for bypass cases.

EOF
```

---

## Task 15: TestWelcomeConfigBuilder extension

**Files:**
- Modify: `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWelcomeConfigBuilder.cs`

- [ ] **Step 1: Add the WithTrustedBypass helper**

Modify `TelegramGroupsAdmin.E2ETests/Infrastructure/TestWelcomeConfigBuilder.cs`. Add a method to the builder class:

```csharp
public TestWelcomeConfigBuilder WithTrustedBypass(
    bool enabled = true,
    string? message = null,
    int ttlSeconds = 30)
{
    _config.TrustedBypass = new TrustedBypassConfig
    {
        Enabled = enabled,
        AnnouncementMessage = message ?? "{username} bypassed.",
        AnnouncementTtlSeconds = ttlSeconds,
    };
    return this;
}
```

Add the using if not present:
```csharp
using TelegramGroupsAdmin.Configuration.Models.Welcome;
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build TelegramGroupsAdmin.E2ETests --no-restore 2>&1 | tail -5`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.E2ETests/Infrastructure/TestWelcomeConfigBuilder.cs
git commit -F- <<'EOF'
test: add WithTrustedBypass helper to TestWelcomeConfigBuilder

Lets future E2E or integration tests set up bypass config fluently
without repeating the nested-object initializer.

EOF
```

---

## Task 16: Full test suite regression check

- [ ] **Step 1: Run the full test suite in the background**

Per project convention (long runtime, avoid pipes):
```bash
dotnet test --no-build > /tmp/full-test-run.log 2>&1 &
echo "Started; poll with: tail -f /tmp/full-test-run.log"
```

- [ ] **Step 2: When complete, verify zero failures**

```bash
grep -E "Passed|Failed|Total" /tmp/full-test-run.log | tail -10
```
Expected: `Failed: 0` across all projects.

If any test failed, stop and debug before proceeding — do not mark this task complete until everything is green.

- [ ] **Step 3: No commit needed** (this is a verification step, not a code change)

---

## Task 17: File GitHub issue for follow-up admin-on-admin moderation work

- [ ] **Step 1: Draft the issue**

Use the heredoc in Step 2 for the issue body.

- [ ] **Step 2: File the issue**

```bash
gh issue create --title "feat: admin-on-admin moderation with hierarchy, confirmation UX, and bot-permission gating" --label feature --label moderation --label backend --label frontend --body "$(cat <<'EOF'
## Summary

Introduce admin-on-admin moderation with hierarchy semantics, explicit confirmation UX, and gating on the bot's promotion permissions. Closes a gap surfaced during the Trusted User Bypass feature design (see `docs/superpowers/specs/2026-04-17-trusted-user-bypass-welcome-design.md`).

## Problem

Today `/ban`, `/spam`, `/tempban`, and the Blazor admin UI allow any sufficiently-privileged actor to moderate any user, with no guard against acting on another administrator. This creates two problems:

1. Accidental actions on protected users.
2. No structured flow for a higher-level admin to legitimately police a lower-level admin.

## Scope

- **Respect admin hierarchy**:
  - Owner may moderate GlobalAdmin and Admin
  - GlobalAdmin may moderate Admin
  - No actor may moderate an equal-or-higher level
- **Confirmation UX in both surfaces**:
  - In chat: inline keyboard with Confirm / Cancel buttons on a bot-posted message
  - In the Blazor UI: MudBlazor confirmation dialog
- **Bot capability gate**:
  - Gate the feature on the bot holding the \`can_promote_members\` (or equivalent demote/promote) permission in the target chat
  - If absent, surface a clear error up front rather than letting the Telegram API call fail later
- **Readable rejection**:
  - When a lower-level admin attempts action on an equal-or-higher target, block with a readable message (e.g., \"You cannot moderate a GlobalAdmin\")

## Identity primitives (already in codebase)

- Web-admin lookup: \`ITelegramUserMappingRepository.GetPermissionLevelByTelegramIdAsync\`
- Telegram chat-admin check: \`IBotUserService.GetChatMemberAsync\`

## Out of scope

- Audit-log changes beyond the standard ban/warn entries
- Hierarchy changes to non-moderation commands (trust/untrust are out)

## Related

- Originating design: \`docs/superpowers/specs/2026-04-17-trusted-user-bypass-welcome-design.md\`
EOF
)"
```

- [ ] **Step 3: No commit needed** (issue filing is out-of-tree)

---

## Task 18: Verify Grafana dashboard label migration (owner-only, no code change)

- [ ] **Step 1: Inspect existing Grafana dashboards**

This task is for the repo owner running the production TGA instance. Check any dashboard or alert that filters on `welcome_outcome{outcome="skipped_admin"}`. Update those queries to `outcome="skipped_bypass_chatadmin"`.

- [ ] **Step 2: No code commit** (dashboard JSON lives outside the repo)

If this is tracked in-repo somewhere (e.g., an embedded JSON under `ops/grafana/`), search and update:
```bash
grep -rn "skipped_admin" ops/ 2>/dev/null || echo "No tracked dashboards reference the old label"
```

If grep finds matches, patch them in a separate small commit.

---

## Task 19: Smoke test manually

- [ ] **Step 1: Build and run with migrations**

```bash
dotnet run --project TelegramGroupsAdmin --migrate-only
```
Verify no migration is triggered (JSONB config is schema-flexible — confirms Task 4 was correctly modelled).

- [ ] **Step 2: Run the app against a test chat**

Only the owner has credentials to do this. Enable the Trusted User Bypass toggle in the global welcome config, have a trusted Telegram user join the test chat, and verify:
- The user is not restricted
- An announcement appears in chat mentioning them
- The announcement disappears after the configured TTL
- An audit row appears on the Audit page with `UserActionType = WelcomeBypass`
- The metric `welcome_outcome{outcome="skipped_bypass_trusted"}` increments in Prometheus

- [ ] **Step 3: No commit needed**

---

## Task 20: Create the pull request

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat/trusted-user-bypass-welcome
```

- [ ] **Step 2: Create the PR**

```bash
gh pr create --base develop --title "feat: trusted user bypass for welcome system" --body "$(cat <<'EOF'
## Summary

- Add opt-in per-chat capability to bypass the welcome flow for trusted users, linked web admins (Owner / GlobalAdmin), and Telegram chat admins — all through a unified `IWelcomeBypassResolver` service.
- Each bypass produces an audit-log row in `user_actions` and an auto-deleting in-chat announcement so admins and members see the auto-admission.
- Extract the 19 system-actor ID strings scattered across `Actor.cs` into a new `SystemActorIds` constants class while we're editing the file.

See also **#411** (`refactor: Simplify WelcomeService join security pipeline`) — this PR inserts a new Step 2.5 in the method #411 plans to reorganize.

## Design

Full design document: `docs/superpowers/specs/2026-04-17-trusted-user-bypass-welcome-design.md`

Key behavioral change from today: the existing silent skip for Telegram chat admins at `WelcomeService.cs:138` is retired. Chat admin joins now generate audit rows and announcements just like web admin and trusted bypasses. The `skipped_admin` metric label is replaced by `skipped_bypass_chatadmin`.

## Test plan

- [ ] Unit tests for `TrustedBypassConfig`, `WelcomeBypassResolver` (9-case matrix), `AuditHandler.LogWelcomeBypassAsync`, `WelcomeService` bypass branches, `SystemActorIds` consistency
- [ ] Integration tests for the full welcome flow with real Postgres covering chat-admin, web-admin, trusted-user, and pre-banned-trusted scenarios
- [ ] bUnit component tests for the new UI expansion panel
- [ ] Manual smoke test against a test chat (owner)
- [ ] Grafana dashboard label migration (`skipped_admin` → `skipped_bypass_chatadmin`) — owner-only follow-up

EOF
)"
```

- [ ] **Step 3: Return the PR URL to the user**

---

## Self-Review Checklist

Run this once all 20 tasks are complete to verify the plan covered the spec:

1. **Spec coverage:**
   - ✓ TrustedBypassConfig (Spec §1) → Task 3 creates, Task 4 wires
   - ✓ Bypass resolver service (Spec §2) → Tasks 5-7 create interface, enum, implementation, DI
   - ✓ WelcomeService flow + audit + announcement (Spec §3) → Tasks 8-10 (audit, metrics, service integration)
   - ✓ Audit log integration (Spec §4) → Task 2 enum, Task 8 handler method
   - ✓ SystemActorIds refactor (Spec §5) → Task 1
   - ✓ UI panel (Spec §6) → Task 11
   - ✓ Testing (Spec §7) → Tasks 3, 4, 6, 8, 12, 13, 14, 15 inline with TDD
   - ✓ Observability (Spec §8) → Task 9 metrics method, Task 18 dashboard migration

2. **Placeholder scan:** No "TBD"/"TODO" in the plan body. Two places reference existing test fixtures' helpers (e.g., `BuildJoinChatMemberUpdate`) — those are real helpers you should already see when opening the files.

3. **Type consistency:**
   - `BypassDecision` enum values: `None`, `ChatAdmin`, `WebAdmin`, `Trusted` — used consistently across Tasks 5-10
   - Method signature `LogWelcomeBypassAsync(UserIdentity, ChatIdentity, BypassDecision, CancellationToken)` — Task 8 defines, Task 10 tests
   - `RecordBypassOutcome(BypassDecision, double)` — Task 9 defines, Task 10 uses
   - `TrustedBypassConfig.UsernameVariable` / `ChatNameVariable` / `DefaultAnnouncementMessage` / `DefaultAnnouncementTtlSeconds` — Task 3 defines, Tasks 4, 10, 11 reference

---

## Execution Notes

- Worktree: already on branch `feat/trusted-user-bypass-welcome` (created by brainstorming skill).
- Commits so far: 7 (spec docs). Subsequent tasks add feature commits.
- Test-run convention: run full suite in background via `dotnet test > /tmp/<name>.log 2>&1 &` (the full suite runs ~20 minutes; do not block the session on it).
- Don't run the app normally — use `dotnet run --migrate-only` for validation (Telegram singleton constraint).
- No emojis in code (global project rule).
