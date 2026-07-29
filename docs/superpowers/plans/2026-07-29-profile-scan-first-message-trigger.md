# Profile Scan First-Message Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Untrusted users whose first observed activity is a message get a profile scan, closing the gap that let 20 of 20 recently-banned spam accounts through unscanned.

**Architecture:** Introduce `IProfileScanGate` as the single owner of the scan-eligibility decision. The three automatic triggers (join, first message, profile change) all route through it; the two admin-initiated paths (UI rescan, bulk rescan job) keep calling `IProfileScanService` directly and intentionally bypass the gate. The first-message scan runs inline and awaited, matching the join path exactly.

**Tech Stack:** .NET 10, C# with primary constructors and `extension` blocks, NUnit + NSubstitute, Testcontainers.PostgreSQL, MudBlazor 9, EF Core 10 (JSONB config, no migration needed).

**Spec:** `docs/superpowers/specs/2026-07-29-profile-scan-first-message-trigger-design.md`

## Global Constraints

- Branch is `fix/profile-scan-missing-on-first-message`, based on `develop`. Never commit to `master` or `develop`.
- Conventional commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`). Granular commits, never squash.
- No emojis in code, commits, or PR text.
- Named arguments for `bool` parameters and for `CancellationToken`.
- Always `DateTimeOffset` UTC for date/time values.
- Services reach the database through repositories, never `DbContext` directly.
- Prefer enums over magic strings.
- Data-layer models stay repo-internal; services accept and return Core/UI models.
- `ProfileScanConfig.ScanOnFirstMessage` defaults to `false`. Do not change that default.
- Run `dotnet test` in Debug locally, without pipes (pipes hide failures).
- Telegram.Bot types (`Message`, `User`, `Chat`) are concrete with non-virtual properties. Never `Substitute.For<Message>()`; use object initializers or `TelegramTestFactory`.

---

### Task 1: Add `ScanOnFirstMessage` config flag

Adds the flag end to end (model, data DTO, both mapping directions, UI switch) so later tasks have something to gate on. Default `false` means the absent JSONB key in the existing `configs` row deserializes to off, with no DB migration.

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`
- Modify: `TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs`
- Modify: `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs:149-175`
- Modify: `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:180-185`
- Test: `TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ProfileScanConfig.ScanOnFirstMessage` (`bool`, default `false`) and `ProfileScanConfigData.ScanOnFirstMessage` (`bool`, default `false`). Task 2 reads the former.

- [ ] **Step 1: Write the failing tests**

Add to `WelcomeConfigMappingsTests.cs`. Match the existing fixture's naming and assertion style in that file.

```csharp
[Test]
public void ProfileScanConfigData_ToModel_MapsScanOnFirstMessage()
{
    var data = new ProfileScanConfigData { ScanOnFirstMessage = true };

    var model = data.ToModel();

    Assert.That(model.ScanOnFirstMessage, Is.True);
}

[Test]
public void ProfileScanConfig_ToData_MapsScanOnFirstMessage()
{
    var model = new ProfileScanConfig { ScanOnFirstMessage = true };

    var data = model.ToData();

    Assert.That(data.ScanOnFirstMessage, Is.True);
}

[Test]
public void ProfileScanConfig_ScanOnFirstMessage_DefaultsToFalse()
{
    // Default-off rollout: the absent scanOnFirstMessage key in existing
    // configs rows must deserialize to disabled.
    Assert.Multiple(() =>
    {
        Assert.That(new ProfileScanConfig().ScanOnFirstMessage, Is.False);
        Assert.That(new ProfileScanConfigData().ScanOnFirstMessage, Is.False);
    });
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests"`

Expected: compile error, `ScanOnFirstMessage` does not exist on `ProfileScanConfig` / `ProfileScanConfigData`.

- [ ] **Step 3: Add the property to both types**

In `ProfileScanConfig.cs`, after the `ScanOnProfileChange` property:

```csharp
    /// <summary>
    /// Whether to scan a user's profile on their first message when they have
    /// never been scanned. Covers users who arrive without a join event, such as
    /// accounts commenting on channel posts in a linked discussion group.
    /// </summary>
    public bool ScanOnFirstMessage { get; set; } = false;
```

In `ProfileScanConfigData.cs`, after `ScanOnProfileChange`:

```csharp
    public bool ScanOnFirstMessage { get; set; } = false;
```

- [ ] **Step 4: Add both mapping directions**

In `WelcomeConfigMappings.cs`, add one line to each of the two initializers. In `extension(ProfileScanConfigData data)` → `ToModel()`, after `ScanOnProfileChange = data.ScanOnProfileChange,`:

```csharp
            ScanOnFirstMessage = data.ScanOnFirstMessage,
```

In `extension(ProfileScanConfig model)` → `ToData()`, after `ScanOnProfileChange = model.ScanOnProfileChange,`:

```csharp
            ScanOnFirstMessage = model.ScanOnFirstMessage,
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeConfigMappingsTests"`

Expected: PASS.

- [ ] **Step 6: Add the UI switch**

In `WelcomeSystemConfig.razor`, insert a new `MudItem` immediately after the `ScanOnProfileChange` switch block (which ends at line 185):

```razor
                                    <MudItem xs="6">
                                        <MudSwitch @bind-Value="_config.JoinSecurity.ProfileScan.ScanOnFirstMessage"
                                                   Color="Color.Primary"
                                                   Label="Scan on first message"
                                                   Disabled="!_config.JoinSecurity.ProfileScan.Enabled" />
                                    </MudItem>
```

- [ ] **Step 7: Add the component test for the switch**

Open `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs` and find the
existing test that asserts on the `ScanOnJoin` or `ScanOnProfileChange` switch. Add a sibling
test in the same style, rendering the component and asserting the new switch is present and
respects the master toggle:

```csharp
[Test]
public void WelcomeSystemConfig_ProfileScanEnabled_RendersScanOnFirstMessageSwitch()
{
    var cut = RenderComponent(profileScanEnabled: true);

    Assert.That(cut.Markup, Does.Contain("Scan on first message"));
}

[Test]
public void WelcomeSystemConfig_ProfileScanDisabled_DisablesScanOnFirstMessageSwitch()
{
    var cut = RenderComponent(profileScanEnabled: false);

    var switchInput = cut.FindAll("input").First(i =>
        i.GetAttribute("aria-label") == "Scan on first message"
        || i.ParentElement?.TextContent.Contains("Scan on first message") == true);

    Assert.That(switchInput.HasAttribute("disabled"), Is.True);
}
```

Adapt `RenderComponent` and the element lookup to that fixture's existing helpers and MudBlazor
9 markup. Read the neighbouring `ScanOnJoin` test first and mirror it rather than inventing a
new approach; if the fixture has no equivalent switch test, assert on markup only and drop the
second test.

- [ ] **Step 8: Verify the project builds and component tests pass**

Run: `dotnet build TelegramGroupsAdmin`

Expected: build succeeds, no warnings introduced.

Run: `dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~WelcomeSystemConfigTests"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs \
        TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs \
        TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs \
        TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor \
        TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs \
        TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs
git commit -m "feat(config): add ScanOnFirstMessage profile scan flag, default off"
```

---

### Task 2: Create `IProfileScanGate`

The gate owns the entire eligibility decision. It does not delegate to a helper predicate, and it does not swallow exceptions.

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanTrigger.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScanGate.cs`
- Create: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanGate.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs:72`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanGateTests.cs`

**Interfaces:**
- Consumes: `ProfileScanConfig.ScanOnFirstMessage` from Task 1.
- Produces:
  - `enum ProfileScanTrigger { Join, FirstMessage, ProfileChange }`
  - `IProfileScanGate.ScanIfEligibleAsync(UserIdentity user, ChatIdentity? chat, ProfileScanTrigger trigger, CancellationToken ct)` returning `Task<ProfileScanResult?>`, null when skipped.

  Tasks 3, 4, and 5 all call `ScanIfEligibleAsync`.

Existing signatures this task depends on, already in the codebase:
- `IConfigService.GetEffectiveWelcomeAsync(long chatId, CancellationToken ct = default)` returning `ValueTask<WelcomeConfig?>`
- `ITelegramUserRepository.GetByTelegramIdAsync(long telegramUserId, CancellationToken cancellationToken = default)` returning `Task<TelegramUser?>`
- `ITelegramSessionManager.HasAnyActiveSessionAsync(CancellationToken ct)` returning `Task<bool>`
- `IProfileScanService.ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct)` returning `Task<ProfileScanResult>`
- `PipelineMetrics.RecordProfileScanSkipped(string reason)`

- [ ] **Step 1: Write the failing tests**

Create `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanGateTests.cs`. Assertions are on the gate's return value, not on mock call counts, because the contract is "null when skipped, result when scanned".

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Unit tests for ProfileScanGate, the single owner of the profile scan
/// eligibility decision shared by the join, first-message, and profile-change
/// triggers.
/// </summary>
[TestFixture]
public class ProfileScanGateTests
{
    private const long TestUserId = 8872589479L;
    private const long TestChatId = -1001329174109L;

    private IConfigService _configService = null!;
    private ITelegramUserRepository _userRepository = null!;
    private ITelegramSessionManager _sessionManager = null!;
    private IProfileScanService _profileScanService = null!;
    private ProfileScanGate _gate = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _userRepository = Substitute.For<ITelegramUserRepository>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();
        _profileScanService = Substitute.For<IProfileScanService>();

        // Defaults: everything enabled, session active, scan returns Clean.
        SetConfig(CreateConfig());
        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);
        _profileScanService
            .ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>())
            .Returns(CreateScanResult());

        _gate = new ProfileScanGate(
            _configService,
            _userRepository,
            _sessionManager,
            _profileScanService,
            new PipelineMetrics(),
            NullLogger<ProfileScanGate>.Instance);
    }

    [Test]
    public async Task FirstMessage_UntrustedNeverScanned_Scans()
    {
        // The regression: an untrusted user whose first observed activity is a
        // message, with no join event, was never scanned by any trigger.
        SetUser(CreateUser(profileScannedAt: null, isTrusted: false));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task FirstMessage_UserRowDoesNotExistYet_Scans()
    {
        SetUser(null);

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task FirstMessage_AlreadyScanned_Skips()
    {
        SetUser(CreateUser(profileScannedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FirstMessage_TrustedUser_Skips()
    {
        SetUser(CreateUser(profileScannedAt: null, isTrusted: true));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FirstMessage_FlagDisabled_Skips()
    {
        var config = CreateConfig();
        config.ScanOnFirstMessage = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Join_AlreadyScanned_StillScans()
    {
        // Join always rescans. Only the first-message trigger is once-per-user.
        SetUser(CreateUser(profileScannedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var result = await ScanAsync(ProfileScanTrigger.Join);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task Join_ScanOnJoinDisabled_Skips()
    {
        var config = CreateConfig();
        config.ScanOnJoin = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.Join);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ProfileChange_ScanOnProfileChangeDisabled_Skips()
    {
        // Proves the previously dead ScanOnProfileChange flag is now honored.
        var config = CreateConfig();
        config.ScanOnProfileChange = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.ProfileChange);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ProfileScanDisabled_AllTriggersSkip()
    {
        var config = CreateConfig();
        config.Enabled = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        Assert.Multiple(async () =>
        {
            Assert.That(await ScanAsync(ProfileScanTrigger.Join), Is.Null);
            Assert.That(await ScanAsync(ProfileScanTrigger.FirstMessage), Is.Null);
            Assert.That(await ScanAsync(ProfileScanTrigger.ProfileChange), Is.Null);
        });
    }

    [Test]
    public async Task NoActiveSession_Skips()
    {
        SetUser(CreateUser(profileScannedAt: null));
        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ScanThrows_ExceptionPropagates()
    {
        SetUser(CreateUser(profileScannedAt: null));
        _profileScanService
            .ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProfileScanResult>>(_ => throw new InvalidOperationException("scan failed"));

        Assert.That(
            async () => await ScanAsync(ProfileScanTrigger.FirstMessage),
            Throws.TypeOf<InvalidOperationException>());
    }

    private Task<ProfileScanResult?> ScanAsync(ProfileScanTrigger trigger) =>
        _gate.ScanIfEligibleAsync(
            UserIdentity.FromId(TestUserId),
            ChatIdentity.FromId(TestChatId),
            trigger,
            CancellationToken.None);

    private void SetConfig(ProfileScanConfig profileScan)
    {
        var welcome = WelcomeConfig.Default;
        welcome.JoinSecurity.ProfileScan = profileScan;
        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(welcome));
    }

    private void SetUser(TelegramUser? user) =>
        _userRepository
            .GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(user));

    private static ProfileScanConfig CreateConfig() => new()
    {
        Enabled = true,
        ScanOnJoin = true,
        ScanOnProfileChange = true,
        ScanOnFirstMessage = true,
    };

    private static ProfileScanResult CreateScanResult() => new(
        TelegramUserId: TestUserId,
        Bio: null,
        PersonalChannelId: null,
        PersonalChannelTitle: null,
        PersonalChannelAbout: null,
        HasPinnedStories: false,
        PinnedStoryCaptions: null,
        IsScam: false,
        IsFake: false,
        IsVerified: false,
        Score: 0.0m,
        Outcome: ProfileScanOutcome.Clean,
        AiReason: null,
        AiSignalsDetected: null);

    private static TelegramUser CreateUser(
        DateTimeOffset? profileScannedAt,
        bool isTrusted = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: TestUserId,
            Username: "AndreaRuiz83",
            FirstName: "Andrea",
            LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: isTrusted, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now,
            ProfileScannedAt: profileScannedAt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanGateTests"`

Expected: compile error, `ProfileScanGate` and `ProfileScanTrigger` do not exist.

If `new PipelineMetrics()` does not compile, open `TelegramGroupsAdmin.Telegram/Metrics/PipelineMetrics.cs`, check its constructor, and construct it the way other unit tests in the suite already do (search the test projects for `new PipelineMetrics` or `PipelineMetrics` substitutes).

If `WelcomeConfig.Default` does not expose a settable `JoinSecurity.ProfileScan`, build the config object graph explicitly instead of mutating the default.

- [ ] **Step 3: Create the trigger enum**

Create `ProfileScanTrigger.cs`:

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// What caused a profile scan to be considered. Selects which config flag
/// applies and whether the never-scanned condition is enforced.
/// </summary>
public enum ProfileScanTrigger
{
    /// <summary>User joined a chat. Always rescans, ignoring prior scan history.</summary>
    Join,

    /// <summary>
    /// User sent a message and has never been scanned. Covers users who arrive
    /// without a join event, such as accounts commenting on channel posts in a
    /// linked discussion group.
    /// </summary>
    FirstMessage,

    /// <summary>Bot API profile fields changed. Always rescans.</summary>
    ProfileChange
}
```

- [ ] **Step 4: Create the interface**

Create `IProfileScanGate.cs`:

```csharp
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <summary>
/// Single owner of the profile scan eligibility decision, shared by every
/// automatic trigger. Admin-initiated rescans (UI, bulk rescan job) call
/// IProfileScanService directly and intentionally bypass this gate.
/// </summary>
public interface IProfileScanGate
{
    /// <summary>
    /// Runs a profile scan if this trigger is eligible for this user.
    /// </summary>
    /// <returns>The scan result, or null when the scan was skipped.</returns>
    Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user,
        ChatIdentity? chat,
        ProfileScanTrigger trigger,
        CancellationToken ct);
}
```

- [ ] **Step 5: Implement the gate**

Create `ProfileScanGate.cs`. Checks run cheapest first. Exceptions from the scan propagate; callers decide what a failure means.

```csharp
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <inheritdoc />
public sealed class ProfileScanGate(
    IConfigService configService,
    ITelegramUserRepository userRepository,
    ITelegramSessionManager sessionManager,
    IProfileScanService profileScanService,
    PipelineMetrics pipelineMetrics,
    ILogger<ProfileScanGate> logger) : IProfileScanGate
{
    public async Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user,
        ChatIdentity? chat,
        ProfileScanTrigger trigger,
        CancellationToken ct)
    {
        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat?.Id ?? 0, ct);
        var config = welcomeConfig?.JoinSecurity?.ProfileScan;

        if (config is null || !config.Enabled)
            return Skip("disabled", user, trigger);

        var triggerEnabled = trigger switch
        {
            ProfileScanTrigger.Join => config.ScanOnJoin,
            ProfileScanTrigger.FirstMessage => config.ScanOnFirstMessage,
            ProfileScanTrigger.ProfileChange => config.ScanOnProfileChange,
            _ => false
        };

        if (!triggerEnabled)
            return Skip("trigger_disabled", user, trigger);

        // A null row means the user is not yet tracked: not trusted, never
        // scanned, so eligible. This is the common case for FirstMessage.
        var existingUser = await userRepository.GetByTelegramIdAsync(user.Id, ct);

        if (existingUser?.IsTrusted == true)
            return Skip("trusted", user, trigger);

        if (trigger == ProfileScanTrigger.FirstMessage && existingUser?.ProfileScannedAt is not null)
            return Skip("already_scanned", user, trigger);

        if (!await sessionManager.HasAnyActiveSessionAsync(ct))
            return Skip("no_session", user, trigger);

        logger.LogDebug(
            "Profile scan gate admitted {User} for trigger {Trigger}",
            user.ToLogDebug(), trigger);

        return await profileScanService.ScanUserProfileAsync(user, chat, ct);
    }

    private ProfileScanResult? Skip(string reason, UserIdentity user, ProfileScanTrigger trigger)
    {
        pipelineMetrics.RecordProfileScanSkipped(reason);

        logger.LogDebug(
            "Profile scan gate skipped {User} for trigger {Trigger}: {Reason}",
            user.ToLogDebug(), trigger, reason);

        return null;
    }
}
```

- [ ] **Step 6: Register the gate in DI**

In `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`, immediately after line 72 (`services.AddSingleton<IProfileScanService, ProfileScanService>();`):

```csharp
            services.AddScoped<IProfileScanGate, ProfileScanGate>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanGateTests"`

Expected: PASS, all 12 tests.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanTrigger.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScanGate.cs \
        TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanGate.cs \
        TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanGateTests.cs
git commit -m "feat(profile-scan): add ProfileScanGate owning scan eligibility"
```

---

### Task 3: Route the join path through the gate

Behavior must not change. The three existing `WelcomeServiceTests` assertions on `IProfileScanService` will break; retargeting them to the gate is how you prove the rewire happened.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs:45-46` (constructor), `:396-445` (step 9)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs:57-58, 115-116, 208-209, 328, 470, 570`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceBlacklistTests.cs` (same constructor, may need the new argument)

**Interfaces:**
- Consumes: `IProfileScanGate.ScanIfEligibleAsync(...)` and `ProfileScanTrigger.Join` from Task 2.
- Produces: nothing new.

- [ ] **Step 1: Read the current step 9 implementation**

Read `WelcomeService.cs` lines 396 to 445 in full before editing. You are replacing the gating conditions only. The `Banned` and `HeldForReview` handling, the verifying-message deletion, the hold message, and the `welcomeMetrics.RecordSecurityCheck` calls all stay exactly as they are.

- [ ] **Step 2: Update the failing tests**

In `WelcomeServiceTests.cs`, add a gate substitute alongside the existing `_profileScanService` field (around line 57):

```csharp
    private IProfileScanGate _profileScanGate = null!;
```

In the setup method (around line 115):

```csharp
        _profileScanGate = Substitute.For<IProfileScanGate>();
```

Pass it to the `WelcomeService` constructor (around line 208), in the position matching the constructor change you make in Step 4.

Then retarget the three `DidNotReceive` assertions at lines 328, 470, and 570 from:

```csharp
        await _profileScanService.DidNotReceive().ScanUserProfileAsync(
```

to:

```csharp
        await _profileScanGate.DidNotReceive().ScanIfEligibleAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity?>(),
            Arg.Any<ProfileScanTrigger>(),
            Arg.Any<CancellationToken>());
```

Match each call site's existing argument shape; read the surrounding lines rather than assuming.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeService"`

Expected: compile error, `WelcomeService` has no constructor parameter accepting `IProfileScanGate`.

- [ ] **Step 4: Add the gate to the constructor**

In `WelcomeService.cs`, add a parameter after `IProfileScanService profileScanService,`:

```csharp
    IProfileScanGate profileScanGate,
```

Leave `profileScanService` in place for now; Step 5 removes its last use, and Step 7 removes the parameter if the compiler reports it unused.

- [ ] **Step 5: Replace the inline gate with the gate call**

Replace the condition block that currently opens at line 397:

```csharp
            if (config.JoinSecurity.ProfileScan.Enabled
                && config.JoinSecurity.ProfileScan.ScanOnJoin
                && existingUser?.IsTrusted != true)
            {
                if (await sessionManager.HasAnyActiveSessionAsync(cancellationToken))
                {
                    var scanResult = await profileScanService.ScanUserProfileAsync(
                        UserIdentity.From(user),
                        ChatIdentity.From(chatMemberUpdate.Chat),
                        cancellationToken);
```

with:

```csharp
            var scanResult = await profileScanGate.ScanIfEligibleAsync(
                UserIdentity.From(user),
                ChatIdentity.From(chatMemberUpdate.Chat),
                ProfileScanTrigger.Join,
                cancellationToken);

            if (scanResult is not null)
            {
```

Then de-indent the retained `Banned` and `HeldForReview` blocks by one level, and collapse the old `else` branch that logged "No User API session available, skipping profile scan" plus the outer `else` that recorded the skipped metric into a single `else` on this new `if`, preserving the existing `welcomeMetrics.RecordSecurityCheck("profile_scan", "skipped")` call. Read lines 436 to 450 to see both existing else branches before collapsing them.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeService"`

Expected: PASS.

- [ ] **Step 7: Remove now-unused constructor parameters**

Run: `dotnet build TelegramGroupsAdmin.Telegram`

If the build reports `profileScanService` or `sessionManager` as unused in `WelcomeService`, remove those constructor parameters and any now-dead `using` directives. Then update the test constructor calls in both `WelcomeServiceTests.cs` and `WelcomeServiceBlacklistTests.cs` to match, and re-run the filter from Step 6.

Do not remove `sessionManager` if other methods in the file still use it. Search the file first.

- [ ] **Step 8: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceBlacklistTests.cs
git commit -m "refactor(welcome): route join profile scan through ProfileScanGate"
```

---

### Task 4: Add the first-message trigger and short-circuit detection

The fix itself. Runs inline and awaited after the user upsert, matching the join path.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs:700-711` (profile-change branch), `:711-717` (new call site), `:750` (short-circuit)
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.Log.cs`

**Interfaces:**
- Consumes: `IProfileScanGate.ScanIfEligibleAsync(...)`, `ProfileScanTrigger.FirstMessage`, `ProfileScanTrigger.ProfileChange` from Task 2.
- Produces: local `profileScanBanned` boolean guarding content detection.

- [ ] **Step 1: Add the log messages**

In `MessageProcessingService.Log.cs`, after the `LogProfileChangeDetected` declaration at line 29:

```csharp
    [LoggerMessage(Level = LogLevel.Information, Message = "Profile scan on first message banned {User}, skipping content detection")]
    private static partial void LogFirstMessageScanBanned(ILogger logger, string user);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile scan failed for {User}, continuing message processing")]
    private static partial void LogProfileScanFailed(ILogger logger, Exception exception, string user);
```

Also update the existing `LogProfileChangeDetected` message text, since it no longer schedules a background job:

```csharp
    [LoggerMessage(Level = LogLevel.Information, Message = "Profile change detected for {User}: {OldProfile} → {NewProfile}, running profile scan")]
    private static partial void LogProfileChangeDetected(ILogger logger, string user, string oldProfile, string newProfile);
```

- [ ] **Step 2: Switch the profile-change branch to the gate**

Replace lines 700 to 708 (the `try`/`catch` that resolves `BackgroundJobScheduler` and calls `ScheduleProfileScanAsync`) with:

```csharp
                try
                {
                    var profileScanGate = messageScope.ServiceProvider.GetRequiredService<IProfileScanGate>();
                    await profileScanGate.ScanIfEligibleAsync(
                        UserIdentity.From(message.From),
                        ChatIdentity.From(message.Chat),
                        ProfileScanTrigger.ProfileChange,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    LogProfileScanFailed(logger, ex, message.From.ToLogDebug());
                }
```

- [ ] **Step 3: Add the first-message call site**

Immediately after line 711 (`await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);`), insert:

```csharp
            // Profile scan for users we have never scanned. Users who arrive
            // without a join event (for example, accounts commenting on channel
            // posts in a linked discussion group) are not covered by the join
            // trigger, and have no prior record for the profile-diff trigger to
            // compare against. Runs after the upsert so the user row exists for
            // the scan-result and report foreign keys. The gate owns the whole
            // eligibility decision, including whether this user was scanned before.
            var profileScanBanned = false;

            try
            {
                var profileScanGate = messageScope.ServiceProvider.GetRequiredService<IProfileScanGate>();
                var firstMessageScan = await profileScanGate.ScanIfEligibleAsync(
                    UserIdentity.From(message.From),
                    ChatIdentity.From(message.Chat),
                    ProfileScanTrigger.FirstMessage,
                    cancellationToken);

                if (firstMessageScan?.Outcome == ProfileScanOutcome.Banned)
                {
                    profileScanBanned = true;
                    LogFirstMessageScanBanned(logger, message.From.ToLogDebug());
                }
            }
            catch (Exception ex)
            {
                // A failed scan must never cost us the message.
                LogProfileScanFailed(logger, ex, message.From.ToLogDebug());
            }
```

Call the gate unconditionally. Do **not** add a `contentCheckSkipReason` pre-check here: the gate
owns the whole eligibility decision, and its trusted check already covers trusted users and
admins. A join's service message is absorbed by the gate's never-scanned check or by the 60s
freshness dedup inside `ScanUserProfileAsync`, so it needs no special case. The cost is one
extra primary-key lookup per message, accepted deliberately in the spec.
```

- [ ] **Step 4: Short-circuit content detection**

Change the condition at line 750 from:

```csharp
            if (commandResult == null && (!string.IsNullOrWhiteSpace(text) || photoLocalPath != null))
```

to:

```csharp
            if (!profileScanBanned
                && commandResult == null
                && (!string.IsNullOrWhiteSpace(text) || photoLocalPath != null))
```

- [ ] **Step 5: Add required using directives**

Ensure `MessageProcessingService.cs` has:

```csharp
using TelegramGroupsAdmin.Telegram.Services.UserApi;
```

`TelegramGroupsAdmin.Core.Models` (for `ProfileScanOutcome`, `UserIdentity`, `ChatIdentity`) and `TelegramGroupsAdmin.Telegram.Extensions` (for `UserIdentity.From`) are already imported at lines 17 and 14.

- [ ] **Step 6: Verify it builds and the existing suite still passes**

Run: `dotnet build TelegramGroupsAdmin.Telegram`

Expected: succeeds.

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: PASS. `MessageProcessingServiceProfileDiffTests` covers `BuildProfileChangeReason`, which you have not changed, so it must stay green.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs \
        TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.Log.cs
git commit -m "fix(profile-scan): scan never-scanned users on their first message

Users whose first observed activity is a message got no profile scan:
no join event for the join trigger, and no prior record for the
profile-diff trigger to compare against. Accounts replying to
auto-forwarded channel posts in a linked discussion group land in
exactly this gap.

Routes the profile-change trigger through the gate as well, replacing
the Quartz job hop with an inline call that matches the join path."
```

---

### Task 5: Reconcile admin trust on refresh

Secondary bug. Admin auto-trust fires only on the promotion event, so an admin promoted before the feature existed, or whose one-shot trust write failed, is never back-filled. The gate's "active admin implies trusted" assumption depends on this.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs:450-500`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/Bot/BotChatServiceTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Read the current refresh loop and the test fixture**

Read `BotChatService.cs` lines 440 to 505, and read `BotChatServiceTests.cs` in full to learn the fixture's setup, its `MigrationTestHelper` usage, and how it builds the service. The class docstring already documents "Auto-trusts new admins globally", so this test belongs in that file.

- [ ] **Step 2: Write the failing tests**

Add to `BotChatServiceTests.cs`, following that fixture's existing arrange/act/assert shape and its canonical anchors. `WorkshopAlumniAdminId` (`9742468412405`) and `WorkshopAlumniChatId` (`-100059667856554`) are already defined as constants in the file.

```csharp
[Test]
public async Task RefreshChatAdmins_ExistingAdminNotTrusted_ReconcilesTrust()
{
    // Admin auto-trust originally fired only on the promotion event, so an
    // admin promoted before that feature existed was never back-filled.
    await SetAdminTrustAsync(WorkshopAlumniAdminId, isTrusted: false);

    await RefreshAdminsAsync(WorkshopAlumniChatId, WorkshopAlumniAdminId);

    var user = await GetTelegramUserAsync(WorkshopAlumniAdminId);
    Assert.That(user!.IsTrusted, Is.True);
}

[Test]
public async Task RefreshChatAdmins_AdminAlreadyTrusted_DoesNotDuplicateTrustAction()
{
    await SetAdminTrustAsync(WorkshopAlumniAdminId, isTrusted: true);
    var before = await CountTrustActionsAsync(WorkshopAlumniAdminId);

    await RefreshAdminsAsync(WorkshopAlumniChatId, WorkshopAlumniAdminId);

    var after = await CountTrustActionsAsync(WorkshopAlumniAdminId);
    Assert.That(after, Is.EqualTo(before));
}
```

Write the four helpers with these exact signatures, using the fixture's existing DbContext and
mocked-`IBotChatHandler` patterns:

```csharp
// Sets telegram_users.is_trusted for the given user via the repository.
private async Task SetAdminTrustAsync(long telegramUserId, bool isTrusted);

// Arranges the mocked IBotChatHandler to return adminUserId as an admin of chatId,
// then invokes the same public refresh entry point the fixture's other admin tests call.
private async Task RefreshAdminsAsync(long chatId, long adminUserId);

// Reads the user row back through ITelegramUserRepository.GetByTelegramIdAsync.
private async Task<TelegramUser?> GetTelegramUserAsync(long telegramUserId);

// Counts user_actions rows with ActionType == UserActionType.Trust for the user.
private async Task<int> CountTrustActionsAsync(long telegramUserId);
```

Find the refresh entry point by reading `IBotChatService.cs` and the existing admin-cache tests
in this fixture. Do not call the private loop directly or via reflection.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BotChatServiceTests"`

Expected: `RefreshChatAdmins_ExistingAdminNotTrusted_ReconcilesTrust` FAILS with `IsTrusted` still false. The duplicate-action test should already PASS, since trust currently never fires for existing admins; it is a regression guard for Step 4.

- [ ] **Step 4: Reconcile trust for existing admins**

In `BotChatService.cs`, capture the user record returned at line 453:

```csharp
                var adminRecord = await userRepo.GetOrCreateAsync(
                    UserIdentity.From(admin.User), admin.User.IsBot, ct);
```

Change the condition at line 462 from `if (wasNew)` to:

```csharp
                var needsTrust = !adminRecord.IsTrusted;

                if (wasNew || needsTrust)
```

Inside that block, keep the "New admin promoted" log gated on `wasNew` only, so a reconciliation pass does not log a false promotion. Guard the `UserActionRecord` insert and the `TrustUserAsync` call on `needsTrust`, so refresh passes do not append a duplicate audit row for the already-trusted admins.

Read the whole block before editing: `adminNames.Add(...)` at line 460 must keep running for every admin regardless of trust state.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~BotChatServiceTests"`

Expected: PASS, both new tests and every pre-existing test in the fixture.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs \
        TelegramGroupsAdmin.IntegrationTests/Telegram/Services/Bot/BotChatServiceTests.cs
git commit -m "fix(admin): reconcile admin trust on refresh, not just on promotion

Auto-trust fired only when an admin was newly discovered, so an admin
promoted before the feature existed, or whose one-shot trust write
failed, was never back-filled. One active admin was in this state."
```

---

### Task 6: Remove the orphaned profile scan job

Task 4 removed the last caller of `ScheduleProfileScanAsync`. These are now genuinely orphaned. Separate commit so it is easy to drop in review.

**Files:**
- Delete: `TelegramGroupsAdmin.BackgroundJobs/Jobs/ProfileScanJob.cs`
- Delete: `TelegramGroupsAdmin.Core/JobPayloads/ProfileScanPayload.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/BackgroundJobScheduler.cs:74-93`
- Modify: `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs:112-118`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs:114`
- Modify: `TelegramGroupsAdmin.BackgroundJobs/Services/QuartzJobScheduler.cs:109`
- Test: `TelegramGroupsAdmin.UnitTests/Core/BackgroundJobs/BackgroundJobNamesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

`ProfileRescanJob` is a different job (bulk rescan) and must be left alone. Do not confuse the two.

- [ ] **Step 1: Confirm there are no remaining callers**

Run:

```bash
grep -rn "ScheduleProfileScanAsync\|ProfileScanPayload\|BackgroundJobNames.ProfileScan\b\|ProfileScanJob\b" --include=*.cs --include=*.razor .
```

Expected: hits only in the files listed above. If anything else references them, stop and report rather than deleting.

- [ ] **Step 2: Delete the job and payload**

```bash
git rm TelegramGroupsAdmin.BackgroundJobs/Jobs/ProfileScanJob.cs \
       TelegramGroupsAdmin.Core/JobPayloads/ProfileScanPayload.cs
```

- [ ] **Step 3: Remove the scheduler method**

In `BackgroundJobScheduler.cs`, delete the `ScheduleProfileScanAsync` method along with its XML doc comment (lines 74 to 93). Also remove the `ProfileScan(userId)` deduplication-key helper if nothing else uses it. Search first:

```bash
grep -rn "ProfileScan(" --include=*.cs TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin.Core/
```

- [ ] **Step 4: Remove the job name, registration, and mapping**

In `BackgroundJobNames.cs`, delete the `ProfileScan` constant and its doc comment near line 116. Leave `ProfileRescan` at line 128 intact.

In `BackgroundJobs/Extensions/ServiceCollectionExtensions.cs`, delete line 114:

```csharp
        q.AddJob<ProfileScanJob>(opts => opts.WithIdentity(BackgroundJobNames.ProfileScan).StoreDurably());
```

In `QuartzJobScheduler.cs`, delete the switch arm at line 109:

```csharp
            BackgroundJobNames.ProfileScan => typeof(Jobs.ProfileScanJob),
```

- [ ] **Step 5: Update the job-names test**

Open `TelegramGroupsAdmin.UnitTests/Core/BackgroundJobs/BackgroundJobNamesTests.cs` and remove any assertion referencing the deleted `ProfileScan` constant. If that fixture asserts a total count of job names, decrement it.

- [ ] **Step 6: Verify the whole solution builds and tests pass**

Run: `dotnet build`

Expected: succeeds with no unresolved references.

Run: `dotnet test TelegramGroupsAdmin.UnitTests`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore(jobs): remove orphaned on-demand ProfileScanJob

The profile-change trigger now scans inline through ProfileScanGate,
matching the join path, which left this job with no callers. The UI
rescan and the bulk ProfileRescanJob call IProfileScanService directly
and are unaffected."
```

---

### Task 7: Integration harness for the first-message trigger

Proves `MessageProcessingService` calls the gate at the right point with the right trigger. No existing harness drives `HandleMessageAsync`; the integration file named `MessageProcessingServiceTests` only reflection-tests static helpers.

**This task is an approved unknown.** If the concrete handlers on the pre-detection path make it unworkable, or the test proves flaky, abandon it and report why. The regression itself is covered by `ProfileScanGateTests`; what is lost is coverage of the wiring. Do not spend more than one focused attempt before escalating.

**Files:**
- Create: `TelegramGroupsAdmin.IntegrationTests/Telegram/MessageProcessingFirstMessageScanTests.cs`
- Test: same file

**Interfaces:**
- Consumes: `IProfileScanGate`, `ProfileScanTrigger.FirstMessage` from Task 2; the call site from Task 4.
- Produces: nothing.

- [ ] **Step 1: Understand the dependency graph**

`HandleMessageAsync` resolves 24 distinct services across 35 sites. List them:

```bash
grep -o "GetRequiredService<[A-Za-z.]*>" TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs | sort -u
```

Seven are concrete handler classes (`AdminMentionHandler`, `BackgroundJobScheduler`, `ContentDetectionOrchestrator`, `FileScanningHandler`, `ImageProcessingHandler`, `MediaProcessingHandler`, `MessageEditProcessor`). NSubstitute cannot intercept their non-virtual methods, so a substitute would run real code needing real dependencies.

The approach that makes this tractable: arrange the substituted `IProfileScanService` to return `Banned`. The Task 4 short-circuit then skips content detection, so `ContentDetectionOrchestrator` is never resolved.

- [ ] **Step 2: Write the failing test**

Create the fixture. Use `MigrationTestHelper.CreateDatabaseFromGoldenTemplateAsync` for the database, following the pattern in `BotChatServiceTests.cs`. Build a `ServiceCollection` registering:

- Real: `ITelegramUserRepository`, `IMessageHistoryRepository`, `IUserActionsRepository`, `IUsernameHistoryRepository`, `IConfigService`, and the real `ProfileScanGate` as `IProfileScanGate`
- Substituted: `IProfileScanService` returning a `Banned` result, `ITelegramSessionManager` returning `HasAnyActiveSessionAsync` true, and every remaining interface-based dependency
- Real instances of the concrete handler classes only where the pre-detection path actually requires them

The test:

```csharp
[Test]
public async Task HandleMessage_UntrustedUserNeverScanned_TriggersProfileScan()
{
    // Reproduces the outage: an untrusted user with no join event and no prior
    // scan posts a message. Before the fix, no trigger fired.
    var message = TelegramTestFactory.CreateMessage(
        messageId: 1,
        text: "hi",
        chat: TelegramTestFactory.CreateChat(id: MainChatId),
        from: TelegramTestFactory.CreateUser(id: UnscannedUserId, username: "AndreaRuiz83"));

    await _sut.HandleMessageAsync(message, CancellationToken.None);

    await _profileScanService.Received(1).ScanUserProfileAsync(
        Arg.Is<UserIdentity>(u => u.Id == UnscannedUserId),
        Arg.Any<ChatIdentity?>(),
        Arg.Any<CancellationToken>());
}
```

Confirm the real entry-point name and signature from `IMessageProcessingService.cs` before writing the act line, and confirm `TelegramTestFactory.CreateMessage`'s actual parameter names from `TestHelpers/TelegramTestFactory.cs:16`.

Enable the flag for this test by writing a `welcome_config` with `Enabled = true` and `ScanOnFirstMessage = true` through `IConfigService`, since the default is off.

This asserts on the substituted `IProfileScanService` deliberately: the gate's own decision logic is already covered by unit tests, and what needs proving here is that the wiring reaches the scan at all.

- [ ] **Step 3: Run test to verify it fails for the right reason**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageProcessingFirstMessageScanTests"`

Expected: FAIL. Before wiring is confirmed, either a missing DI registration throws, or `Received(1)` fails with zero calls.

If it fails because a concrete handler cannot be constructed, that is the abandonment signal from the task preamble. Record which handler and why, then stop.

- [ ] **Step 4: Make it pass**

The production code from Task 4 already implements the behavior. Adjust only the test harness (DI registrations, config seeding, fixture setup) until the test passes. Do not change production code to accommodate the harness.

- [ ] **Step 5: Verify it is not flaky**

Run the fixture three times in a row:

```bash
for i in 1 2 3; do dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageProcessingFirstMessageScanTests"; done
```

Expected: PASS all three. Any intermittent failure is the abandonment signal. Delete the file and report rather than adding retries or sleeps. Never use `Task.Delay` to stabilise a test.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.IntegrationTests/Telegram/MessageProcessingFirstMessageScanTests.cs
git commit -m "test(profile-scan): cover first-message scan trigger wiring"
```

---

### Task 8: Full verification

**Files:** none modified.

**Interfaces:** none.

- [ ] **Step 1: Build the solution clean**

Run: `dotnet build`

Expected: succeeds, no new warnings.

- [ ] **Step 2: Run the unit and component suites**

Run: `dotnet test TelegramGroupsAdmin.UnitTests`
Run: `dotnet test TelegramGroupsAdmin.ComponentTests`

Expected: PASS. No pipes; pipes hide failures.

- [ ] **Step 3: Run the integration suite**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests`

Expected: PASS. Takes roughly 50 seconds. Requires Docker for Testcontainers.

- [ ] **Step 4: Confirm the config default is still off**

Run:

```bash
grep -n "ScanOnFirstMessage" TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs \
                             TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs
```

Expected: both show `= false`. Shipping this on by default was explicitly rejected.

- [ ] **Step 5: Report status**

Summarise: which tasks landed, whether Task 7 survived or was abandoned and why, and the full test results. State plainly if anything is red. Do not claim success without the command output to back it.

---

## Post-Merge Manual Step

**The bug stays live until the flag is enabled.** After the image is built and deployed, open the welcome configuration UI and switch on "Scan on first message" under Join Security. Nothing scans on first message until that toggle is saved to the `configs` table.

To confirm it is working, watch for `Profile scan for <user>` log lines from users who have no preceding `New user joined` event.

## Known Follow-Up, Out of Scope

**Short-text veto abstain.** The single-emoji reply also evaded content detection: `Combined text too short (< 20 chars)` makes the AI veto abstain, and a rule score of 0.8 alone did not auto-action. Emoji-only spam still needs a human report until this is addressed separately.
