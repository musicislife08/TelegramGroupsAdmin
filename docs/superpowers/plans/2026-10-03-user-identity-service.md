# User Identity Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every Telegram user identity comes from one service that records observed names (newest observation wins) and carries a name verdict, so bot-written mentions show `[name removed: explicit]` for flagged names while logs and the UI keep the real name.

**Architecture:** `UserIdentity` (Core) gains `NameVerdict` and `BotDisplayName(NameMasking)`. A `user_identities` Postgres view (names + latest-scan flags) and one mapper are the single source for every identity read. `IUserIdentityService` (Telegram) owns `ObserveAsync` (conditional name update + history + audit in one transaction, then an inline rescan on rename where the caller asks for one, as today) and `ResolveAsync` / `ResolveManyAsync` (names + verdict from the latest scan row). `TelegramMessageBuilder.For(NameMasking)` applies the per-chat "Mask flagged names" setting where text is written. A rule file plus a source-scanning unit test keep identity construction on the sanctioned paths.

**Tech Stack:** .NET 10, C# 14, EF Core 10 + PostgreSQL 18 (views, raw SQL via `ExecuteSqlAsync` / `SqlQuery`), Quartz.NET, NUnit, NSubstitute 6.

**Spec:** `docs/superpowers/specs/2026-10-03-user-identity-service-design.md`

> **Note (after implementation):** the `RenameRescan` option below was replaced by the spec's
> `ObserveAsync` rename rules: `ObserveAsync` decides from the observation's source and the user
> whether a rename is rescanned, and callers pass no rescan option. Read `RenameRescan` in the
> tasks below as historical.

## Global Constraints

- Branch: `feat/552-user-identity-service` (spec, plan and implementation together). PR to `develop` only. Conventional commits. Never commit to `develop`/`master`. Never use git worktrees.
- Fixed wording: `[name removed: explicit]` (explicit), `[name removed: spam]` (promotional). No configurable redaction text.
- `NameVerdict` values: `Unscanned`, `Clean`, `Promotional`, `Explicit`. Nothing produces `Promotional` in this plan.
- Setting: `MaskFlaggedNames`, default `true`, global (`chat_id = 0`) with per-chat override via `GetEffectiveWelcomeAsync`; chat-less messages use the global value.
- Names are global per Telegram account: one resolved identity is reused across chats.
- Concurrency: no in-process lock for names. Row lock + `names_observed_at` guard ("newest observation wins").
- Integration tests use canonical data only; never seed preconditions (`.claude/rules/integration-test-data.md`). Anchors are listed per task.
- NSubstitute matcher lambdas use `x!.Prop`, never `?.`.
- Migrations: change models/`AppDbContext` first, then `dotnet ef migrations add <Name> --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`.
- Services access the DB only through repositories; UI goes through services.
- Every project has `TreatWarningsAsErrors`; keep builds at 0 warnings.

## Spec deviations

None. Planning decisions (inline rename rescans, the `user_identities` view, rule-plus-test enforcement, masking independent of the scan switch) are in the spec.

## Review Focus

1. A Quartz payload queued before deploy (no `verdict` member) must still deserialize and run: pinned by Task 1's JSON compatibility test.
2. A user renamed twice quickly (A→B→A) must record both changes and end on A, and a stale scan observation must not undo a newer message name: pinned by Task 5's ordering tests.
3. Names containing only whitespace or emoji, and users with only an id (no row), must still render a non-empty mention: pinned by Task 1's `BotDisplayName` cases and Task 6's id-only resolve test.
4. A DB failure during `ObserveAsync` must not stop the message being moderated: pinned by Task 6's failure test.
5. Turning `MaskFlaggedNames` off for one chat must not unmask admin DMs (global value applies there), and a chat with scanning off still masks a name flagged elsewhere: pinned by Task 3's `GetNameMaskingAsync` tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `TelegramGroupsAdmin.Core/Models/NameVerdict.cs` (create) | Verdict enum |
| `TelegramGroupsAdmin.Core/Models/NameMasking.cs` (create) | Masking policy enum |
| `TelegramGroupsAdmin.Core/Models/NameRedaction.cs` (create) | Fixed label constants |
| `TelegramGroupsAdmin.Core/Models/UserIdentity.cs` (modify) | `Verdict`, `BotDisplayName(masking)`, `ForPreview`, `ForTest` |
| `TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs` (modify) | `For(NameMasking)`, masked `Mention` |
| `TelegramGroupsAdmin.Configuration/...` (modify) | `MaskFlaggedNames` rename, `GetNameMaskingAsync` |
| `TelegramGroupsAdmin.Data/Migrations/<ts>_RenameMaskFlaggedNames.cs` (create) | JSONB key rename |
| `TelegramGroupsAdmin.Data/Models/TelegramUserDto.cs` (modify) | `names_observed_at` |
| `TelegramGroupsAdmin.Data/Migrations/<ts>_AddNamesObservedAt.cs` (create) | column |
| `TelegramGroupsAdmin.Telegram/Models/ObservedUser.cs` (create) | observation input |
| `TelegramGroupsAdmin.Telegram/Models/ObservedNamesResult.cs` (create) | repository result |
| `TelegramGroupsAdmin.Telegram/Repositories/TelegramUserRepository.cs` (modify) | `GetOrUpdateAsync`, `MarkActiveAsync`; remove `GetOrCreateAsync`, `UpsertAsync` |
| `TelegramGroupsAdmin.Data/Models/UserIdentityView.cs` (create) + migration `AddUserIdentitiesView` | the view |
| `TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs` (create) | the one row-to-identity mapper and verdict rule |
| `TelegramGroupsAdmin.Telegram/Services/Identity/IUserIdentityService.cs`, `UserIdentityService.cs` (create) | the service |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs`, `IProfileScanGate.cs`, `ProfileScanGate.cs` (modify) | single-flight, freshness bypass, observe live names |
| `.claude/rules/user-identity.md`, `TelegramGroupsAdmin.UnitTests/Architecture/UserIdentityConstructionTests.cs` (create) | enforcement |

---

### Task 0: Branch

The spec, this plan and the implementation share one branch, `feat/552-user-identity-service`.

- [ ] **Step 1: Confirm the branch**

```bash
git branch --show-current
```
Expected: `feat/552-user-identity-service`

---

### Task 1: Core verdict types and `UserIdentity.BotDisplayName`

**Files:**
- Create: `TelegramGroupsAdmin.Core/Models/NameVerdict.cs`, `NameMasking.cs`, `NameRedaction.cs`
- Modify: `TelegramGroupsAdmin.Core/Models/UserIdentity.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Models/UserIdentityTests.cs` (create)

**Interfaces:**
- Produces: `enum NameVerdict { Unscanned, Clean, Promotional, Explicit }`; `enum NameMasking { Off, On }`; `NameRedaction.Explicit`, `NameRedaction.Spam`; `UserIdentity.Verdict { get; init; }`; `string UserIdentity.BotDisplayName(NameMasking masking)`; `static UserIdentity ForPreview(long id, string? first, string? last, string? username)`; `static UserIdentity ForTest(long id, string? first = null, string? last = null, string? username = null, NameVerdict verdict = NameVerdict.Unscanned)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Models;

[TestFixture]
public class UserIdentityTests
{
    [TestCase(NameVerdict.Unscanned, NameMasking.On, "Sofia R")]
    [TestCase(NameVerdict.Clean, NameMasking.On, "Sofia R")]
    [TestCase(NameVerdict.Explicit, NameMasking.On, "[name removed: explicit]")]
    [TestCase(NameVerdict.Promotional, NameMasking.On, "[name removed: spam]")]
    [TestCase(NameVerdict.Explicit, NameMasking.Off, "Sofia R")]
    [TestCase(NameVerdict.Promotional, NameMasking.Off, "Sofia R")]
    public void BotDisplayName_MapsVerdictAndMasking(NameVerdict verdict, NameMasking masking, string expected)
    {
        var identity = UserIdentity.ForTest(42, "Sofia", "R", verdict: verdict);

        Assert.That(identity.BotDisplayName(masking), Is.EqualTo(expected));
    }

    [Test]
    public void BotDisplayName_IdOnlyIdentity_FallsBackToUserId()
    {
        var identity = UserIdentity.ForTest(42);

        Assert.That(identity.BotDisplayName(NameMasking.On), Is.EqualTo("User 42"));
    }

    [Test]
    public void DisplayName_IsRealNameRegardlessOfVerdict()
    {
        var identity = UserIdentity.ForTest(42, "Sofia", "R", verdict: NameVerdict.Explicit);

        Assert.That(identity.DisplayName, Is.EqualTo("Sofia R"));
    }

    [Test]
    public void Deserialize_PayloadWithoutVerdict_DefaultsToUnscanned()
    {
        // Shape System.Text.Json wrote for queued Quartz payloads before Verdict existed.
        const string json = """{"Id":42,"FirstName":"Sofia","LastName":"R","Username":null,"DisplayName":"Sofia R"}""";

        var identity = JsonSerializer.Deserialize<UserIdentity>(json)!;

        Assert.That(identity.Id, Is.EqualTo(42));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public void Serialize_RoundTripsVerdict()
    {
        var identity = UserIdentity.ForTest(42, "Sofia", verdict: NameVerdict.Explicit);

        var copy = JsonSerializer.Deserialize<UserIdentity>(JsonSerializer.Serialize(identity))!;

        Assert.That(copy.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityTests"`
Expected: build error, `NameVerdict` / `ForTest` / `BotDisplayName` do not exist.

- [ ] **Step 3: Implement**

`NameVerdict.cs`:
```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// What the latest profile scan concluded about a user's name. Platform-neutral.
/// </summary>
public enum NameVerdict
{
    /// <summary>No scan has judged this account's name.</summary>
    Unscanned = 0,
    Clean = 1,
    /// <summary>The name advertises, solicits or lures.</summary>
    Promotional = 2,
    /// <summary>The name contains explicit text.</summary>
    Explicit = 3
}
```

`NameMasking.cs`:
```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Effective "Mask flagged names" setting for the place a bot message is written.
/// </summary>
public enum NameMasking
{
    Off = 0,
    On = 1
}
```

`NameRedaction.cs`:
```csharp
namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Fixed text shown in bot-written messages instead of a flagged name.
/// </summary>
public static class NameRedaction
{
    public const string Explicit = "[name removed: explicit]";
    public const string Spam = "[name removed: spam]";
}
```

`UserIdentity.cs` (replace the record body; the positional constructor stays; Task 14's scan test limits who calls it):
```csharp
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Identity of a Telegram user as one resolved value: the real name for logs and the UI, and the
/// name verdict that decides what bot-written text shows. Obtain it from IUserIdentityService.
/// Telegram names are per account, so one identity is reused across chats.
/// </summary>
public sealed record UserIdentity(long Id, string? FirstName, string? LastName, string? Username)
{
    public string DisplayName { get; } = TelegramDisplayName.Format(FirstName, LastName, Username, Id);

    /// <summary>
    /// Verdict from the latest profile scan. Defaults to Unscanned, which also covers Quartz
    /// payloads serialized before this member existed.
    /// </summary>
    public NameVerdict Verdict { get; init; } = NameVerdict.Unscanned;

    /// <summary>
    /// Name for text the bot writes to Telegram, given the effective masking setting where the
    /// text is written.
    /// </summary>
    public string BotDisplayName(NameMasking masking) =>
        masking == NameMasking.Off ? DisplayName : Verdict switch
        {
            NameVerdict.Explicit => NameRedaction.Explicit,
            NameVerdict.Promotional => NameRedaction.Spam,
            _ => DisplayName
        };

    /// <summary>
    /// Creates an ID-only identity. Internal fallback used by FromAsync when user isn't in DB.
    /// </summary>
    public static UserIdentity FromId(long id) => new(id, null, null, null);

    /// <summary>Sample identity for settings-page previews. Never sent to Telegram for a real user.</summary>
    public static UserIdentity ForPreview(long id, string? firstName, string? lastName, string? username) =>
        new(id, firstName, lastName, username);

    /// <summary>Identity for tests.</summary>
    public static UserIdentity ForTest(
        long id, string? firstName = null, string? lastName = null, string? username = null,
        NameVerdict verdict = NameVerdict.Unscanned) =>
        new(id, firstName, lastName, username) { Verdict = verdict };
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Models TelegramGroupsAdmin.UnitTests/Core/Models/UserIdentityTests.cs
git commit -m "feat(core): add name verdict and bot display name to UserIdentity"
```

---

### Task 2: `TelegramMessageBuilder.For(NameMasking)` and masked `Mention`

**Files:**
- Modify: `TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramMessageBuilderTests.cs`

**Interfaces:**
- Consumes: `UserIdentity.BotDisplayName(NameMasking)` (Task 1).
- Produces: `static TelegramMessageBuilder TelegramMessageBuilder.For(NameMasking masking)`. The parameterless constructor stays (masking `Off`) until Task 14.

- [ ] **Step 1: Write the failing tests** (append to `TelegramMessageBuilderTests`)

```csharp
    [Test]
    public void Mention_masking_on_shows_label_and_keeps_user_link()
    {
        var user = UserIdentity.ForTest(6612345678, "Bad", "Name", verdict: NameVerdict.Explicit);

        var msg = TelegramMessageBuilder.For(NameMasking.On).Text("Welcome, ").Mention(user).Build();

        Assert.That(msg.Text, Is.EqualTo("Welcome, [name removed: explicit]"));
        var e = msg.Entities.Single();
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(e.Offset, Is.EqualTo("Welcome, ".Length));
        Assert.That(e.Length, Is.EqualTo("[name removed: explicit]".Length));
        Assert.That(e.User!.Id, Is.EqualTo(6612345678));
    }

    [Test]
    public void Mention_masking_off_shows_real_name()
    {
        var user = UserIdentity.ForTest(42, "Bad", "Name", verdict: NameVerdict.Explicit);

        var msg = TelegramMessageBuilder.For(NameMasking.Off).Mention(user).Build();

        Assert.That(msg.Text, Is.EqualTo("Bad Name"));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TelegramMessageBuilderTests"`
Expected: build error, `For` does not exist.

- [ ] **Step 3: Implement** in `TelegramMessageBuilder`:

```csharp
    private readonly NameMasking _masking;

    public TelegramMessageBuilder() : this(NameMasking.Off) { }

    private TelegramMessageBuilder(NameMasking masking) => _masking = masking;

    /// <summary>
    /// Builder for a message written where the effective "Mask flagged names" setting is
    /// <paramref name="masking"/> (see IConfigService.GetNameMaskingAsync).
    /// </summary>
    public static TelegramMessageBuilder For(NameMasking masking) => new(masking);
```

and in `Mention` replace the display-text line:
```csharp
        var displayText = user.BotDisplayName(_masking);
```
Update its XML doc: "Display text is `UserIdentity.BotDisplayName` for this builder's masking setting."

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~TelegramMessageBuilderTests"`
Expected: PASS, including the existing Mention tests (default `Off` keeps today's output).

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramMessageBuilderTests.cs
git commit -m "feat(core): apply name masking in TelegramMessageBuilder.Mention"
```

---

### Task 3: `MaskFlaggedNames` setting and `GetNameMaskingAsync`

**Files:**
- Modify: `TelegramGroupsAdmin.Configuration/Models/Welcome/ProfileScanConfig.cs`, `TelegramGroupsAdmin.Data/Models/Configs/ProfileScanConfigData.cs`, `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs:150-176`, `TelegramGroupsAdmin.Configuration/Services/IConfigService.cs`, `ConfigService.cs`, `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor:190-206`
- Create: migration `RenameMaskFlaggedNames`
- Test: `TelegramGroupsAdmin.UnitTests/Configuration/ConfigServiceNameMaskingTests.cs` (create); update `TelegramGroupsAdmin.IntegrationTests/Configuration/ConfigServiceIntegrationTests.cs:170-205`, `TelegramGroupsAdmin.ComponentTests/Components/WelcomeSystemConfigTests.cs:688-850`; create `TelegramGroupsAdmin.IntegrationTests/Migrations/RenameMaskFlaggedNamesMigrationTests.cs`

**Interfaces:**
- Produces: `bool ProfileScanConfig.MaskFlaggedNames` (default `true`); `ValueTask<NameMasking> IConfigService.GetNameMaskingAsync(long? chatId, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing unit tests**

Read how `ConfigService` is constructed (`ConfigService.cs` constructor) and substitute its repository the same way existing `ConfigService` unit tests do (`grep -rln "new ConfigService(" TelegramGroupsAdmin.UnitTests`); if none exist, substitute `IConfigRepository` and pass a real `HybridCache` from `new ServiceCollection().AddHybridCache().BuildServiceProvider()`.

```csharp
[TestFixture]
public class ConfigServiceNameMaskingTests
{
    private IConfigRepository _repository = null!;
    private ConfigService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IConfigRepository>();
        _sut = CreateConfigService(_repository); // helper per the constructor read above
    }

    private void Effective(long chatId, bool scanEnabled, bool mask) =>
        _repository.GetEffectiveWelcomeAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(new WelcomeConfig
            {
                JoinSecurity = new JoinSecurityConfig
                {
                    ProfileScan = new ProfileScanConfig { Enabled = scanEnabled, MaskFlaggedNames = mask }
                }
            });

    [Test]
    public async Task ChatOverrideOff_ReturnsOff()
    {
        Effective(-100, scanEnabled: true, mask: false);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.Off));
    }

    [Test]
    public async Task ChatEffectiveOn_ReturnsOn()
    {
        Effective(-100, scanEnabled: true, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task NullChat_UsesGlobalRow()
    {
        Effective(0, scanEnabled: true, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(null), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task ScanDisabledInChat_StillMasksWhenSettingOn()
    {
        // Verdicts are per account, so a flag from another chat's scan masks here too.
        Effective(-100, scanEnabled: false, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.On));
    }

    [Test]
    public async Task NoConfig_ReturnsOff()
    {
        _repository.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WelcomeConfig?)null);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.Off));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ConfigServiceNameMaskingTests"`
Expected: build error (`MaskFlaggedNames`, `GetNameMaskingAsync` missing).

- [ ] **Step 3: Implement the rename and the lookup**

`ProfileScanConfig`: delete `DefaultExplicitUsernameRedactionText`, `ExplicitUsernameRedactionText`; replace `MaskExplicitUsername` with:
```csharp
    /// <summary>
    /// When true, bot-written messages show "[name removed: explicit]" or "[name removed: spam]"
    /// instead of a name the latest profile scan flagged. Per-chat override of the global value;
    /// messages that belong to no chat use the global value.
    /// </summary>
    public bool MaskFlaggedNames { get; set; } = true;
```
`ProfileScanConfigData`: same rename, delete `ExplicitUsernameRedactionText`. `WelcomeConfigMappings.cs:158-159,173-174`: map `MaskFlaggedNames` both ways, drop the redaction text lines.

`IConfigService`:
```csharp
    /// <summary>
    /// Effective "Mask flagged names" for a chat, or the global value when <paramref name="chatId"/>
    /// is null (messages that belong to no chat, e.g. admin DMs). Independent of whether this chat
    /// scans profiles: a name verdict belongs to the account, not the chat.
    /// </summary>
    ValueTask<NameMasking> GetNameMaskingAsync(long? chatId, CancellationToken ct = default);
```
`ConfigService`:
```csharp
    public async ValueTask<NameMasking> GetNameMaskingAsync(long? chatId, CancellationToken ct = default)
    {
        var welcome = await GetEffectiveWelcomeAsync(chatId ?? 0, ct);
        return welcome?.JoinSecurity?.ProfileScan.MaskFlaggedNames == true ? NameMasking.On : NameMasking.Off;
    }
```

`WelcomeSystemConfig.razor:190-206`: replace the switch + redaction text field with one switch:
```razor
<MudSwitch @bind-Value="_config.JoinSecurity.ProfileScan.MaskFlaggedNames"
           Label="Mask flagged names"
           Color="Color.Primary" />
<MudText Typo="Typo.caption" Color="Color.Secondary">
    Bot messages show "[name removed: explicit]" or "[name removed: spam]" instead of a name the profile scan flagged.
</MudText>
```
(Keep the surrounding layout from lines 190-206. The switch is no longer disabled when profile scanning is off: a verdict from another chat's scan still applies here. Move it out of any block that only renders when scanning is enabled, and update the component test that expected it disabled.)

- [ ] **Step 4: Add the migration**

Run: `dotnet ef migrations add RenameMaskFlaggedNames --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`
Replace the generated `Up`/`Down` bodies (the model change is JSON-only, so EF generates empty bodies):
```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET welcome_config = jsonb_set(
                    welcome_config,
                    '{joinSecurity,profileScan}',
                    (welcome_config #> '{joinSecurity,profileScan}')
                        - 'maskExplicitUsername' - 'explicitUsernameRedactionText'
                        || CASE WHEN (welcome_config #> '{joinSecurity,profileScan}') ? 'maskExplicitUsername'
                                THEN jsonb_build_object('maskFlaggedNames', welcome_config #> '{joinSecurity,profileScan,maskExplicitUsername}')
                                ELSE '{}'::jsonb END)
                WHERE welcome_config #> '{joinSecurity,profileScan}' IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET welcome_config = jsonb_set(
                    welcome_config,
                    '{joinSecurity,profileScan}',
                    (welcome_config #> '{joinSecurity,profileScan}') - 'maskFlaggedNames'
                        || CASE WHEN (welcome_config #> '{joinSecurity,profileScan}') ? 'maskFlaggedNames'
                                THEN jsonb_build_object('maskExplicitUsername', welcome_config #> '{joinSecurity,profileScan,maskFlaggedNames}')
                                ELSE '{}'::jsonb END)
                WHERE welcome_config #> '{joinSecurity,profileScan}' IS NOT NULL;
                """);
        }
```
Before writing it, confirm the JSON key casing by reading `ConfigRepository.JsonOptions` (camelCase is expected; canonical `04_configs.sql` stores `"profileScan": {"enabled": …}`).

- [ ] **Step 5: Migration test (empty template, migration fixture)**

Find the existing migration-test pattern (`grep -rln "MigrateAsync\|Migrator" TelegramGroupsAdmin.IntegrationTests`) and follow it: migrate an empty database to the migration before `RenameMaskFlaggedNames`, insert one `configs` row whose `welcome_config` has `{"joinSecurity":{"profileScan":{"enabled":true,"maskExplicitUsername":false,"explicitUsernameRedactionText":"x"}}}` (migration input, allowed for migration fixtures), migrate to `RenameMaskFlaggedNames`, then assert `welcome_config #> '{joinSecurity,profileScan}'` equals `{"enabled":true,"maskFlaggedNames":false}`. If no migration-test pattern exists, STOP and report BLOCKED rather than inventing a harness.

- [ ] **Step 6: Update existing tests**

- `ConfigServiceIntegrationTests.cs:170-205`: set and assert `MaskFlaggedNames`; delete the redaction-text assertions; default assertion becomes `MaskFlaggedNames` is true.
- `WelcomeSystemConfigTests.cs:688-850`: the switch is now labelled "Mask flagged names"; delete the redaction-text field tests (the field no longer exists); the save test asserts `MaskFlaggedNames == false`.
- `BanCelebrationService.cs` still reads the old properties: change `profileScanConfig.MaskExplicitUsername` to `MaskFlaggedNames` and the redaction text to `NameRedaction.Explicit` so the build stays green (Task 11 replaces this code). Update `BanCelebrationServiceTests` (unit and integration) expectations from the configured text to `NameRedaction.Explicit`; delete the blank-redaction-text fallback test (the setting no longer exists).

- [ ] **Step 7: Run tests**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ConfigServiceNameMaskingTests|FullyQualifiedName~BanCelebration" && dotnet test TelegramGroupsAdmin.ComponentTests --filter "FullyQualifiedName~WelcomeSystemConfigTests" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ConfigServiceIntegrationTests|FullyQualifiedName~RenameMaskFlaggedNames|FullyQualifiedName~BanCelebration"`
Expected: build 0 warnings; all PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(config): replace explicit-name masking settings with MaskFlaggedNames"
```

---

### Task 4: `user_identities` view and the shared identity mapper

**Files:**
- Create: `TelegramGroupsAdmin.Data/Models/UserIdentityView.cs`, migration `AddUserIdentitiesView`, `TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs`
- Modify: `TelegramGroupsAdmin.Data/AppDbContext.cs` (DbSet + keyless mapping next to `EnrichedReportView`, ~line 964), `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserRepository.cs`, `TelegramUserRepository.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Core/Repositories/Mappings/UserIdentityMappingTests.cs` (create), `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/UserIdentitiesViewTests.cs` (create)

**Interfaces:**
- Produces:
  - view `user_identities (telegram_user_id, first_name, last_name, username, is_bot, latest_scan_explicit)`; `latest_scan_explicit` is NULL when the user has no scan.
  - `public static UserIdentity UserIdentityMapping.ToIdentity(long id, string? firstName, string? lastName, string? username, bool isBot, bool? latestScanExplicit)` — the only verdict rule.
  - `Task<IReadOnlyList<UserIdentity>> ITelegramUserRepository.GetIdentitiesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)` — one identity per id found; ids with no row are absent.

**Canonical anchors (read-only):** `ScannedTwiceExplicitUserId` 9220500615182 @bagging_armado (scans 530 older, 534 newer; 534 explicit), `UnscannedUserId` 9063342700386 @Juvenileii (no scan rows), `BotUserId` 9742468412405 @doilyemcee.

- [ ] **Step 1: Write the failing tests**

Unit (mapper):
```csharp
[TestFixture]
public class UserIdentityMappingTests
{
    [TestCase(false, true, NameVerdict.Explicit)]
    [TestCase(false, false, NameVerdict.Clean)]
    [TestCase(false, null, NameVerdict.Unscanned)]
    [TestCase(true, true, NameVerdict.Unscanned)]   // bots are never judged
    public void ToIdentity_AppliesVerdictRule(bool isBot, bool? latestScanExplicit, NameVerdict expected)
    {
        var identity = UserIdentityMapping.ToIdentity(42, "A", null, null, isBot, latestScanExplicit);

        Assert.That(identity.Verdict, Is.EqualTo(expected));
        Assert.That(identity.DisplayName, Is.EqualTo("A"));
    }

    [Test]
    public void ToIdentity_SystemAccount_IsUnscanned()
    {
        var identity = UserIdentityMapping.ToIdentity(TelegramConstants.ServiceAccountUserId, "Telegram", null, null, false, true);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }
}
```
(Use the actual constant name for 777000 from `TelegramConstants`; read the file first.)

Integration (view + repository):
```csharp
[TestFixture]
public class UserIdentitiesViewTests : GoldenTestBase   // same base as TelegramUserRepositoryTests
{
    [Test]
    public async Task GetIdentitiesAsync_LatestScanWins_UnscannedAndBotsAreUnscanned()
    {
        var explicitId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
        var unscannedId = GoldenDatasetConstants.IdentityService.UnscannedUserId;
        var botId = GoldenDatasetConstants.IdentityService.BotUserId;
        await using var ctx = await CreateContextAsync();
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == explicitId), Is.EqualTo(2));
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == unscannedId), Is.Zero);
        Assert.That((await ctx.TelegramUsers.SingleAsync(u => u.TelegramUserId == botId)).IsBot, Is.True);

        var identities = await Repository.GetIdentitiesAsync([explicitId, unscannedId, botId, 1L]);

        Assert.That(identities.Select(i => i.Id), Is.EquivalentTo(new[] { explicitId, unscannedId, botId }));
        Assert.That(identities.Single(i => i.Id == explicitId).Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(identities.Single(i => i.Id == unscannedId).Verdict, Is.EqualTo(NameVerdict.Unscanned));
        Assert.That(identities.Single(i => i.Id == botId).Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task View_ExposesNullFlagForUnscannedUser()
    {
        await using var ctx = await CreateContextAsync();

        var row = await ctx.UserIdentities.SingleAsync(v => v.TelegramUserId == GoldenDatasetConstants.IdentityService.UnscannedUserId);

        Assert.That(row.LatestScanExplicit, Is.Null);
    }
}
```

Add the `GoldenDatasetConstants.IdentityService` class (all anchors this plan uses) and its `IntegrationTests/CLAUDE.md` recipe now:
```csharp
    /// <summary>Anchors for the user identity service tests (#552 part 1). No canonical rows were edited.</summary>
    public static class IdentityService
    {
        /// <summary>@bagging_armado: two scans (530 older, 534 newer); 534 has ai_explicit_display_text = true. Read-only: ProfileScanResultsRepositoryTests pins it.</summary>
        public const long ScannedTwiceExplicitUserId = 9220500615182;
        /// <summary>@Juvenileii: not trusted, not a bot, no scan rows, profile_scanned_at NULL.</summary>
        public const long UnscannedUserId = 9063342700386;
        /// <summary>@doilyemcee: the canonical bot. Read-only.</summary>
        public const long BotUserId = 9742468412405;
        /// <summary>@pastramiherbs: not trusted, active, no username_history rows.</summary>
        public const long UntrustedNoHistoryUserId = 9263051408340;
        /// <summary>@starlightskinless: trusted, not an admin.</summary>
        public const long TrustedUserId = 9006671634371;
        /// <summary>@violingentleman: not trusted, active, no history rows. Used by the row-lock race test.</summary>
        public const long RaceUserId = 9680301255238;
        /// <summary>@raceoutnumber: not trusted; user_photo_path and photo_hash both set.</summary>
        public const long PhotoUserId = 9264989724828;
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityMappingTests"`
Expected: build error, mapper missing.

- [ ] **Step 3: Implement**

`UserIdentityView.cs`:
```csharp
namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Keyless model of the user_identities view: a user's names plus the explicit flag from their
/// latest profile scan. Every identity read joins this view so the verdict comes from one place.
/// </summary>
public class UserIdentityView
{
    public const string CreateViewSql = """
        CREATE VIEW user_identities AS
        SELECT u.telegram_user_id, u.first_name, u.last_name, u.username, u.is_bot,
               s.ai_explicit_display_text AS latest_scan_explicit
        FROM telegram_users u
        LEFT JOIN LATERAL (
            SELECT r.ai_explicit_display_text
            FROM profile_scan_results r
            WHERE r.user_id = u.telegram_user_id
            ORDER BY r.scanned_at DESC
            LIMIT 1
        ) s ON true
        """;

    public const string DropViewSql = "DROP VIEW IF EXISTS user_identities";

    [Column("telegram_user_id")] public long TelegramUserId { get; set; }
    [Column("first_name")] public string? FirstName { get; set; }
    [Column("last_name")] public string? LastName { get; set; }
    [Column("username")] public string? Username { get; set; }
    [Column("is_bot")] public bool IsBot { get; set; }
    /// <summary>NULL when the user has no profile scan.</summary>
    [Column("latest_scan_explicit")] public bool? LatestScanExplicit { get; set; }
}
```
`AppDbContext`: `public DbSet<UserIdentityView> UserIdentities => Set<UserIdentityView>();` and
```csharp
        // Configure UserIdentityView as keyless entity mapping to user_identities view
        // Names + latest-scan flags: the single source for identity reads
        modelBuilder.Entity<UserIdentityView>()
            .HasNoKey()
            .ToView("user_identities");
```
Migration: `dotnet ef migrations add AddUserIdentitiesView --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`, then set `Up` to `migrationBuilder.Sql(UserIdentityView.CreateViewSql);` and `Down` to `migrationBuilder.Sql(UserIdentityView.DropViewSql);` (same pattern as `AddContentUserIdToEnrichedReportsView`). Remove anything EF scaffolded for the view itself.

`UserIdentityMapping.cs` (Core, so Core and Telegram mappings share it):
```csharp
namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// The one place a stored user row becomes a UserIdentity, and the one place the verdict rule lives.
/// </summary>
public static class UserIdentityMapping
{
    public static UserIdentity ToIdentity(
        long id, string? firstName, string? lastName, string? username, bool isBot, bool? latestScanExplicit) =>
        new(id, firstName, lastName, username)
        {
            Verdict = isBot || TelegramConstants.IsSystemUser(id) || latestScanExplicit is null
                ? NameVerdict.Unscanned
                : latestScanExplicit.Value ? NameVerdict.Explicit : NameVerdict.Clean
        };

    public static UserIdentity ToIdentity(this UserIdentityView row) =>
        ToIdentity(row.TelegramUserId, row.FirstName, row.LastName, row.Username, row.IsBot, row.LatestScanExplicit);
}
```
`TelegramUserRepository`:
```csharp
    public async Task<IReadOnlyList<UserIdentity>> GetIdentitiesAsync(
        IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0) return [];
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.UserIdentities
            .AsNoTracking()
            .Where(v => userIds.Contains(v.TelegramUserId))
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToIdentity()).ToList();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityMappingTests" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~UserIdentitiesViewTests"`
Expected: 0 warnings; PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(data): add user_identities view and the shared identity mapper"
```

---

### Task 5: `names_observed_at` and `GetOrUpdateAsync`

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/TelegramUserDto.cs`, `TelegramGroupsAdmin.Telegram/Repositories/ITelegramUserRepository.cs`, `TelegramUserRepository.cs`
- Create: `TelegramGroupsAdmin.Telegram/Models/ObservedUser.cs`, `ObservedNamesResult.cs`, migration `AddNamesObservedAt`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/TelegramUserRepositoryObserveTests.cs` (create)

**Interfaces:**
- Produces:
```csharp
public enum ObservationSource { BotUpdate = 0, ChatMember = 1, UserApiScan = 2 }

public sealed record ObservedUser(
    long Id, string? FirstName, string? LastName, string? Username, bool IsBot,
    ObservationSource Source, DateTimeOffset ObservedAt);

/// Context written into the ProfileChange audit row when a rename is recorded.
public sealed record ProfileChangeContext(ChatIdentity? Chat, int? MessageId);

public sealed record PreviousNames(string? FirstName, string? LastName, string? Username);

public sealed record ObservedNamesResult(UiModels.TelegramUser User, PreviousNames? Renamed);

Task<ObservedNamesResult> GetOrUpdateAsync(ObservedUser observed, ProfileChangeContext context, CancellationToken cancellationToken = default);
Task MarkActiveAsync(long telegramUserId, DateTimeOffset seenAt, CancellationToken cancellationToken = default);
```
- `GetOrCreateAsync` and `UpsertAsync` stay until Tasks 8/9 move their callers, then are deleted in Task 9.

**Canonical anchors:** UntrustedNoHistoryUserId (rename, no-op, ordering), RaceUserId (race), PhotoUserId (photo fields). Each test reads the row back first and asserts the precondition (no history rows; photo fields non-null).

- [ ] **Step 1: Add the column**

`TelegramUserDto`:
```csharp
    /// <summary>
    /// When the stored names were observed (message date, edit date, or scan fetch time).
    /// An observation older than this never overwrites the names.
    /// </summary>
    [Column("names_observed_at")]
    public DateTimeOffset? NamesObservedAt { get; set; }
```
Run: `dotnet ef migrations add AddNamesObservedAt --project TelegramGroupsAdmin.Data --startup-project TelegramGroupsAdmin`
Expected: migration adding a nullable `timestamp with time zone` column `names_observed_at`.

- [ ] **Step 2: Write the failing integration tests**

```csharp
[TestFixture]
public class TelegramUserRepositoryObserveTests : GoldenTestBase   // use the fixture base the other repository tests use
{
    private static ObservedUser Observe(long id, string? first, string? last, string? username, DateTimeOffset at) =>
        new(id, first, last, username, IsBot: false, ObservationSource.BotUpdate, at);

    [Test]
    public async Task Rename_UpdatesNames_WritesOneHistoryAndOneAuditRow()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = await CreateContextAsync();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);

        var result = await Repository.GetOrUpdateAsync(
            Observe(id, "Renamed", null, before.Username, DateTimeOffset.UtcNow),
            new ProfileChangeContext(Chat: null, MessageId: null));

        Assert.That(result.Renamed, Is.Not.Null);
        Assert.That(result.Renamed!.FirstName, Is.EqualTo(before.FirstName));
        Assert.That(result.User.FirstName, Is.EqualTo("Renamed"));
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
        Assert.That(await ctx.UserActions.CountAsync(a => a.UserId == id && a.ActionType == (int)UserActionType.ProfileChange), Is.EqualTo(1));
    }

    [Test]
    public async Task SameNames_WritesNothing()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = await CreateContextAsync();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);

        var result = await Repository.GetOrUpdateAsync(
            Observe(id, before.FirstName, before.LastName, before.Username, DateTimeOffset.UtcNow),
            new ProfileChangeContext(null, null));

        Assert.That(result.Renamed, Is.Null);
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
    }

    [Test]
    public async Task OlderObservation_IsIgnored()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        var t2 = DateTimeOffset.UtcNow;
        await Repository.GetOrUpdateAsync(Observe(id, "Newer", null, null, t2), new ProfileChangeContext(null, null));

        var result = await Repository.GetOrUpdateAsync(Observe(id, "Stale", null, null, t2.AddMinutes(-5)), new ProfileChangeContext(null, null));

        Assert.That(result.Renamed, Is.Null);
        Assert.That(result.User.FirstName, Is.EqualTo("Newer"));
        await using var ctx = await CreateContextAsync();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
    }

    [Test]
    public async Task RenameThereAndBack_RecordsBothChanges()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = await CreateContextAsync();
        var original = (await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id)).FirstName;
        var t = DateTimeOffset.UtcNow;

        await Repository.GetOrUpdateAsync(Observe(id, "B", null, null, t), new ProfileChangeContext(null, null));
        var back = await Repository.GetOrUpdateAsync(Observe(id, original, null, null, t.AddSeconds(1)), new ProfileChangeContext(null, null));

        Assert.That(back.Renamed, Is.Not.Null);
        Assert.That(back.User.FirstName, Is.EqualTo(original));
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(2));
    }

    [Test]
    public async Task NameUpdate_LeavesPhotoFieldsUntouched()
    {
        var id = GoldenDatasetConstants.IdentityService.PhotoUserId;
        await using var ctx = await CreateContextAsync();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.UserPhotoPath, Is.Not.Null);
        Assert.That(before.PhotoHash, Is.Not.Null);

        await Repository.GetOrUpdateAsync(Observe(id, "Renamed", null, before.Username, DateTimeOffset.UtcNow), new ProfileChangeContext(null, null));

        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(after.UserPhotoPath, Is.EqualTo(before.UserPhotoPath));
        Assert.That(after.PhotoHash, Is.EqualTo(before.PhotoHash));
    }

    [Test]
    public async Task ConcurrentRenames_ExactlyOneRecordsTheRename()
    {
        var id = GoldenDatasetConstants.IdentityService.RaceUserId;
        await using var holder = new NpgsqlConnection(ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM telegram_users WHERE telegram_user_id = @id FOR UPDATE", holder, tx))
        {
            lockCmd.Parameters.AddWithValue("id", id);
            await lockCmd.ExecuteScalarAsync();
        }

        var at = DateTimeOffset.UtcNow;
        var first = Repository.GetOrUpdateAsync(Observe(id, "Racer", null, null, at), new ProfileChangeContext(null, null));
        var second = Repository.GetOrUpdateAsync(Observe(id, "Racer", null, null, at), new ProfileChangeContext(null, null));

        await WaitForLockWaitersAsync(expected: 2);  // polls pg_stat_activity, no sleeps
        await tx.CommitAsync();

        var results = await Task.WhenAll(first, second);
        Assert.That(results.Count(r => r.Renamed is not null), Is.EqualTo(1));
        await using var ctx = await CreateContextAsync();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
    }

    private async Task WaitForLockWaitersAsync(int expected)
    {
        await using var probe = new NpgsqlConnection(ConnectionString);
        await probe.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", probe);
            if ((long)(await cmd.ExecuteScalarAsync())! >= expected) return;
            await Task.Yield();
        }
        Assert.Fail($"Expected {expected} lock waiters");
    }
}
```
Use the base class, `Repository`, `CreateContextAsync` and `ConnectionString` names from the existing `TelegramUserRepositoryTests` fixture (read it first and adapt the names; do not create a new harness).

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryObserveTests"`
Expected: build error, `GetOrUpdateAsync` missing.

- [ ] **Step 4: Implement `GetOrUpdateAsync` and `MarkActiveAsync`**

```csharp
    public async Task<ObservedNamesResult> GetOrUpdateAsync(
        ObservedUser observed, ProfileChangeContext changeContext, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isTrusted = TelegramConstants.IsSystemUser(observed.Id);

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO telegram_users (
                telegram_user_id, username, first_name, last_name, names_observed_at,
                is_bot, is_trusted, is_banned, bot_dm_enabled,
                first_seen_at, last_seen_at, created_at, updated_at, is_active
            ) VALUES (
                {observed.Id}, {observed.Username}, {observed.FirstName}, {observed.LastName}, {observed.ObservedAt},
                {observed.IsBot}, {isTrusted}, {false}, {false},
                {now}, {now}, {now}, {now}, {false}
            )
            ON CONFLICT (telegram_user_id) DO NOTHING
            """, cancellationToken);

        // The row lock serializes concurrent writers; the loser re-checks the WHERE against the
        // committed row, so only the statement that changed the names returns a row.
        var previous = await context.Database.SqlQuery<PreviousNamesRow>($"""
            UPDATE telegram_users t
            SET first_name = {observed.FirstName},
                last_name = {observed.LastName},
                username = {observed.Username},
                names_observed_at = {observed.ObservedAt},
                updated_at = {now}
            FROM (SELECT telegram_user_id, first_name, last_name, username
                  FROM telegram_users WHERE telegram_user_id = {observed.Id} FOR UPDATE) old
            WHERE t.telegram_user_id = old.telegram_user_id
              AND (t.names_observed_at IS NULL OR {observed.ObservedAt} >= t.names_observed_at)
              AND (t.first_name IS DISTINCT FROM {observed.FirstName}
                   OR t.last_name IS DISTINCT FROM {observed.LastName}
                   OR t.username IS DISTINCT FROM {observed.Username})
            RETURNING old.first_name AS "FirstName", old.last_name AS "LastName", old.username AS "Username"
            """).ToListAsync(cancellationToken);

        PreviousNames? renamed = null;
        if (previous.Count == 1)
        {
            var old = previous[0];
            renamed = new PreviousNames(old.FirstName, old.LastName, old.Username);

            context.UsernameHistory.Add(new UsernameHistoryDto
            {
                UserId = observed.Id,
                Username = old.Username,
                FirstName = old.FirstName,
                LastName = old.LastName,
                RecordedAt = now
            });

            var reason = ProfileChangeReason.Build(renamed, observed);
            context.UserActions.Add(new UserActionRecord(
                Id: 0,
                UserId: observed.Id,
                ActionType: UserActionType.ProfileChange,
                MessageId: changeContext.MessageId,
                ChatId: changeContext.Chat?.Id,
                IssuedBy: Actor.ProfileDiffDetection,
                IssuedAt: now,
                ExpiresAt: null,
                Reason: changeContext.Chat is { } chat ? AuditReason.WithChatTag(chat, reason) : reason).ToDto());

            await context.SaveChangesAsync(cancellationToken);
        }

        // Keep a never-set guard from blocking future updates when names were already equal.
        await context.Database.ExecuteSqlAsync($"""
            UPDATE telegram_users SET names_observed_at = {observed.ObservedAt}
            WHERE telegram_user_id = {observed.Id}
              AND (names_observed_at IS NULL OR names_observed_at < {observed.ObservedAt})
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var entity = await context.TelegramUsers.AsNoTracking()
            .FirstAsync(u => u.TelegramUserId == observed.Id, cancellationToken);
        return new ObservedNamesResult(entity.ToModel(), renamed);
    }

    private sealed record PreviousNamesRow(string? FirstName, string? LastName, string? Username);

    public async Task MarkActiveAsync(long telegramUserId, DateTimeOffset seenAt, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlAsync($"""
            UPDATE telegram_users
            SET is_active = true, last_seen_at = {seenAt}, updated_at = {DateTimeOffset.UtcNow}
            WHERE telegram_user_id = {telegramUserId}
            """, cancellationToken);
    }
```
Move `MessageProcessingService.BuildProfileChangeReason` (lines 884-898) to a new `TelegramGroupsAdmin.Telegram/Helpers/ProfileChangeReason.cs` as `internal static string Build(PreviousNames old, ObservedUser current)` with the same output format; update its existing unit tests (`grep -rn BuildProfileChangeReason TelegramGroupsAdmin.UnitTests`) to call `ProfileChangeReason.Build`. `UserActionRecord.ToDto()` is the existing mapping used by `UserActionsRepository.InsertAsync`.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryObserveTests" && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileChangeReason"`
Expected: PASS (6 integration tests, existing reason tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(telegram): record observed names with newest-observation-wins and rename audit"
```

---

### Task 6: `IUserIdentityService`

**Files:**
- Create: `TelegramGroupsAdmin.Telegram/Services/Identity/IUserIdentityService.cs`, `UserIdentityService.cs`, `RenameRescan.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` (register scoped)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Identity/UserIdentityServiceTests.cs` (create)

**Interfaces:**
- Consumes: `GetOrUpdateAsync` (Task 5), `GetIdentitiesAsync` (Task 4), `IProfileScanGate.ScanIfEligibleAsync(…, forceRescan)` (Task 7 adds the parameter; if executing in order, add the optional parameter to the gate interface and implementation in this task's Step 3 and let Task 7 add its tests).
- Produces:
```csharp
/// Whether ObserveAsync rescans the profile inline when it records a rename.
public enum RenameRescan { None = 0, Inline = 1 }

public interface IUserIdentityService
{
    Task<UserIdentity> ObserveAsync(ObservedUser observed, ProfileChangeContext context, RenameRescan rescan, CancellationToken ct = default);
    Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[TestFixture]
public class UserIdentityServiceTests
{
    private static readonly ChatIdentity Chat = new(-100, "Chat");
    private ITelegramUserRepository _users = null!;
    private IProfileScanGate _gate = null!;
    private UserIdentityService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<ITelegramUserRepository>();
        _gate = Substitute.For<IProfileScanGate>();
        _sut = new UserIdentityService(_users, _gate, Substitute.For<ILogger<UserIdentityService>>());
    }

    private static TelegramUser Row(long id, string first, bool trusted = false, bool bot = false) =>
        TestTelegramUsers.Create(id, first, isTrusted: trusted, isBot: bot);   // use the existing TelegramUser test factory in UnitTests (grep for it); add one there if none exists

    private static ObservedUser Observed(long id, string first) =>
        new(id, first, null, null, IsBot: false, ObservationSource.BotUpdate, DateTimeOffset.UtcNow);

    private void IdentityRow(long id, string first, NameVerdict verdict) =>
        _users.GetIdentitiesAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids!.Contains(id)), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(id, first, verdict: verdict)]);

    private void Renamed(TelegramUser row) =>
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(row, new PreviousNames("Old", null, null)));

    [Test]
    public async Task Resolve_ReturnsIdentityFromView()
    {
        IdentityRow(7, "Bad", NameVerdict.Explicit);

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(identity.DisplayName, Is.EqualTo("Bad"));
    }

    [Test]
    public async Task Resolve_UnknownId_GivesIdOnlyUnscanned()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns([]);

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.DisplayName, Is.EqualTo("User 7"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task ResolveMany_KeepsRequestedOrder_AndFillsUnknownIds()
    {
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(2, "B"), UserIdentity.ForTest(1, "A")]);

        var result = await _sut.ResolveManyAsync([1, 2, 3]);

        Assert.That(result.Select(i => i.Id), Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(result[2].DisplayName, Is.EqualTo("User 3"));
        await _users.Received(1).GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Observe_RenameOfUntrustedUser_Inline_RescansThroughGate()
    {
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), RenameRescan.Inline);

        await _gate.Received(1).ScanIfEligibleAsync(
            Arg.Is<UserIdentity>(u => u!.Id == 7), Chat, ProfileScanTrigger.ProfileChange,
            Arg.Any<CancellationToken>(), forceRescan: true);
    }

    [Test]
    public async Task Observe_RenameWithRescanNone_DoesNotScan()
    {
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, null), RenameRescan.None);

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_RenameOfTrustedUser_DoesNotScan()
    {
        Renamed(Row(7, "New", trusted: true));
        IdentityRow(7, "New", NameVerdict.Unscanned);

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), RenameRescan.Inline);

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_NoRename_DoesNotScan()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(Row(7, "Same"), Renamed: null));
        IdentityRow(7, "Same", NameVerdict.Clean);

        await _sut.ObserveAsync(Observed(7, "Same"), new ProfileChangeContext(Chat, 5), RenameRescan.Inline);

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
    }

    [Test]
    public async Task Observe_AfterRescan_ReturnsNewVerdict()
    {
        Renamed(Row(7, "New"));
        _users.GetIdentitiesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([UserIdentity.ForTest(7, "New", verdict: NameVerdict.Clean)],
                     [UserIdentity.ForTest(7, "New", verdict: NameVerdict.Explicit)]);

        var identity = await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), RenameRescan.Inline);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }

    [Test]
    public async Task Observe_RescanThrows_StillReturnsIdentity()
    {
        Renamed(Row(7, "New"));
        IdentityRow(7, "New", NameVerdict.Clean);
        _gate.ScanIfEligibleAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs<ProfileScanResult?>(_ => throw new InvalidOperationException("scan failed"));

        var identity = await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(Chat, 5), RenameRescan.Inline);

        Assert.That(identity.Id, Is.EqualTo(7));
    }

    [Test]
    public async Task Observe_RepositoryThrows_ReturnsObservedNamesUnscanned()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns<ObservedNamesResult>(_ => throw new InvalidOperationException("db down"));

        var identity = await _sut.ObserveAsync(Observed(7, "Seen"), new ProfileChangeContext(null, null), RenameRescan.Inline);

        Assert.That(identity.DisplayName, Is.EqualTo("Seen"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityServiceTests"`
Expected: build error.

- [ ] **Step 3: Implement**

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Identity;

/// <summary>
/// The single way to obtain a UserIdentity for bot-facing work. Records observed names (newest
/// observation wins) and reads identities from the user_identities view. Stateless.
/// </summary>
public sealed class UserIdentityService(
    ITelegramUserRepository users,
    IProfileScanGate scanGate,
    ILogger<UserIdentityService> logger) : IUserIdentityService
{
    public async Task<UserIdentity> ObserveAsync(
        ObservedUser observed, ProfileChangeContext context, RenameRescan rescan, CancellationToken ct = default)
    {
        ObservedNamesResult result;
        try
        {
            result = await users.GetOrUpdateAsync(observed, context, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Recording what we saw must never cost the update its moderation.
            logger.LogError(ex, "Failed to record observed names for user {UserId}", observed.Id);
            return new UserIdentity(observed.Id, observed.FirstName, observed.LastName, observed.Username);
        }

        var identity = await ResolveAsync(observed.Id, ct);

        // Inline, as the message pipeline did before: a profile ban stays inside this update's
        // context and its existing cleanup path. Renames are rare, so the stall is rare.
        if (rescan == RenameRescan.Inline && result.Renamed is not null
            && !result.User.IsTrusted && !result.User.IsBot && !TelegramConstants.IsSystemUser(observed.Id))
        {
            try
            {
                await scanGate.ScanIfEligibleAsync(identity, context.Chat, ProfileScanTrigger.ProfileChange, ct, forceRescan: true);
                identity = await ResolveAsync(observed.Id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Profile rescan after rename failed for user {UserId}", observed.Id);
            }
        }

        return identity;
    }

    public async Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default) =>
        (await ResolveManyAsync([userId], ct))[0];

    public async Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default)
    {
        var found = (await users.GetIdentitiesAsync(userIds, ct)).ToDictionary(i => i.Id);
        return userIds.Select(id => found.TryGetValue(id, out var identity) ? identity : UserIdentity.FromId(id)).ToList();
    }
}
```
Register in `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` next to the other scoped services: `services.AddScoped<IUserIdentityService, UserIdentityService>();`. `ProfileScanService` (singleton) reaches the identity service through its own scope (Task 10), so there is no constructor cycle with `IProfileScanGate`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityServiceTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(telegram): add user identity service"
```

---

### Task 7: Scan single-flight and freshness bypass

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScanService.cs`, `ProfileScanService.cs:50-92`, `IProfileScanGate.cs`, `ProfileScanGate.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanServiceSingleFlightTests.cs` (create), existing `ProfileScanGate` tests

**Interfaces:**
- Produces: `IProfileScanService.ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool forceRescan = false)`; `IProfileScanGate.ScanIfEligibleAsync(UserIdentity user, ChatIdentity? chat, ProfileScanTrigger trigger, CancellationToken ct, bool forceRescan = false)`.

- [ ] **Step 1: Write the failing tests**

Use the substitutes and construction the existing `ProfileScanService` unit tests use (`grep -rln "new ProfileScanService(" TelegramGroupsAdmin.UnitTests`); match `_sessions` / `_users` to them. The core scan is reached through `ITelegramSessionManager`: a `GetAnyClientAsync` that awaits a `TaskCompletionSource` and then returns null yields `EmptyResult`, which is enough to observe how many scans ran.

```csharp
    [Test]
    public async Task ConcurrentScansForOneUser_ShareOneRun()
    {
        var gate = new TaskCompletionSource<WTelegram.Client?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");

        var first = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        var second = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        gate.SetResult(null);
        await Task.WhenAll(first, second);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
        Assert.That(await second, Is.SameAs(await first));
    }

    [Test]
    public async Task BypassFreshness_ScansEvenWhenRecentlyScanned()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(TestTelegramUsers.Create(7, "A") with { ProfileScannedAt = DateTimeOffset.UtcNow, ProfileScanScore = 1m });
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((WTelegram.Client?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None, forceRescan: true);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithoutBypass_RecentlyScanned_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(TestTelegramUsers.Create(7, "A") with { ProfileScannedAt = DateTimeOffset.UtcNow, ProfileScanScore = 1m });

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None);

        await _sessions.DidNotReceiveWithAnyArgs().GetAnyClientAsync(default);
    }
```
Gate test (existing gate test file): `ScanIfEligibleAsync(…, forceRescan: true)` forwards `forceRescan: true` to `ScanUserProfileAsync`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanServiceSingleFlightTests|FullyQualifiedName~ProfileScanGate"`
Expected: build errors (no `forceRescan` parameter).

- [ ] **Step 3: Implement**

`ProfileScanService`:
```csharp
    // Concurrent requests for one user share one scan. The 60s freshness window alone cannot do
    // this: profile_scanned_at is written only when a scan finishes.
    private readonly ConcurrentDictionary<long, Lazy<Task<ProfileScanResult>>> _inFlight = new();

    public async Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool forceRescan = false)
    {
        var lazy = _inFlight.GetOrAdd(user.Id, _ => new Lazy<Task<ProfileScanResult>>(
            () => ScanOnceAsync(user, triggeringChat, forceRescan, ct)));
        try
        {
            return await lazy.Value;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<long, Lazy<Task<ProfileScanResult>>>(user.Id, lazy));
        }
    }
```
Rename the current method body to `private async Task<ProfileScanResult> ScanOnceAsync(UserIdentity user, ChatIdentity? triggeringChat, bool forceRescan, CancellationToken ct)` and change the freshness condition at line 68 to `if (!forceRescan && existingUser?.ProfileScannedAt is { } lastScan && …)`. Thread `forceRescan` through `IProfileScanGate` / `ProfileScanGate` to `ScanUserProfileAsync`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScan"`
Expected: 0 warnings; PASS, including existing ProfileScanService and gate tests (update `Received` calls for the new optional parameter where NSubstitute needs it).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix(profile-scan): share in-flight scans and let rename rescans bypass the freshness window"
```

---

### Task 8: Message pipeline observes first

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BackgroundServices/MessageProcessingService.cs` (entry of `HandleNewMessageAsync` at :63; remove :645-717 block and `ProfileDiffDetected` :874-879; the first-message scan at :724-746 keeps running)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BackgroundServices/MessageProcessingServiceObserveTests.cs` (create, following the existing `MessageProcessingService` unit-test construction: `grep -rln "MessageProcessingService(" TelegramGroupsAdmin.UnitTests`)

**Interfaces:**
- Consumes: `IUserIdentityService.ObserveAsync`, `ITelegramUserRepository.MarkActiveAsync`.
- Produces: one `UserIdentity sender` local for the rest of `HandleNewMessageAsync`, used instead of every `UserIdentity.From(message.From)` in this file (:108, :213, :522, :592, :603, :706, :732).

- [ ] **Step 1: Write the failing tests**

```csharp
    [Test]
    public async Task NewMessage_ObservesSenderBeforeRoutingCommands()
    {
        var order = new List<string>();
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<RenameRescan>(), Arg.Any<CancellationToken>())
            .Returns(ci => { order.Add("observe"); return UserIdentity.ForTest(ci.Arg<ObservedUser>().Id, "A"); });
        _commandRouter.IsCommand(Arg.Any<Message>()).Returns(ci => { order.Add("command"); return false; });

        await _sut.HandleNewMessageAsync(TextMessage(from: 7, text: "hi"), CancellationToken.None);

        Assert.That(order.First(), Is.EqualTo("observe"));
    }

    [Test]
    public async Task NewMessage_ObservationCarriesMessageDateAndChatContext()
    {
        var message = TextMessage(from: 7, text: "hi");

        await _sut.HandleNewMessageAsync(message, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == 7 && o.ObservedAt == message.Date && o.Source == ObservationSource.BotUpdate),
            Arg.Is<ProfileChangeContext>(c => c!.Chat!.Id == message.Chat.Id && c.MessageId == message.MessageId),
            RenameRescan.Inline,
            Arg.Any<CancellationToken>());
        await _users.Received(1).MarkActiveAsync(7, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _users.DidNotReceiveWithAnyArgs().UpsertAsync(default!);
    }
```
(`message.Date` is a `DateTime` in Telegram.Bot; convert with `new DateTimeOffset(DateTime.SpecifyKind(message.Date, DateTimeKind.Utc))` in both the code and the assertion helper. Use the method names and command-routing seam the existing tests use; adapt `IsCommand` to the real seam.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MessageProcessingServiceObserveTests"`
Expected: FAIL (observe not called / upsert still called).

- [ ] **Step 3: Implement**

At the top of `HandleNewMessageAsync`, after the null checks and before command routing:
```csharp
            var identityService = messageScope.ServiceProvider.GetRequiredService<IUserIdentityService>();
            var observedAt = new DateTimeOffset(DateTime.SpecifyKind(message.Date, DateTimeKind.Utc));
            var sender = await identityService.ObserveAsync(
                new ObservedUser(message.From!.Id, message.From.FirstName, message.From.LastName, message.From.Username,
                    message.From.IsBot, ObservationSource.BotUpdate, observedAt),
                new ProfileChangeContext(ChatIdentity.From(message.Chat), message.MessageId),
                RenameRescan.Inline,
                cancellationToken);
```
(If the scope is created later in the method, move this to the first point a scope exists, still before command routing; the order test pins it.)

Delete the block from "Upsert user into telegram_users table" through `await telegramUserRepo.UpsertAsync(telegramUser, cancellationToken);` (:645-717) and replace it with:
```csharp
            await messageScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
                .MarkActiveAsync(message.From!.Id, observedAt, cancellationToken);
```
Delete `ProfileDiffDetected`, `BuildProfileChangeReason` (moved in Task 5) and `LogProfileChangeDetected` if now unused. Replace each `UserIdentity.From(message.From)` / hand-rolled `new Core.Models.UserIdentity(message.From…)` in this method with `sender`. Keep `UserIdentity.From(message.From!)` in the ban-race block (:592, :603) replaced by `sender` too.

- [ ] **Step 4: Run tests**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MessageProcessingService" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~MessageProcessing"`
Expected: PASS. Existing tests that asserted `UpsertAsync` or username-history writes from the pipeline move to asserting `ObserveAsync` / `MarkActiveAsync`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(pipeline): observe the sender first and pass one identity down"
```

---

### Task 9: Edited messages, joins, callbacks, chat-member updates; remove old writes

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/MessageEditProcessor.cs`, the callback entry (`grep -rn "CallbackQuery" TelegramGroupsAdmin.Telegram/Services/UpdateRouter.cs` → its handler), `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs:80,141` (join) and `:713` (callback), `TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs:184,288,453`, `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/BanCelebrationSubscriptionService.cs:38`, `TelegramGroupsAdmin.Telegram/Services/BotProtectionService.cs:109`, `TelegramGroupsAdmin.Telegram/Services/Bot/BotMessageService.cs:117,363,467`, `ITelegramUserRepository` / `TelegramUserRepository` (delete `GetOrCreateAsync`, `UpsertAsync`)
- Test: update existing tests of each modified class; add one per entry point asserting `ObserveAsync` with the right `ObservationSource` and timestamp.

**Interfaces:**
- Consumes: `IUserIdentityService.ObserveAsync`, `ResolveAsync`.

Only entry points that rescan (or scan right after) record names; recording a new name uses up the rename, so everything else resolves by id.

| Site | Call | Timestamp | `RenameRescan` |
|---|---|---|---|
| `MessageEditProcessor` | `ObserveAsync(editedMessage.From)` | `EditDate ?? Date` | `Inline` |
| `WelcomeService` join (:80, :141 `GetOrCreateAsync`) | `ObserveAsync(chatMemberUpdate.NewChatMember.User)`, `ObservationSource.ChatMember` | `chatMemberUpdate.Date` | `None` (the join scan runs right after) |
| Callback handlers (`WelcomeService` :713, report/ban callbacks) | `ResolveAsync(callbackQuery.From.Id)` | — | — |
| `BotChatService` :184, :288 (chat-member changes other than join) | `ResolveAsync(id)` | — | — |
| `BotChatService` :453 (admin refresh via `getChatAdministrators`) | `ObserveAsync(ChatMember.User)`, `ObservationSource.ChatMember` | `DateTimeOffset.UtcNow` | `None` (admins are trusted) |
| `BanCelebrationSubscriptionService` :38 | `ResolveAsync(ChatMemberUpdated.From.Id)` | — | — |
| `BotProtectionService` :109 | `ObserveAsync(bot User)`, `IsBot = true` | update `Date` | `None` |
| `BotMessageService` :117/:363/:467 | `ObserveAsync(own bot User from GetMe)`, `IsBot = true` | `DateTimeOffset.UtcNow` | `None` |

For `ResolveAsync` sites with no row yet (a first-time user), `ResolveAsync` returns an id-only identity; where the old code used `GetOrCreateAsync` to guarantee the row for foreign keys, use `ObserveAsync(..., RenameRescan.None)` instead and say so in the commit message. Each site uses the returned identity in place of the `UserIdentity.From(...)` it built before.

- [ ] **Step 1: Write the failing tests** — one per row above, in each class's existing test file, asserting the call, timestamp and `RenameRescan` from the table (`ResolveAsync` rows assert `ObserveAsync` is not called), of this shape:

```csharp
    [Test]
    public async Task EditedMessage_ObservesEditorWithEditDate()
    {
        var edited = EditedTextMessage(from: 7, editDate: new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));

        await _sut.ProcessEditAsync(edited, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == 7 && o.ObservedAt == new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)),
            Arg.Any<ProfileChangeContext>(), RenameRescan.Inline, Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~MessageEditProcessor|FullyQualifiedName~WelcomeService|FullyQualifiedName~BotChatService|FullyQualifiedName~BanCelebrationSubscription|FullyQualifiedName~BotProtection|FullyQualifiedName~BotMessageService"`
Expected: the new tests FAIL.

- [ ] **Step 3: Implement** each row, then delete `GetOrCreateAsync` and `UpsertAsync` from `ITelegramUserRepository` and `TelegramUserRepository`. The build lists any caller left; there must be none.

- [ ] **Step 4: Run tests**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: 0 warnings; all PASS. Integration tests that used `GetOrCreateAsync`/`UpsertAsync` as setup were already rule violations; rewrite each to a canonical anchor (do not reintroduce a seeding write).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(telegram): observe names at every entry point and drop GetOrCreate/Upsert"
```

---

### Task 10: Profile scan records live names

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` (:61-65 enrichment, after the MTProto user fetch near :358)
- Test: `ProfileScanServiceSingleFlightTests.cs` or the existing ProfileScanService tests

- [ ] **Step 1: Write the failing test**: when the MTProto fetch returns names, `ObserveAsync` is called with `ObservationSource.UserApiScan` and those names; when the caller passed an id-only identity, the scan uses `IUserIdentityService.ResolveAsync` instead of `UserIdentity.From(existingUser)`.

```csharp
    [Test]
    public async Task Scan_IdOnlyIdentity_ResolvesThroughIdentityService()
    {
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(UserIdentity.ForTest(7, "Stored"));
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((WTelegram.Client?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.FromId(7), null, CancellationToken.None);

        await _identities.Received(1).ResolveAsync(7, Arg.Any<CancellationToken>());
    }
```
The live-names path needs a WTelegram client and is covered by reading: add the `ObserveAsync` call right after `tlUser` is fetched, guarded by `tlUser is not null`, with `ObservedAt = DateTimeOffset.UtcNow`, `ObservationSource.UserApiScan`, `new ProfileChangeContext(triggeringChat, null)` and `RenameRescan.None` (the scan must not trigger itself); failures are already swallowed by the service. `ProfileScanService` is a singleton: resolve `IUserIdentityService` from the scope it already creates (`scope.ServiceProvider`), never inject it.

- [ ] **Step 2: Run to verify it fails**, **Step 3: implement**, **Step 4: run** `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScan"` (Expected: PASS), **Step 5: commit** `refactor(profile-scan): resolve and record names through the identity service`.

---

### Task 11: Welcome, exam and ban celebration

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` (29 `From(user)` sites listed below), `ExamFlowService.cs` (14 sites), `Welcome/WelcomeMessageBuilder.cs:87,100`, `Welcome/ExamMessageBuilder.cs:20,30`, `BotCommands/Commands/StartCommand.cs:120,180,332`, `Services/BanCelebrationService.cs:78-115,263,297-302`, `Components/Shared/ExamConfigEditor.razor:375,392`, `Components/Shared/WelcomeSystemConfig.razor:645,672`
- Test: existing `WelcomeServiceTests`, `ExamFlowServiceTests`, `WelcomeMessageBuilderTests`, `ExamMessageBuilderTests`, `BanCelebrationServiceTests` (unit + integration)

Rules for this area:
- Each flow observes once at its entry (Task 9 already did join and callback) and passes that identity into every helper. Helper signatures change from `User user` to `UserIdentity user` where they only used the user for identity; where they also need SDK fields (e.g. `IsBot`), keep both parameters.
- Every builder writing into a chat is created with `TelegramMessageBuilder.For(await configService.GetNameMaskingAsync(chat.Id, ct))`; DM-only text uses `GetNameMaskingAsync(null, ct)`. `WelcomeMessageBuilder` / `ExamMessageBuilder` gain a `NameMasking masking` parameter and create their builders with `For(masking)`.
- `BanCelebrationService`: delete the explicit-flag lookup (:78-105) and `scanRepository` dependency; use `var masking = await configService.GetNameMaskingAsync(chat.Id, ct); var displayedName = bannedUser.BotDisplayName(masking);` where `bannedUser` comes from `identityService.ResolveAsync(bannedUser.Id, ct)` at the top of `SendBanCelebrationAsync`. Keep `pipelineMetrics.RecordMaskedUsername(...)` when `displayedName != bannedUser.DisplayName`.
- Settings previews use `UserIdentity.ForPreview(...)` and `NameMasking.Off`.

WelcomeService sites: :142, 155, 168, 176, 197, 209, 276, 397, 420, 452, 484, 538, 705, 823, 936, 987, 1085, 1104, 1234, 1252, 1269, 1273, 1323, 1343, 1348, 1383 (×2), 1385 (×2). ExamFlowService sites: :114, 175, 191, 196, 197, 514, 537, 557, 581, 594, 614, 630, 640, 643.

- [ ] **Step 1: Write the failing tests**

```csharp
    // WelcomeMessageBuilderTests
    [Test]
    public void BuildFromTemplate_ExplicitUserWithMaskingOn_ShowsLabelAsMention()
    {
        var user = UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit);

        var msg = WelcomeMessageBuilder.BuildFromTemplate("Welcome {username}!", user, chatName: "Chat", NameMasking.On);

        Assert.That(msg.Text, Is.EqualTo("Welcome [name removed: explicit]!"));
        Assert.That(msg.Entities.Single().User!.Id, Is.EqualTo(7));
    }

    // BanCelebrationServiceTests (unit)
    [Test]
    public async Task Celebration_ExplicitVerdictMaskingOn_CaptionShowsLabel_ChatAndFanout()
    {
        _identities.ResolveAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(TestUserId, "Bad", "User", verdict: NameVerdict.Explicit));
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);
        _subscribers.HasDeliverableSubscribersAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(true);
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        await _messages.Received(1).SendAndSaveAnimationAsync(TestChatId, Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text == "[name removed: explicit] got banned!"), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueBanCelebrationAsync(TestChat, "[name removed: explicit] got banned!", 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Celebration_ExplicitVerdictMaskingOff_CaptionShowsName()
    {
        _identities.ResolveAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(UserIdentity.ForTest(TestUserId, "Bad", "User", verdict: NameVerdict.Explicit));
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.Off);
        SeedOneGifAndOneCaption("{username} got banned!");

        await _sut.SendBanCelebrationAsync(TestChat, TestBannedUser, isAutoBan: true);

        await _messages.Received(1).SendAndSaveAnimationAsync(TestChatId, Arg.Any<InputFile>(),
            Arg.Is<TelegramMessage>(m => m!.Text == "Bad User got banned!"), Arg.Any<CancellationToken>());
    }
```
Replace the old masking tests in `BanCelebrationServiceTests` (unit and integration) that drove `MaskExplicitUsername` and the scan repository; the integration test for a celebration uses anchor `ScannedTwiceExplicitUserId` with the real identity service and asserts the caption contains `[name removed: explicit]` when the canonical global config has profile scanning enabled (read the global `welcome_config` first and assert `profileScan.enabled`; if it is not enabled, assert the real name instead and say so in the test name).

- [ ] **Step 2: Run to verify they fail**: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~WelcomeMessageBuilder|FullyQualifiedName~ExamMessageBuilder|FullyQualifiedName~BanCelebrationServiceTests"` — Expected: build errors / FAIL.
- [ ] **Step 3: Implement** per the rules and site lists.
- [ ] **Step 4: Run**: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~Welcome|FullyQualifiedName~Exam|FullyQualifiedName~BanCelebration"` — Expected: 0 warnings, PASS.
- [ ] **Step 5: Commit** `refactor(welcome): use resolved identities and name masking in welcome, exam and celebrations`.

---

### Task 12: Commands, moderation and detection

**Files (sites from the call-site survey):**
- Commands (`TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/`): `BanCommand.cs:72,88,99,115`, `MuteCommand.cs:102`, `TempBanCommand.cs:103`, `UnbanCommand.cs:65`, `WarnCommand.cs:86,130`, `TrustCommand.cs:102,133`, `SpamCommand.cs:90`, `ReportCommand.cs:117`, `DmCelebrationsCommand.cs:51`
- Handlers/detection: `Handlers/ContentDetectionOrchestrator.cs:94`, `Handlers/LanguageWarningHandler.cs:95`, `Handlers/FileScanningHandler.cs:122`, `BackgroundServices/DetectionActionService.cs:96,128,232`, `Moderation/Infrastructure/MessageBackfillService.cs:62`
- Callbacks/moderation: `BanCallbackService.cs:140`, `BotProtectionService.cs:115`, `ImpersonationDetectionService.cs:240,241,267`, `ReportService.cs:71`
- Bot services: `Bot/BotChatService.cs:197,332,340,350,466`, `UserMessagingService.cs:87,123`

Rules:
- The command's caller identity comes from the pipeline (`sender`, Task 8): add a `UserIdentity sender` member to the command execution context the router passes (read `CommandRouter` and `IBotCommand.ExecuteAsync`; add the parameter there once) and use it for the `Actor`/executor.
- Reply targets and other SDK users: `await identityService.ResolveAsync(u.Id, ct)`. Every message was observed when it arrived, so the stored names are current.
- DB lookups (`BanCommand:88,99,115`, `BanCallbackService:140`, `UserMessagingService:87,123`): `identityService.ResolveAsync(row.TelegramUserId, ct)`.
- Detection paths that only need the id for logging may keep the pipeline's `sender` passed down (`ContentDetectionOrchestrator.RunDetectionAsync` gains a `UserIdentity sender` parameter).
- Every `new TelegramMessageBuilder()` in these files becomes `TelegramMessageBuilder.For(await configService.GetNameMaskingAsync(chatId, ct))`; DMs use `null`.
- `WarnCommand` confirmation (:124-138) moves from plain `@username`/`FirstName` text to `.Mention(target)` through the builder, so it follows the masking setting.

- [ ] **Step 1: Failing tests** — for each command, update its test to pass `sender` and assert the executor/target identity comes from the identity service; add `WarnCommandTests` (create) with:

```csharp
    [Test]
    public async Task Warn_confirmation_mentions_target_with_masking()
    {
        var target = UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(target);
        _config.GetNameMaskingAsync(TestChatId, Arg.Any<CancellationToken>()).Returns(NameMasking.On);

        var result = await _command.ExecuteAsync(WarnReplyTo(7), ["spam"], PermissionLevel.Admin, Sender);

        Assert.That(result.Message.Text, Does.StartWith("⚠️ Warning issued to [name removed: explicit]"));
        Assert.That(result.Message.Entities.Single(e => e.Type == MessageEntityType.TextMention).User!.Id, Is.EqualTo(7));
    }
```
(Build `WarnReplyTo`/`Sender` the way `ReportCommandTests` builds its messages and scope substitutes.)

- [ ] **Step 2: Run to verify they fail**, **Step 3: implement**, **Step 4: run** `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.IntegrationTests` (Expected: 0 warnings, PASS), **Step 5: commit** `refactor(commands): resolve identities through the identity service`.

---

### Task 13: Notifications, jobs and web UI

**Files:**
- Notifications: `TelegramGroupsAdmin/Services/Notifications/NotificationDmDispatcher.cs:27` (`FromAsync` → `ResolveAsync`), `TelegramGroupsAdmin.Telegram/Services/Notifications/TelegramDmChannel.cs:41` (`FromId` → `ResolveAsync`), `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs:25,93-95` (renderer takes `NameMasking`, creates `TelegramMessageBuilder.For(masking)`; Telegram DMs pass `GetNameMaskingAsync(null)`), `TelegramGroupsAdmin/Services/AdminNotificationService.cs:129` (resolve the reporter by id via `ResolveAsync(reporter.TelegramUserId)` instead of building from `Actor.DisplayName`), `Telegram/Services/Bot/BotDmService.cs:122` (fallback-to-chat mention uses `GetNameMaskingAsync(fallbackChatId)`), `Telegram/Services/AdminMentionHandler.cs:82` (admins via `ResolveManyAsync`)
- Jobs: `TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs:58-184`, `FileScanJob.cs:109,283-288`, `TempbanExpiryJob.cs:56`, `DeleteUserMessagesJob.cs:41`, `ProfileRescanJob.cs:85` — each re-resolves with `ResolveAsync(payload.User.Id)` (or `payload.UserId`) before building intents or text; payload types are unchanged.
- Repository/subscriber: `BanCelebrationSubscriberRepository.cs:97` returns ids; `BanCelebrationFanoutProcessor` resolves recipients with `ResolveManyAsync`.
- Web UI: `Components/Shared/ContentDetection/ContentTester.razor:576,685` (`ResolveAsync` via an injected service), `TempBanDialog.razor:88`, `WarnDialog.razor:45` (defaults become `UserIdentity.ForPreview(0, null, null, null)` placeholders, real values come from parameters), `Messages.razor` / `UserDetailDialog.razor` (identities passed to intents come from `TelegramUserManagementService`, which resolves by id via the identity service before building intents).
- Test: existing tests for each class; add `NotificationRendererTests` cases for masking on/off, and one `WelcomeTimeoutJob` test asserting the job resolves the user by id.

```csharp
    // NotificationRendererTests
    [Test]
    public void TelegramRender_UserFieldWithExplicitVerdict_MaskingOn_ShowsLabel()
    {
        var payload = new NotificationPayloadBuilder("Subject")
            .WithField("User", UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit))
            .Build();

        var msg = NotificationRenderer.RenderTelegram(payload, NameMasking.On);

        Assert.That(msg.Text, Does.Contain("[name removed: explicit]"));
        Assert.That(msg.Text, Does.Not.Contain("Bad"));
    }

    // WelcomeTimeoutJobTests
    [Test]
    public async Task Execute_ResolvesUserByIdBeforeKicking()
    {
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(UserIdentity.ForTest(7, "Current"));

        await _job.Execute(ContextWith(new WelcomeTimeoutPayload(UserIdentity.ForTest(7, "Stale"), TestChat, 42)));

        await _moderation.Received(1).KickAsync(Arg.Is<KickIntent>(i => i!.User.DisplayName == "Current"), Arg.Any<CancellationToken>());
    }
```
Use the renderer and job method names as they exist (read them first); keep email and push rendering on `DisplayName` (real name), since only Telegram text is masked.

Steps: failing tests → run (FAIL) → implement → `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests && dotnet test TelegramGroupsAdmin.IntegrationTests` (PASS, 0 warnings) → commit `refactor(notifications): resolve identities and mask names in Telegram notifications`.

---

### Task 13b: Enriched views and repository mappings use `user_identities`

**Files:**
- Modify: `TelegramGroupsAdmin.Data/Models/EnrichedMessageView.cs` (`CreateViewSql` joins at :72, :74; add columns), `TelegramGroupsAdmin.Data/Models/EnrichedReportView.cs` (`CreateViewSql` joins at :85, :90, :95, :100; add columns)
- Create: migration `JoinUserIdentitiesInEnrichedViews`
- Modify mappings: `TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs:51,52,109,185`, `TelegramGroupsAdmin.Telegram/Repositories/Mappings/EnrichedMessageMappings.cs:36`, `MessageMappings.cs:26`, `ChatAdminMappings.cs:29,46`, `TelegramUserRepository.cs:912` (`GetUserDetailAsync`)
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/UserIdentitiesViewTests.cs` (extend), existing mapping tests

**Interfaces:**
- Consumes: `UserIdentityMapping.ToIdentity(...)`, `UserIdentityView` (Task 4).
- Produces: for every user an enriched view projects, two more columns beside its names: `<prefix>is_bot` and `<prefix>latest_scan_explicit` (e.g. `is_bot`, `latest_scan_explicit` for the message author; `reply_to_is_bot`, `reply_to_latest_scan_explicit`; `suspected_is_bot`, `suspected_latest_scan_explicit`, and likewise for `target_`, `exam_user_`, `profile_user_`). Match each user's existing column prefix in the view.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Test]
    public async Task EnrichedReports_ProfileScanAlert_UserCarriesLatestScanFlag()
    {
        // Pending profile-scan alert 188's user is the canonical "Profile-scan target" (IntegrationTests/CLAUDE.md);
        // read its user id and expected flag from the tables, then assert the mapped identity's verdict.
        await using var ctx = await CreateContextAsync();
        var row = await ctx.EnrichedReports.SingleAsync(r => r.Id == GoldenDatasetConstants.Reports.PendingProfileScanAlertId);
        var expectedFlag = await ctx.UserIdentities
            .Where(v => v.TelegramUserId == row.ProfileUserId)
            .Select(v => v.LatestScanExplicit)
            .SingleAsync();

        Assert.That(row.ProfileUserLatestScanExplicit, Is.EqualTo(expectedFlag));
    }
```
(Adjust `ProfileUserId` / `ProfileUserLatestScanExplicit` to the view's actual column-property names after Step 3; the assertion compares the enriched view with `user_identities`, so it holds whatever the canonical value is.) Add one mapping unit test per mapping file asserting an explicit flag maps to `NameVerdict.Explicit` on the produced identity.

- [ ] **Step 2: Run to verify they fail**: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~UserIdentitiesViewTests"` — Expected: build error (new properties missing).

- [ ] **Step 3: Implement**

In each `CreateViewSql`, replace `LEFT JOIN telegram_users <alias>` with `LEFT JOIN user_identities <alias>` and add `<alias>.is_bot AS <prefix>is_bot, <alias>.latest_scan_explicit AS <prefix>latest_scan_explicit` to the select list. If a view also reads `telegram_users` columns that `user_identities` lacks (e.g. `user_photo_path` in `enriched_messages`), keep the `telegram_users` join for those columns and add a second join to `user_identities` on the same id for the identity columns. Add the matching properties to the view models.

Migration (`dotnet ef migrations add JoinUserIdentitiesInEnrichedViews …`):
```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down references the code constants, which always have the latest shape.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
        }
```
Before writing it, check whether any other view depends on these two (`grep -rn "enriched_messages\|enriched_reports" TelegramGroupsAdmin.Data/Models/*.cs` for views selecting from them); drop and recreate dependents in the same order if so.

Mappings: every `new UserIdentity(...)` / `UserIdentity.From(...)` in the listed files becomes `UserIdentityMapping.ToIdentity(id, first, last, username, isBot, latestScanExplicit)` with the view's columns. `ChatAdminMappings` (navigation to `TelegramUser` and a projection) and `GetUserDetailAsync` read from `context.UserIdentities` (join on id) instead of `telegram_users` for the identity fields.

- [ ] **Step 4: Run**: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.IntegrationTests` — Expected: 0 warnings, PASS.
- [ ] **Step 5: Commit** `refactor(data): enriched views and repository mappings read identities from user_identities`.

---

### Task 14: Enforcement — remove the factories, add the rule and the scan test

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/IdentityExtensions.cs` (delete the `UserIdentity` `From` / `FromAsync` overloads; keep the `ChatIdentity` ones), `TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs` (parameterless constructor becomes private), root `CLAUDE.md` (one line under Critical Rules)
- Create: `.claude/rules/user-identity.md`, `TelegramGroupsAdmin.UnitTests/Architecture/UserIdentityConstructionTests.cs`

The project has two contributors and Claude writes the code, so enforcement is a rule Claude is given plus a test that fails if the rule is broken.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.UnitTests.Architecture;

/// <summary>
/// A UserIdentity that reaches bot-written text must come from IUserIdentityService or the shared
/// row mapper, so it carries the name verdict. See .claude/rules/user-identity.md.
/// </summary>
[TestFixture]
public class UserIdentityConstructionTests
{
    // Sanctioned constructors: the type itself, the shared row mapper, and the service's
    // fallback when recording names fails.
    private static readonly string[] Allowlist =
    [
        "TelegramGroupsAdmin.Core/Models/UserIdentity.cs",
        "TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs",
        "TelegramGroupsAdmin.Telegram/Services/Identity/UserIdentityService.cs",
    ];

    private static readonly Regex Construction = new(@"new\s+(Core\.Models\.)?UserIdentity\s*\(|UserIdentity\.FromId\s*\(", RegexOptions.Compiled);

    internal static IEnumerable<string> Violations(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(f => f.StartsWith("TelegramGroupsAdmin") && !f.Contains("Tests/") && !f.Contains("Tests.")
                        && !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/Migrations/")
                        && !f.StartsWith("TelegramGroupsAdmin.Testing."))
            .Where(f => !Allowlist.Contains(f))
            .Where(f => Construction.IsMatch(File.ReadAllText(Path.Combine(repoRoot, f))));

    [Test]
    public void ProductionCode_BuildsUserIdentityOnlyThroughSanctionedPaths()
    {
        var violations = Violations(RepoRoot()).ToList();

        Assert.That(violations, Is.Empty,
            "Build UserIdentity through IUserIdentityService or UserIdentityMapping (see .claude/rules/user-identity.md). Offending files: "
            + string.Join(", ", violations));
    }

    [Test]
    public void Detector_FlagsConstructionOutsideAllowlist()
    {
        var root = Path.Combine(Path.GetTempPath(), "uid-scan-" + Guid.NewGuid());
        var file = Path.Combine(root, "TelegramGroupsAdmin.Telegram", "Bad.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "var u = new UserIdentity(1, null, null, null);");
        try
        {
            Assert.That(Violations(root), Is.EquivalentTo(new[] { "TelegramGroupsAdmin.Telegram/Bad.cs" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelegramGroupsAdmin.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityConstructionTests"`
Expected: `Detector_FlagsConstructionOutsideAllowlist` PASS; `ProductionCode_…` FAIL listing every site Tasks 8-13b missed (expected to be few or none). Each listed file is a missed migration: move it to the service or the mapper. Never add a file to the allowlist to make the test pass; a new allowlist entry needs the maintainer's agreement.

- [ ] **Step 3: Remove the factories and the parameterless builder constructor**

Delete the `UserIdentity` `From(User)`, `From(TelegramUser)`, `From(TelegramUserDto)` and `FromAsync` overloads from `IdentityExtensions`. Make `public TelegramMessageBuilder()` private so `For(...)` is the only way to create a builder. Fix the build errors this produces the same way as Step 2.

- [ ] **Step 4: Add the rule**

`.claude/rules/user-identity.md`:
```markdown
---
paths:
  - "TelegramGroupsAdmin*/**/*.cs"
  - "TelegramGroupsAdmin*/**/*.razor"
  - "docs/superpowers/plans/**/*.md"
  - "docs/superpowers/specs/**/*.md"
---

# User identity rule (MANDATORY)

- Get a `UserIdentity` from `IUserIdentityService`: `ObserveAsync` where an update shows the user's
  current names and a rename should be rescanned (new and edited messages, joins), otherwise
  `ResolveAsync` / `ResolveManyAsync` by id.
- Repository code that reads users joins the `user_identities` view and builds identities only with
  `UserIdentityMapping.ToIdentity`. Never join `telegram_users` for names that become an identity.
- Bot-written text uses `TelegramMessageBuilder.For(await configService.GetNameMaskingAsync(chatId))`
  (`null` for messages that belong to no chat) and `Mention(identity)` / `identity.BotDisplayName(masking)`.
  Logs and the web UI use `identity.DisplayName`.
- Tests use `UserIdentity.ForTest`; settings previews use `UserIdentity.ForPreview`.
- `UserIdentityConstructionTests` fails on any other `new UserIdentity(` / `UserIdentity.FromId(`.
  Don't extend its allowlist without the maintainer's agreement.
```
Root `CLAUDE.md`, under Critical Rules, add:
`- **User identities come from \`IUserIdentityService\` or the \`user_identities\` view** — full rule: \`.claude/rules/user-identity.md\`.`
Check how the integration-test-data rule is injected by the PreToolUse hook (`grep -rn "integration-test-data" .claude/`); if the hook lists rule files explicitly, add `user-identity.md` the same way.

- [ ] **Step 5: Full verification**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests && dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: 0 warnings, 0 errors, all PASS (E2E runs in CI).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "build: enforce identity construction through the service and the shared mapper"
```

---

## Self-review notes

- Spec coverage: identity type (T1), builder policy (T2), config + migration (T3), verdict source (T4), observe/rename/ordering/one transaction/photo fields (T5), service + failure handling + system accounts/bots (T6), single-flight + freshness bypass (T7), inline rename rescans (T6, T8), the user_identities view and mapper (T4, T13b), entry points (T8, T9), scan observes live names (T10), Mention/celebration and all area migrations (T11-T13), enforcement (T14), Quartz compatibility (T1, T13), canonical anchors (T4 constants), enforcement (T14). Follow-ups in the spec stay out of scope.
- Deviations from the spec are listed at the top for confirmation at review.
