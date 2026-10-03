# User Identity Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every Telegram user identity comes from one service that records observed names (newest observation wins) and carries a name verdict, so bot-written mentions show `[name removed: explicit]` for flagged names while logs and the UI keep the real name.

**Architecture:** `UserIdentity` (Core) gains `NameVerdict` and `BotDisplayName(NameMasking)`. `IUserIdentityService` (Telegram) owns `ObserveAsync` (conditional name update + history + audit in one transaction, then a queued rescan on rename) and `ResolveAsync` / `ResolveManyAsync` (names + verdict from the latest scan row). `TelegramMessageBuilder.For(NameMasking)` applies the per-chat "Mask flagged names" setting where text is written. A banned-API analyzer makes the old construction paths a build error.

**Tech Stack:** .NET 10, C# 14, EF Core 10 + PostgreSQL 18 (raw SQL via `ExecuteSqlAsync` / `SqlQuery`), Quartz.NET, NUnit, NSubstitute 6, Microsoft.CodeAnalysis.BannedApiAnalyzers.

**Spec:** `docs/superpowers/specs/2026-10-03-user-identity-service-design.md`

## Global Constraints

- Branch: `feat/552-user-identity-service`, created from `docs/552-name-masking-specs`. PR to `develop` only. Conventional commits. Never commit to `develop`/`master`. Never use git worktrees.
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

## Spec deviations (decided while planning; confirm at review)

1. **Enforcement uses the banned-API analyzer instead of deleting the constructor.** Repository mappings (`EnrichedReportMappings`, `ChatAdminMappings`, `MessageMappings`, `TelegramUserRepository`) legitimately build identities from rows, and they live in the same projects as the call sites, so `internal` cannot separate them. `RS0030` bans `UserIdentity`'s constructor and the `From(...)` factories everywhere; the identity service and the repository mapping files opt out with a `#pragma` and a reason. A new call site that skips the service is a build error, which is what the spec's stage 4 asked for.
2. **Rename rescans are always queued.** The spec allowed inline rescans when a message's moderation decision needs the new verdict, but today's pipeline ignores the ProfileChange scan's outcome (only the first-message scan's outcome is used). Queuing everywhere removes the up-to-45s stall of the serial update loop with no lost behaviour.
3. **Masking keeps the profile-scan kill switch.** `GetNameMaskingAsync` returns `On` only when the effective `ProfileScan.Enabled` and `MaskFlaggedNames` are both true, matching today's `maskingActive` rule in `BanCelebrationService.cs:87`.

## Review Focus

1. A Quartz payload queued before deploy (no `verdict` member) must still deserialize and run: pinned by Task 1's JSON compatibility test.
2. A user renamed twice quickly (A→B→A) must record both changes and end on A, and a stale scan observation must not undo a newer message name: pinned by Task 5's ordering tests.
3. Names containing only whitespace or emoji, and users with only an id (no row), must still render a non-empty mention: pinned by Task 1's `BotDisplayName` cases and Task 6's id-only resolve test.
4. A DB failure during `ObserveAsync` must not stop the message being moderated: pinned by Task 6's failure test.
5. Turning `MaskFlaggedNames` off for one chat must not unmask admin DMs (global value applies there): pinned by Task 3's `GetNameMaskingAsync` tests.

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
| `TelegramGroupsAdmin.Telegram/Repositories/ProfileScanResultsRepository.cs` (modify) | `GetLatestByUserIdsAsync` |
| `TelegramGroupsAdmin.Telegram/Services/Identity/IUserIdentityService.cs`, `UserIdentityService.cs` (create) | the service |
| `TelegramGroupsAdmin.Core/JobPayloads/ProfileChangeRescanPayload.cs`, `TelegramGroupsAdmin.BackgroundJobs/Jobs/ProfileChangeRescanJob.cs` (create) | queued rename rescan |
| `TelegramGroupsAdmin.Telegram/Services/UserApi/ProfileScanService.cs` (modify) | single-flight, freshness bypass, observe live names |
| `BannedSymbols.txt` (create, repo root) + csproj `AdditionalFiles` | enforcement |

---

### Task 0: Branch

- [ ] **Step 1: Create the branch**

```bash
git checkout docs/552-name-masking-specs
git checkout -b feat/552-user-identity-service
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

`UserIdentity.cs` (replace the record body; keep the positional constructor for now, Task 14 bans it):
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
    public async Task ScanDisabled_ReturnsOff()
    {
        Effective(-100, scanEnabled: false, mask: true);
        Assert.That(await _sut.GetNameMaskingAsync(-100), Is.EqualTo(NameMasking.Off));
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
    /// is null (messages that belong to no chat, e.g. admin DMs). On only when profile scanning is
    /// also enabled, so stale scan rows are never consulted for a chat that turned scanning off.
    /// </summary>
    ValueTask<NameMasking> GetNameMaskingAsync(long? chatId, CancellationToken ct = default);
```
`ConfigService`:
```csharp
    public async ValueTask<NameMasking> GetNameMaskingAsync(long? chatId, CancellationToken ct = default)
    {
        var welcome = await GetEffectiveWelcomeAsync(chatId ?? 0, ct);
        var scan = welcome?.JoinSecurity?.ProfileScan;
        return scan is { Enabled: true, MaskFlaggedNames: true } ? NameMasking.On : NameMasking.Off;
    }
```

`WelcomeSystemConfig.razor:190-206`: replace the switch + redaction text field with one switch:
```razor
<MudSwitch @bind-Value="_config.JoinSecurity.ProfileScan.MaskFlaggedNames"
           Label="Mask flagged names"
           Color="Color.Primary"
           Disabled="@(!_config.JoinSecurity.ProfileScan.Enabled)" />
<MudText Typo="Typo.caption" Color="Color.Secondary">
    Bot messages show "[name removed: explicit]" or "[name removed: spam]" instead of a name the profile scan flagged.
</MudText>
```
(Copy the existing `Disabled` expression and surrounding layout from lines 190-206 exactly; only the bound property, label and helper text change.)

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

### Task 4: Latest verdicts for many users

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Repositories/IProfileScanResultsRepository.cs`, `ProfileScanResultsRepository.cs`
- Test: `TelegramGroupsAdmin.IntegrationTests/Telegram/Repositories/ProfileScanResultsRepositoryTests.cs` (existing file)

**Interfaces:**
- Produces: `Task<IReadOnlyDictionary<long, ProfileScanResultRecord>> GetLatestByUserIdsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)` — one row per user that has any scan, latest by `scanned_at`.

**Canonical anchors (read-only):** 9220500615182 @bagging_armado (scans 530 older, 534 newer; 534 explicit), 9063342700386 @Juvenileii (no scan rows).

- [ ] **Step 1: Write the failing test**

```csharp
    [Test]
    public async Task GetLatestByUserIdsAsync_ReturnsLatestRowPerUser_AndOmitsUnscanned()
    {
        // Guard the canonical shapes this test depends on.
        var flagged = await _repository.GetByUserIdAsync(GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId, CancellationToken.None);
        Assert.That(flagged, Has.Count.EqualTo(2));
        var unscanned = await _repository.GetByUserIdAsync(GoldenDatasetConstants.IdentityService.UnscannedUserId, CancellationToken.None);
        Assert.That(unscanned, Is.Empty);

        var result = await _repository.GetLatestByUserIdsAsync(
            [GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId, GoldenDatasetConstants.IdentityService.UnscannedUserId],
            CancellationToken.None);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId }));
        Assert.That(result[GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId].Id, Is.EqualTo(534));
        Assert.That(result[GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId].ExplicitDisplayText, Is.True);
    }
```

Add to `GoldenDatasetConstants` (new nested class, XML doc on each):
```csharp
    /// <summary>Anchors for the user identity service tests (#552 part 1).</summary>
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
Add a "User identity service anchors" recipe to `TelegramGroupsAdmin.IntegrationTests/CLAUDE.md` listing these ids and shapes (no canonical edits).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~GetLatestByUserIdsAsync"`
Expected: build error, method missing.

- [ ] **Step 3: Implement**

```csharp
    public async Task<IReadOnlyDictionary<long, ProfileScanResultRecord>> GetLatestByUserIdsAsync(
        IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return new Dictionary<long, ProfileScanResultRecord>();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.ProfileScanResults
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .GroupBy(r => r.UserId)
            .Select(g => g.OrderByDescending(r => r.ScannedAt).First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.UserId, r => r.ToModel());
    }
```
Interface XML doc: "Latest scan row per user (by scanned_at). Users with no scan rows are absent."

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~ProfileScanResultsRepositoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(telegram): batch lookup of latest profile scan per user"
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
- Create: `TelegramGroupsAdmin.Telegram/Services/Identity/IUserIdentityService.cs`, `UserIdentityService.cs`, `TelegramGroupsAdmin.Telegram/Services/Identity/IProfileChangeRescanQueue.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` (register scoped)
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Identity/UserIdentityServiceTests.cs` (create)

**Interfaces:**
- Consumes: `GetOrUpdateAsync` (Task 5), `GetLatestByUserIdsAsync` (Task 4), `IChatAdminsRepository.IsAdminAsync`, `ITelegramUserRepository.GetByTelegramIdAsync`.
- Produces:
```csharp
public interface IUserIdentityService
{
    Task<UserIdentity> ObserveAsync(ObservedUser observed, ProfileChangeContext context, CancellationToken ct = default);
    Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);
}

public interface IProfileChangeRescanQueue
{
    Task EnqueueAsync(long userId, ChatIdentity? chat, CancellationToken ct = default);
}
```

Verdict mapping (one private static method, shared by all three calls):
- system account (`TelegramConstants.IsSystemUser`) or bot → `Unscanned`
- no scan row → `Unscanned`
- latest row `ExplicitDisplayText` → `Explicit`, else `Clean`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestFixture]
public class UserIdentityServiceTests
{
    private ITelegramUserRepository _users = null!;
    private IProfileScanResultsRepository _scans = null!;
    private IProfileChangeRescanQueue _rescans = null!;
    private UserIdentityService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<ITelegramUserRepository>();
        _scans = Substitute.For<IProfileScanResultsRepository>();
        _rescans = Substitute.For<IProfileChangeRescanQueue>();
        _scans.GetLatestByUserIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, ProfileScanResultRecord>());
        _sut = new UserIdentityService(_users, _scans, _rescans, Substitute.For<ILogger<UserIdentityService>>());
    }

    private static TelegramUser Row(long id, string first, bool trusted = false, bool bot = false) =>
        TestTelegramUsers.Create(id, first, isTrusted: trusted, isBot: bot);   // existing test factory; read UnitTests for the TelegramUser test helper and use it

    private static ProfileScanResultRecord Scan(long userId, bool explicitText) =>
        new(Id: 1, UserId: userId, ScannedAt: DateTimeOffset.UtcNow, Score: 0m, Outcome: ProfileScanOutcome.Clean,
            RuleScore: 0m, AiScore: 0m, AiReason: null, AiSignals: null, ExplicitDisplayText: explicitText);

    private static ObservedUser Observed(long id, string first) =>
        new(id, first, null, null, IsBot: false, ObservationSource.BotUpdate, DateTimeOffset.UtcNow);

    [Test]
    public async Task Resolve_LatestScanExplicit_GivesExplicit()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(Row(7, "Bad"));
        _scans.GetLatestByUserIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, ProfileScanResultRecord> { [7] = Scan(7, explicitText: true) });

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(identity.DisplayName, Is.EqualTo("Bad"));
    }

    [Test]
    public async Task Resolve_LatestScanNotExplicit_GivesClean()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(Row(7, "Fine"));
        _scans.GetLatestByUserIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, ProfileScanResultRecord> { [7] = Scan(7, explicitText: false) });

        Assert.That((await _sut.ResolveAsync(7)).Verdict, Is.EqualTo(NameVerdict.Clean));
    }

    [Test]
    public async Task Resolve_NoScan_GivesUnscanned()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(Row(7, "New"));

        Assert.That((await _sut.ResolveAsync(7)).Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task Resolve_UnknownId_GivesIdOnlyUnscanned()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);

        var identity = await _sut.ResolveAsync(7);

        Assert.That(identity.DisplayName, Is.EqualTo("User 7"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task Resolve_Bot_GivesUnscannedEvenWithScan()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(Row(7, "Bot", bot: true));
        _scans.GetLatestByUserIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, ProfileScanResultRecord> { [7] = Scan(7, explicitText: true) });

        Assert.That((await _sut.ResolveAsync(7)).Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task Observe_RenameOfUntrustedUser_QueuesRescan()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(Row(7, "New"), new PreviousNames("Old", null, null)));

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(null, null));

        await _rescans.Received(1).EnqueueAsync(7, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Observe_RenameOfTrustedUser_DoesNotQueueRescan()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(Row(7, "New", trusted: true), new PreviousNames("Old", null, null)));

        await _sut.ObserveAsync(Observed(7, "New"), new ProfileChangeContext(null, null));

        await _rescans.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
    }

    [Test]
    public async Task Observe_NoRename_DoesNotQueueRescan()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(new ObservedNamesResult(Row(7, "Same"), Renamed: null));

        await _sut.ObserveAsync(Observed(7, "Same"), new ProfileChangeContext(null, null));

        await _rescans.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
    }

    [Test]
    public async Task Observe_RepositoryThrows_ReturnsObservedNamesUnscanned()
    {
        _users.GetOrUpdateAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns<ObservedNamesResult>(_ => throw new InvalidOperationException("db down"));

        var identity = await _sut.ObserveAsync(Observed(7, "Seen"), new ProfileChangeContext(null, null));

        Assert.That(identity.DisplayName, Is.EqualTo("Seen"));
        Assert.That(identity.Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task ResolveMany_OneScanQuery_ForAllIds()
    {
        _users.GetByTelegramIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns([Row(1, "A"), Row(2, "B")]);

        var result = await _sut.ResolveManyAsync([1, 2]);

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new long[] { 1, 2 }));
        await _scans.Received(1).GetLatestByUserIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }
}
```
`ResolveManyAsync` needs `ITelegramUserRepository.GetByTelegramIdsAsync(IReadOnlyCollection<long>, CancellationToken)` returning `List<TelegramUser>`: add it to the interface and repository in this task (`Where(u => ids.Contains(u.TelegramUserId))`), with one integration test in `TelegramUserRepositoryObserveTests` using `UnscannedUserId` and `TrustedUserId`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityServiceTests"`
Expected: build error.

- [ ] **Step 3: Implement**

```csharp
namespace TelegramGroupsAdmin.Telegram.Services.Identity;

/// <summary>
/// The single way to obtain a UserIdentity. Records observed names (newest observation wins) and
/// attaches the name verdict from the latest profile scan. Stateless.
/// </summary>
public sealed class UserIdentityService(
    ITelegramUserRepository users,
    IProfileScanResultsRepository scans,
    IProfileChangeRescanQueue rescans,
    ILogger<UserIdentityService> logger) : IUserIdentityService
{
    public async Task<UserIdentity> ObserveAsync(ObservedUser observed, ProfileChangeContext context, CancellationToken ct = default)
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
#pragma warning disable RS0030 // the identity service is the sanctioned constructor of UserIdentity
            return new UserIdentity(observed.Id, observed.FirstName, observed.LastName, observed.Username);
#pragma warning restore RS0030
        }

        if (result.Renamed is not null && !result.User.IsTrusted && !result.User.IsBot
            && !TelegramConstants.IsSystemUser(observed.Id))
        {
            try
            {
                await rescans.EnqueueAsync(observed.Id, context.Chat, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to queue profile rescan after rename for user {UserId}", observed.Id);
            }
        }

        return await WithVerdictAsync(result.User, ct);
    }

    public async Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default)
    {
        var row = await users.GetByTelegramIdAsync(userId, ct);
        if (row is null)
            return UserIdentity.FromId(userId);
        return await WithVerdictAsync(row, ct);
    }

    public async Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default)
    {
        var rows = await users.GetByTelegramIdsAsync(userIds, ct);
        var latest = await scans.GetLatestByUserIdsAsync(userIds, ct);
        var byId = rows.ToDictionary(r => r.TelegramUserId);
        return userIds
            .Select(id => byId.TryGetValue(id, out var row) ? Build(row, latest.GetValueOrDefault(id)) : UserIdentity.FromId(id))
            .ToList();
    }

    private async Task<UserIdentity> WithVerdictAsync(TelegramUser row, CancellationToken ct)
    {
        var latest = await scans.GetLatestByUserIdsAsync([row.TelegramUserId], ct);
        return Build(row, latest.GetValueOrDefault(row.TelegramUserId));
    }

    private static UserIdentity Build(TelegramUser row, ProfileScanResultRecord? latestScan)
    {
#pragma warning disable RS0030 // the identity service is the sanctioned constructor of UserIdentity
        return new UserIdentity(row.TelegramUserId, row.FirstName, row.LastName, row.Username)
        {
            Verdict = VerdictFor(row, latestScan)
        };
#pragma warning restore RS0030
    }

    private static NameVerdict VerdictFor(TelegramUser row, ProfileScanResultRecord? latestScan)
    {
        if (row.IsBot || TelegramConstants.IsSystemUser(row.TelegramUserId) || latestScan is null)
            return NameVerdict.Unscanned;
        return latestScan.ExplicitDisplayText ? NameVerdict.Explicit : NameVerdict.Clean;
    }
}
```
(The `#pragma` lines are inert until Task 14 adds the analyzer.) Register in `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` next to the other scoped services: `services.AddScoped<IUserIdentityService, UserIdentityService>();`. `IProfileChangeRescanQueue` is implemented in Task 7; until then register nothing and keep the unit tests on the substitute.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserIdentityServiceTests" && dotnet test TelegramGroupsAdmin.IntegrationTests --filter "FullyQualifiedName~TelegramUserRepositoryObserveTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(telegram): add user identity service"
```

---

### Task 7: Queued rename rescan and scan single-flight

**Files:**
- Create: `TelegramGroupsAdmin.Core/JobPayloads/ProfileChangeRescanPayload.cs`, `TelegramGroupsAdmin.BackgroundJobs/Jobs/ProfileChangeRescanJob.cs`, `TelegramGroupsAdmin.Telegram/Services/Identity/ProfileChangeRescanQueue.cs`
- Modify: `TelegramGroupsAdmin.Core/BackgroundJobs/BackgroundJobNames.cs`, `DeduplicationKeys.cs`, `TelegramGroupsAdmin.BackgroundJobs/Extensions/ServiceCollectionExtensions.cs` (next to line 107), `TelegramGroupsAdmin.Telegram/Services/UserApi/IProfileScanService.cs`, `ProfileScanService.cs:50-92`, `IProfileScanGate.cs`, `ProfileScanGate.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserApi/ProfileScanServiceSingleFlightTests.cs` (create), `TelegramGroupsAdmin.UnitTests/BackgroundJobs/ProfileChangeRescanJobTests.cs` (create)

**Interfaces:**
- Produces: `record ProfileChangeRescanPayload(long UserId, ChatIdentity? Chat)`; `BackgroundJobNames.ProfileChangeRescan = "ProfileChangeRescan"`; `DeduplicationKeys.ProfileChangeRescan(long userId) => $"ProfileChangeRescan_{userId}"`; `IProfileScanService.ScanUserProfileAsync(UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool bypassFreshness = false)`; `IProfileScanGate.ScanIfEligibleAsync(UserIdentity user, ChatIdentity? chat, ProfileScanTrigger trigger, CancellationToken ct, bool bypassFreshness = false)`.

- [ ] **Step 1: Write the failing tests**

Single-flight (substitute the scan's inner work through the existing seams; if `ProfileScanService` has no seam for the core scan, add `internal Func<…>`-free extraction: move the body after the dedup check into `private Task<ProfileScanResult> RunScanAsync(...)` and test single-flight through `ScanUserProfileAsync` with an `ITelegramSessionManager` substitute whose `GetAnyClientAsync` awaits a `TaskCompletionSource` and then returns null, which yields `EmptyResult`):

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

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None, bypassFreshness: true);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }
```
Use the substitutes and construction the existing `ProfileScanService` unit tests use (`grep -rln "new ProfileScanService(" TelegramGroupsAdmin.UnitTests`); match `_sessions` / `_users` to them.

Job test: the job resolves the user and calls the gate with `ProfileScanTrigger.ProfileChange` and `bypassFreshness: true`:
```csharp
    [Test]
    public async Task Execute_ScansThroughGateWithFreshnessBypass()
    {
        var identity = UserIdentity.ForTest(7, "A");
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(identity);
        var context = JobContextWithPayload(new ProfileChangeRescanPayload(7, null)); // existing job-test helper (grep JobPayloadHelper in UnitTests)

        await _job.Execute(context);

        await _gate.Received(1).ScanIfEligibleAsync(identity, null, ProfileScanTrigger.ProfileChange, Arg.Any<CancellationToken>(), bypassFreshness: true);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScanServiceSingleFlightTests|FullyQualifiedName~ProfileChangeRescanJobTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`ProfileScanService`:
```csharp
    // Concurrent requests for one user share one scan. The 60s freshness window alone cannot do
    // this: profile_scanned_at is written only when a scan finishes.
    private readonly ConcurrentDictionary<long, Lazy<Task<ProfileScanResult>>> _inFlight = new();

    public async Task<ProfileScanResult> ScanUserProfileAsync(
        UserIdentity user, ChatIdentity? triggeringChat, CancellationToken ct, bool bypassFreshness = false)
    {
        var lazy = _inFlight.GetOrAdd(user.Id, _ => new Lazy<Task<ProfileScanResult>>(
            () => ScanOnceAsync(user, triggeringChat, bypassFreshness, ct)));
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
Rename the current method body to `private async Task<ProfileScanResult> ScanOnceAsync(UserIdentity user, ChatIdentity? triggeringChat, bool bypassFreshness, CancellationToken ct)` and change the freshness condition at line 68 to `if (!bypassFreshness && existingUser?.ProfileScannedAt is { } lastScan && …)`. Thread `bypassFreshness` through `IProfileScanGate`/`ProfileScanGate` to `ScanUserProfileAsync`.

Payload, names, dedup key as in Interfaces. Job:
```csharp
public sealed class ProfileChangeRescanJob(
    ILogger<ProfileChangeRescanJob> logger,
    IServiceScopeFactory scopeFactory) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<ProfileChangeRescanPayload>(context, logger);
        if (payload == null) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var identities = scope.ServiceProvider.GetRequiredService<IUserIdentityService>();
        var gate = scope.ServiceProvider.GetRequiredService<IProfileScanGate>();

        var user = await identities.ResolveAsync(payload.UserId, context.CancellationToken);
        await gate.ScanIfEligibleAsync(user, payload.Chat, ProfileScanTrigger.ProfileChange, context.CancellationToken, bypassFreshness: true);
    }
}
```
(If other jobs inject services directly rather than a scope factory, follow them; `DeleteUserMessagesJob` injects directly.) Register next to `ServiceCollectionExtensions.cs:107`:
`q.AddJob<ProfileChangeRescanJob>(opts => opts.WithIdentity(BackgroundJobNames.ProfileChangeRescan).StoreDurably());`

Queue:
```csharp
public sealed class ProfileChangeRescanQueue(IJobScheduler scheduler) : IProfileChangeRescanQueue
{
    public Task EnqueueAsync(long userId, ChatIdentity? chat, CancellationToken ct = default) =>
        scheduler.ScheduleJobAsync(
            BackgroundJobNames.ProfileChangeRescan,
            new ProfileChangeRescanPayload(userId, chat),
            delaySeconds: 0,
            deduplicationKey: DeduplicationKeys.ProfileChangeRescan(userId),
            cancellationToken: ct);
}
```
Register `services.AddScoped<IProfileChangeRescanQueue, ProfileChangeRescanQueue>();` next to `IUserIdentityService`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~ProfileScan|FullyQualifiedName~ProfileChangeRescanJobTests"`
Expected: build 0 warnings; PASS, including existing ProfileScanService and gate tests (update their mocks for the new optional parameter if NSubstitute `Received` calls need it).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(profile-scan): queue rename rescans and share in-flight scans"
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
        _identities.ObserveAsync(Arg.Any<ObservedUser>(), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
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

### Task 9: Edited messages, callbacks, chat-member updates; remove old writes

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Handlers/MessageEditProcessor.cs`, the callback entry (`grep -rn "CallbackQuery" TelegramGroupsAdmin.Telegram/Services/UpdateRouter.cs` → its handler), `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs:80,141` (join) and `:713` (callback), `TelegramGroupsAdmin.Telegram/Services/Bot/BotChatService.cs:184,288,453`, `TelegramGroupsAdmin.Telegram/Services/DmCelebrations/BanCelebrationSubscriptionService.cs:38`, `TelegramGroupsAdmin.Telegram/Services/BotProtectionService.cs:109`, `TelegramGroupsAdmin.Telegram/Services/Bot/BotMessageService.cs:117,363,467`, `ITelegramUserRepository` / `TelegramUserRepository` (delete `GetOrCreateAsync`, `UpsertAsync`)
- Test: update existing tests of each modified class; add one per entry point asserting `ObserveAsync` with the right `ObservationSource` and timestamp.

**Interfaces:**
- Consumes: `IUserIdentityService.ObserveAsync`.

Per entry point:

| Site | Observation | Timestamp |
|---|---|---|
| `MessageEditProcessor` | `editedMessage.From` | `edit_date` (`EditDate ?? Date`) |
| Callback handler | `callbackQuery.From` | `DateTimeOffset.UtcNow` (callbacks carry no date) |
| `WelcomeService` join (:80, :141 `GetOrCreateAsync`) | `chatMemberUpdate.NewChatMember.User` | `chatMemberUpdate.Date` |
| `BotChatService` :184, :288 | `ChatMemberUpdated` user, `ObservationSource.ChatMember` | update `Date` |
| `BotChatService` :453 (admin refresh) | `ChatMember.User` from `getChatAdministrators`, `ObservationSource.ChatMember` | `DateTimeOffset.UtcNow` |
| `BanCelebrationSubscriptionService` :38 | `ChatMemberUpdated.From` | update `Date` |
| `BotProtectionService` :109 | the bot `User`, `IsBot = true` | update `Date` |
| `BotMessageService` :117/:363/:467 | the bot's own `User` (from `GetMe`), `IsBot = true` | `DateTimeOffset.UtcNow` |

Each site uses the returned identity in place of the `UserIdentity.From(...)` it built before.

- [ ] **Step 1: Write the failing tests** — one per row above, in each class's existing test file, of this shape:

```csharp
    [Test]
    public async Task EditedMessage_ObservesEditorWithEditDate()
    {
        var edited = EditedTextMessage(from: 7, editDate: new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));

        await _sut.ProcessEditAsync(edited, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == 7 && o.ObservedAt == new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)),
            Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>());
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
The live-names path needs a WTelegram client and is covered by reading: add the `ObserveAsync` call right after `tlUser` is fetched, guarded by `tlUser is not null`, with `ObservedAt = DateTimeOffset.UtcNow` and `new ProfileChangeContext(triggeringChat, null)`; failures are already swallowed by the service.

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
- Reply targets and other SDK users: `await identityService.ObserveAsync(new ObservedUser(u.Id, u.FirstName, u.LastName, u.Username, u.IsBot, ObservationSource.BotUpdate, observedAt), new ProfileChangeContext(chat, null), ct)` with the replied-to message's date.
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
        _identities.ObserveAsync(Arg.Is<ObservedUser>(o => o!.Id == 7), Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>())
            .Returns(target);
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

### Task 14: Enforce with the banned-API analyzer

**Files:**
- Create: `BannedSymbols.Identity.txt` (repo root)
- Modify: every production csproj that references Core (`TelegramGroupsAdmin`, `.Telegram`, `.BackgroundJobs`, `.ContentDetection`, `.Configuration`, `.AI`, `.Core`): add the analyzer package and the additional file; `TelegramGroupsAdmin.Telegram/Extensions/IdentityExtensions.cs` (delete the `UserIdentity` `From`/`FromAsync` overloads); `TelegramMessageBuilder.cs` (parameterless constructor becomes private)
- Test: build only (the analyzer is the test) plus the full suite

- [ ] **Step 1: Ban the symbols**

`BannedSymbols.Identity.txt`:
```
# A UserIdentity must come from IUserIdentityService so it carries the name verdict.
# Sanctioned exceptions (#pragma warning disable RS0030 with a reason): UserIdentityService,
# repository mapping files that build identities from rows for the UI and logs.
M:TelegramGroupsAdmin.Core.Models.UserIdentity.#ctor(System.Int64,System.String,System.String,System.String);Use IUserIdentityService (ObserveAsync / ResolveAsync), or UserIdentity.ForPreview / ForTest
M:TelegramGroupsAdmin.Core.Models.UserIdentity.FromId(System.Int64);Use IUserIdentityService.ResolveAsync
M:TelegramGroupsAdmin.Core.Models.UserIdentity.ForTest(System.Int64,System.String,System.String,System.String,TelegramGroupsAdmin.Core.Models.NameVerdict);Test-only factory
```

Sanctioned uses inside the type and the service: put `#pragma warning disable RS0030 // the type's own factories` around the factory bodies in `UserIdentity.cs`, and wrap the `UserIdentity.FromId` calls in `UserIdentityService` the same way as its constructor calls. `ForPreview` stays unbanned (settings previews in Razor); its XML doc says it must never reach a real user, and a reviewer checks new uses.
Each production csproj:
```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)../BannedSymbols.Identity.txt" />
  </ItemGroup>
```
(Copy the `PackageReference` block exactly as `TelegramGroupsAdmin.E2ETests.csproj:17` declares it.)

Delete the `UserIdentity` overloads in `IdentityExtensions` (keep the `ChatIdentity` ones). Make `TelegramMessageBuilder()` private so `For(...)` is the only way to create a builder.

- [ ] **Step 2: Build and read every error**

Run: `dotnet build TelegramGroupsAdmin.sln 2>&1 | grep -E "RS0030|error CS" | sort -u`
Expected: a list of remaining sites. For each:
- production call site that was missed in Tasks 8-13 → move it to the identity service (that is the point of this task);
- repository mapping that builds an identity from a row (`EnrichedReportMappings.cs:51,52,109,185`, `ChatAdminMappings.cs:29,46`, `EnrichedMessageMappings.cs:36`, `MessageMappings.cs:26`, `TelegramUserRepository.cs:912`, `ChatAdmin.cs:14`, `TelegramUserDetail.cs:13`) → wrap the single line in `#pragma warning disable RS0030 // row-backed identity for UI/logs; mentions resolve through IUserIdentityService` / `restore`;
- test projects are not analyzed; they use `UserIdentity.ForTest`.

- [ ] **Step 3: Full verification**

Run: `dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests && dotnet test TelegramGroupsAdmin.ComponentTests && dotnet test TelegramGroupsAdmin.IntegrationTests`
Expected: 0 warnings, 0 errors, all PASS. (E2E runs in CI.)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "build: ban direct UserIdentity construction outside the identity service"
```

---

## Self-review notes

- Spec coverage: identity type (T1), builder policy (T2), config + migration (T3), verdict source (T4), observe/rename/ordering/one transaction/photo fields (T5), service + failure handling + system accounts/bots (T6), single-flight + freshness bypass + queued rescans (T7), entry points (T8, T9), scan observes live names (T10), Mention/celebration and all area migrations (T11-T13), enforcement (T14), Quartz compatibility (T1, T13), canonical anchors (T4 constants). Follow-ups in the spec stay out of scope.
- Deviations from the spec are listed at the top for confirmation at review.
