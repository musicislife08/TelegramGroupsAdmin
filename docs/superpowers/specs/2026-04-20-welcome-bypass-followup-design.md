# Welcome Bypass Follow-up — Design

**Date:** 2026-04-20
**Branch:** `feat/trusted-user-bypass-welcome`
**References:**
- Original feature spec: `docs/superpowers/specs/2026-04-17-trusted-user-bypass-welcome-design.md`
- Original feature plan: `docs/superpowers/plans/2026-04-17-trusted-user-bypass-welcome.md`
- Review source: multi-agent `/review-all` pass on 2026-04-20

**Goal:** Address findings from the multi-agent code review of the trusted-user welcome bypass feature before opening the PR to `develop`. Scope is fix-as-we-find — including adjacent bugs in siblings of the touched code.

**Non-goals:**
- Accessibility improvements (deferred per user direction)
- Resolver fail-closed behavior (accepted as fail-open)
- Concurrent-join announcement deduplication (accepted as-is; revisit if observed in production)

---

## Summary of structural changes

Three structural shifts land in this commit beyond the surface-level fixes:

1. **`BypassDecision` collapses from 3 values to 2.** `ChatAdmin` and `WebAdmin` merge into a single `Admin` variant. The underlying detection still distinguishes the two (Telegram chat admin anywhere vs linked web GlobalAdmin/Owner), but the enum, metrics, announcement templates, and UI all treat them as one class.

2. **`Core` becomes the single home for shared enums.** `UserActionType` and `PermissionLevel` each have a duplicate `Data/Models/*.cs` copy that has been manually synced. Both copies are collapsed into `Core/Models/` and the `Data` duplicates are deleted. Repository interfaces at the Data-project boundary now accept/return the Core enums via mapping-layer casts; Data DTOs remain `int` columns.

3. **HTML encoding for Telegram output is centralized.** A new `TelegramHtmlEncoder` lives in `Core/Utilities`; the two existing inline `EscapeHtml` helpers (`NotificationHandler.EscapeHtml`, `NotificationRenderer.EscapeHtml`) are migrated to use it, and the bypass announcement call site adopts it.

---

## Section 1 — Critical fixes

### 1.1 Centralized HTML encoder (review finding: Security H1)

**File:** `TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs` (new)

Single static class with one method:

```csharp
public static class TelegramHtmlEncoder
{
    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
}
```

**Migrations:**
- `NotificationHandler.EscapeHtml` → delete inline method; call `TelegramHtmlEncoder.Encode`
- `NotificationRenderer.EscapeHtml` → delete inline method; call `TelegramHtmlEncoder.Encode`

**New usage in bypass announcement** (`WelcomeService.PostBypassAnnouncementIfConfiguredAsync`):

```csharp
static string Esc(string? s) => TelegramHtmlEncoder.Encode(s);

var mention = !string.IsNullOrWhiteSpace(user.Username)
    ? $"@{Esc(user.Username)}"
    : $"<a href=\"tg://user?id={user.Id}\">{Esc(TelegramDisplayName.Format(user))}</a>";

var text = template
    .Replace(TrustedBypassConfig.UsernameVariable, mention)
    .Replace(TrustedBypassConfig.ChatNameVariable, Esc(chat.Title));
```

### 1.2 Announcement gated on `Enabled && decision != None` (review finding: Refactor #1)

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs`

First line of `PostBypassAnnouncementIfConfiguredAsync` becomes:

```csharp
if (!config.TrustedBypass.Enabled) return;
```

The helper is also updated to take `BypassDecision` as a parameter and look up the matching per-decision template (see 1.3). An empty template string for a given decision means "skip announcement for that bypass type" — individual opt-out per type.

### 1.3 Collapse `BypassDecision` to `Admin` + `Trusted`, rework resolver

**Files:**
- `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassDecision.cs` — enum shrinks to `None`, `Admin`, `Trusted`
- `TelegramGroupsAdmin.Telegram/Services/Welcome/WelcomeBypassResolver.cs` — see new logic below
- `TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs` — two announcement template fields
- `TelegramGroupsAdmin.Data/Models/Configs/TrustedBypassConfigData.cs` — two string fields to match
- `TelegramGroupsAdmin.Configuration/Mappings/WelcomeConfigMappings.cs` — map the new fields
- `TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs` — two outcome labels (`skipped_bypass_admin`, `skipped_bypass_trusted`) instead of three
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/AuditHandler.cs` — `LogWelcomeBypassAsync` reason strings merge
- `TelegramGroupsAdmin/Components/Shared/WelcomeSystemConfig.razor` — two template fields in the UI
- All tests touching `BypassDecision` — update assertions

**New resolver logic:**

```csharp
public async Task<BypassDecision> ResolveAsync(UserIdentity user, ChatIdentity chat, CancellationToken ct)
{
    await using var scope = _scopeFactory.CreateAsyncScope();
    var chatAdminsRepo = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();
    var mappingRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserMappingRepository>();
    var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
    var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

    // Rule 1: Admin bypass (Telegram chat admin anywhere OR linked web GlobalAdmin/Owner)
    var adminChats = await chatAdminsRepo.GetAdminChatsAsync(user.Id, ct);
    if (adminChats.Count > 0)
    {
        _logger.LogDebug(AdminBypassChatAdminFormat, user.ToLogDebug(), chat.ToLogDebug());
        return BypassDecision.Admin;
    }

    var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, ct);
    if (permissionLevel >= PermissionLevel.GlobalAdmin)
    {
        _logger.LogDebug(AdminBypassWebAdminFormat, user.ToLogDebug(), chat.ToLogDebug(), permissionLevel);
        return BypassDecision.Admin;
    }

    // Rule 2: Trusted bypass
    var config = await configService.GetEffectiveAsync<WelcomeConfig>(chat.Id, ct);
    if (config.TrustedBypass.Enabled)
    {
        var isTrusted = await userRepo.IsTrustedAsync(user.Id, ct);
        if (isTrusted)
        {
            _logger.LogDebug(TrustedBypassFormat, user.ToLogDebug(), chat.ToLogDebug());
            return BypassDecision.Trusted;
        }
    }

    return BypassDecision.None;
}
```

Notes:
- Drops `IBotUserService.GetChatMemberAsync` call entirely — that check was a no-op (a joining user cannot already be a chat admin of the chat they're joining)
- `ITelegramUserMappingRepository.GetPermissionLevelByTelegramIdAsync` is retyped to return `PermissionLevel?` via Core enum (cast in the mapping layer; Data column stays `int`)
- Reason-string forensics preserved in `AuditHandler.LogWelcomeBypassAsync` — the decision switch expands to include a sub-reason, so the audit row's `Reason` column distinguishes Telegram-chat-admin-elsewhere from linked-web-admin, while the enum and metrics treat both as `Admin`. Concrete shape:

```csharp
public async Task LogWelcomeBypassAsync(UserIdentity user, ChatIdentity chat, BypassDecision decision, string reasonDetail, CancellationToken ct = default)
{
    // reasonDetail set by resolver: "Telegram chat admin (N chats)" / "Linked web admin (GlobalAdmin)" / "Trusted user"
    var record = CreateRecord(user.Id, UserActionType.WelcomeBypass, Actor.WelcomeBypass, reasonDetail, chatId: chat.Id);
    await _userActionsRepository.InsertAsync(record, ct);
    _logger.LogDebug("Recorded {ActionType} for {User} in {Chat} (decision: {Decision}, reason: {Reason})",
        UserActionType.WelcomeBypass, user.ToLogDebug(), chat.ToLogDebug(), decision, reasonDetail);
}
```

**`TrustedBypassConfig` shape:**

```csharp
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

**Template lookup** in `PostBypassAnnouncementIfConfiguredAsync`:

```csharp
var template = decision switch
{
    BypassDecision.Admin => config.TrustedBypass.AnnouncementMessageAdmin,
    BypassDecision.Trusted => config.TrustedBypass.AnnouncementMessageTrusted,
    _ => null,
};

if (string.IsNullOrWhiteSpace(template)) return;
```

---

## Section 2 — Important fixes

### 2.1 Use `BackgroundJobNames` constants (review finding: Refactor #2)

**File:** `WelcomeService.cs`

- Line ~552: replace `"WelcomeTimeout"` with `BackgroundJobNames.WelcomeTimeout`
- Line ~792: replace `"DeleteMessage"` with `BackgroundJobNames.DeleteMessage`

### 2.2 Preserve `TrustedBypass` + `JoinSecurity` in `LoadConfig` migration (review finding: UX I1)

**File:** `WelcomeSystemConfig.razor` (~lines 554-574)

Inside the "Preserve old settings" block:

```csharp
_config.TrustedBypass = config.TrustedBypass;
_config.JoinSecurity = config.JoinSecurity;
```

Add a component test that renders with `{ MainWelcomeMessage = "", TrustedBypass = { Enabled = true } }`, triggers the migration path, and asserts the Enabled flag is preserved after save.

### 2.3 Input clamping (review finding: Security M1)

**Why at the consumer, not the mapping layer:** `ConfigService.GetAsync<WelcomeConfig>` and `SaveAsync<WelcomeConfig>` use `JsonSerializer` directly against the business model — the `WelcomeConfigData` DTO and `WelcomeConfigMappings.ToModel` / `.ToData` are not invoked at runtime for Welcome config. Investigation confirmed parallel configs (ContentDetection, UserApi) use dedicated repositories that DO invoke their mappings, but the Welcome path was never wired. See §5 "Known gaps" for the broader wiring follow-up. Putting validation in the mapping would be invisible to the runtime; putting it in the consumer guarantees execution.

**File:** `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` (inside `PostBypassAnnouncementIfConfiguredAsync`)

```csharp
// Constants on TrustedBypassConfig:
//   public const int MinAnnouncementTtlSeconds = 0;
//   public const int MaxAnnouncementTemplateLength = 3500;
// (Template max = 3500, not 4096, to leave ~600 chars of headroom for
//  {username}/{chat_name} expansion + worst-case HTML encoding. Telegram's
//  4096-byte message limit applies to the rendered output.)

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
        decision, TrustedBypassConfig.MaxAnnouncementTemplateLength, template.Length, chat.ToLogInfo());
    template = template[..TrustedBypassConfig.MaxAnnouncementTemplateLength];
}

var ttl = Math.Max(TrustedBypassConfig.MinAnnouncementTtlSeconds,
                   config.TrustedBypass.AnnouncementTtlSeconds);

// … then substitute variables + encode + send …
```

Notes:
- Empty/whitespace template = skip announcement (per-type opt-out); no default fallback
- TTL floor at 0; no upper bound (admin controls)
- Message truncates to 3500 chars with a warning log so admins can diagnose. Defensive against hand-edited JSONB rows; UI `MaxLength` prevents legitimate flows from hitting this path.

**Constants live on `TrustedBypassConfig`** even though the mapping isn't invoked — the UI and the consumer both reference them, and they belong with the model they describe:

```csharp
public const int MinAnnouncementTtlSeconds = 0;
public const int MaxAnnouncementTemplateLength = 3500;
```

**UI** (`WelcomeSystemConfig.razor`): add `MaxLength="@TrustedBypassConfig.MaxAnnouncementTemplateLength"` and `Counter="@TrustedBypassConfig.MaxAnnouncementTemplateLength"` to both announcement template fields. Helper text mentions that the rendered message (after variable expansion) is subject to Telegram's 4096-byte limit.

### 2.4 Integration test for relaxed CK constraint (review finding: Test #2)

**File:** `TelegramGroupsAdmin.IntegrationTests/Repositories/UserActionsRepositoryConstraintTests.cs` (new)

Two scenarios against Testcontainers Postgres:
- **Success:** insert `UserActionRecord` with `ChatId` set, `MessageId` null, action `WelcomeBypass` — expect row committed
- **Failure:** insert with `MessageId` set but `ChatId` null — expect `DbUpdateException` wrapping a CK-constraint violation

Anchors the migration's intent in CI.

### 2.5 Exception-path tests deferred

Per user direction: skip #12 (`ActivateAsync` / `LogWelcomeBypassAsync` exception paths). Fail-open policy already accepts these; tests would only codify "we log and return," low leverage.

### 2.6 Throw contract test for `RecordBypassOutcome(None)` (review finding: Test #1)

**File:** `TelegramGroupsAdmin.UnitTests/Telegram/Metrics/WelcomeMetricsTests.cs` (augment if exists; create if not)

```csharp
[Test]
public void RecordBypassOutcome_WhenDecisionIsNone_ThrowsInvalidOperationException()
{
    var metrics = new WelcomeMetrics();
    Assert.Throws<InvalidOperationException>(() => metrics.RecordBypassOutcome(BypassDecision.None, 0.0));
}
```

### 2.7 Template substitution + HTML encoding tests (review finding: Test #9, pairs with 1.1)

**File:** `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

Three additional tests:
- `PostBypassAnnouncement_Substitutes_Username_AsEncodedMention` — assert `{username}` expansion uses the mention format and the display portion is HTML-encoded
- `PostBypassAnnouncement_Substitutes_ChatName_Encoded` — hostile chat title `<b>test</b>` renders as `&lt;b&gt;test&lt;/b&gt;`
- `PostBypassAnnouncement_HostileFirstName_IsEncoded` — regression cover for the HTML-injection fix

### 2.8 `LogRestrictAsync` inline log (review finding: Refactor #3)

**File:** `AuditHandler.cs`

Replace `LogRecorded(UserActionType.Mute, user, executor)` (line 102) with:

```csharp
_logger.LogDebug(
    "Recorded {ActionType} action for {User} in {Chat} by {Executor}",
    UserActionType.Mute, user.ToLogDebug(), chat.ToLogDebug(), executor.GetDisplayText());
```

`chat.ToLogDebug()` handles null correctly. Narrow change; does not touch siblings (they already have this inline).

### 2.9 Mapping + consumer validation tests (review finding: Test #6, retargeted)

Because clamping moved to the consumer (§2.3), test coverage also shifts.

**File:** `TelegramGroupsAdmin.UnitTests/Telegram/Services/WelcomeServiceTests.cs`

Add tests for the consumer-side clamping:
- `PostBypassAnnouncement_OverLengthTemplate_TruncatesAndLogsWarning` — template of 5000 chars → truncated to 3500, warning logged
- `PostBypassAnnouncement_NegativeTtl_ClampsToZero` — `AnnouncementTtlSeconds = -5` → scheduled delete delay is 0
- `PostBypassAnnouncement_EmptyTemplate_Skips` — empty string → no `SendMessageAsync` call, no `ScheduleJobAsync` call

**File:** `TelegramGroupsAdmin.UnitTests/Configuration/WelcomeConfigMappingsTests.cs`

Update the existing mapping tests to reflect the two-field shape (`AnnouncementMessageAdmin` / `AnnouncementMessageTrusted`). Roundtrip coverage for the new fields is the only change here; the mapping no longer has clamping responsibility, so no clamp tests belong.

### 2.10 Consolidate `UserActionType` + `PermissionLevel` into Core (review finding: Concurrency #7)

**Changes:**
- `TelegramGroupsAdmin.Data/Models/UserActionType.cs` — DELETE
- `TelegramGroupsAdmin.Data/Models/PermissionLevel.cs` — DELETE
- `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs` — DELETE (already duplicate of Data; both gone, canonical moves to Core)
- `TelegramGroupsAdmin.Core/Models/UserActionType.cs` — NEW (promoted from Data copy, includes `WelcomeBypass = 11`)
- `TelegramGroupsAdmin.Core/Models/PermissionLevel.cs` — already exists (canonical), unchanged
- Retarget all `using TelegramGroupsAdmin.Data.Models;` or `using TelegramGroupsAdmin.Telegram.Models;` references that resolve to these two enums → `using TelegramGroupsAdmin.Core.Models;`
- EF Core mapping: confirm `UserActionType` column stays `int` via `HasConversion<int>()` in `AppDbContext` — Data DTOs keep `int` columns; entity models use the Core enum directly, EF casts at the boundary

Repository method signatures adjust:
- `ITelegramUserMappingRepository.GetPermissionLevelByTelegramIdAsync` — return type `Task<PermissionLevel?>` (Core enum) instead of `Task<int?>`

---

## Section 3 — Polish / suggestions

### 3.1 Panel rename (review finding: UX S2)

`WelcomeSystemConfig.razor` — change section header (~line 194) and panel `TitleContent` (~line 211) from "Trusted User Bypass" to "Auto-admit Trusted Users". No other label changes.

### 3.2 Inline announcement preview (review finding: UX S7)

Reuse the existing `TelegramMessagePreview` component (pattern at razor:336-344 and :407-414). Add two preview rows inside the Trusted Bypass panel — one per template (Admin, Trusted) — bound to `_config.TrustedBypass.AnnouncementMessageAdmin` and `...Trusted`. Wire sample values for `{username}` / `{chat_name}` so admins see the rendered mention and chat-name substitution.

### 3.3 Delete dead `OnAfterRenderAsync` override (review finding: Refactor #5)

`WelcomeSystemConfig.razor:531-539` — delete the override. Base class implementation is a no-op and the current body adds nothing.

### 3.4 Replace `string.Format` with `$""` in `WelcomeMetrics`

`WelcomeMetrics.cs:80` — replace `string.Format(BypassOutcomeUnmappedFormat, decision)` with `$"Unmapped bypass decision: {decision}"`. Delete the `BypassOutcomeUnmappedFormat` constant.

### 3.5 `Meter` as field across 3 metrics classes (review finding: Refactor #7, broadened)

**Files:**
- `WelcomeMetrics.cs`
- `ChatMetrics.cs`
- `ReportMetrics.cs`

In each, replace `var meter = new Meter("TelegramGroupsAdmin.<Domain>");` local with `private readonly Meter _meter = new("TelegramGroupsAdmin.<Domain>");` field, and update all `meter.Create*` calls to `_meter.Create*`. Matches `PipelineMetrics` pattern.

### 3.6 Trusted + toggle-off integration scenario (review finding: Test #11)

**File:** `TelegramGroupsAdmin.IntegrationTests/Telegram/Services/WelcomeFlowBypassIntegrationTests.cs`

Add one scenario:
- Seed a trusted user (`IsTrusted = true`) and a chat with `TrustedBypass.Enabled = false`
- Trigger a chat-member update
- Assert: resolver returns `None` → normal welcome flow runs → user is muted, welcome message posted, NO audit row of type `WelcomeBypass`

---

## What's out of scope

- Accessibility improvements (UX C1, C2, C3)
- Resolver fail-closed behavior (accepted fail-open)
- Concurrent-join announcement deduplication (accepted as-is)
- Variable-chip insertion UX (UX S8 — deferred)
- Exception-path tests for `ActivateAsync` / `LogWelcomeBypassAsync` (Test #12 — deferred)
- TOCTOU between resolver and activation (dropped — polling queue serializes updates, no race)
- Admin-on-admin moderation UX (separate known follow-up)
- Wiring `ConfigService` through the Data-DTO/Mapping layer for Welcome (and ~5 other configs) — see §5

## 5. Known gaps (deferred follow-up PR)

### 5.1 `ConfigService` bypasses the Data DTO layer for most config types

`ConfigService.GetAsync<T>` and `SaveAsync<T>` (`TelegramGroupsAdmin.Core/Services/ConfigService.cs`) use `JsonSerializer` directly against the business model `T`. This correctly serializes/deserializes but skips the `*ConfigData` DTO and `*ConfigMappings` layer that was designed to separate the wire-stable JSON shape from the business model.

**Affected configs** (all go through `ConfigService.GetAsync<T>` / `SaveAsync<T>` without their mapping being invoked):
- `WelcomeConfig` ↔ `WelcomeConfigData` (+ `WelcomeConfigMappings`)
- `LogConfig` — verify whether a mapping exists
- `ModerationConfig` — verify
- `UrlFilterConfig` (maps to `BotProtectionConfig` column) — verify
- `TelegramBotConfig` — verify
- `ServiceMessageDeletionConfig` — verify
- `BanCelebrationConfig` — verify

**Unaffected (already wired):**
- `ContentDetectionConfig` — uses EF Core `OwnsOne().ToJson()` via `ContentDetectionConfigRepository.GetGlobalConfigAsync` which explicitly calls `entity.Config.ToModel()`
- `UserApiConfig` — `SystemConfigRepository.GetUserApiConfigAsync` deserializes to `UserApiConfigData` and calls `.ToModel()`; save does `.ToData()` → serialize

**Impact:** Mapping layer is inert for the affected configs. Any validation, coalescing, or default-handling written into their `ToModel` / `ToData` methods is skipped. The `TrustedBypassConfigData` updates in §1.3 of this spec are kept for structural parity with the other configs (so the layer is internally consistent) but do not execute at runtime.

**Resolution options (for the follow-up PR):**
- (a) Teach `ConfigService` to route through a mapper registry when one is registered for `T`.
- (b) Migrate each affected config to its own dedicated repository (like `UserApiConfig`).

Neither is in scope for this bypass-review follow-up.

## Risks

- **Enum consolidation blast radius.** `UserActionType` + `PermissionLevel` consolidation touches many files. Most changes are `using`-statement retargets and one method-signature return-type change. EF Core mapping must continue to persist as `int`; `HasConversion<int>()` should remain on the DbSet configuration.
- **BypassDecision enum value reuse.** If `BypassDecision.ChatAdmin = 1` and `WebAdmin = 2` are collapsed to a single `Admin = 1`, any persisted integers (e.g., metric labels, logs already written) could be read ambiguously. Metrics labels are strings — no concern. Audit rows store `Reason` as string — also no concern. Nothing serializes `BypassDecision` as an int to durable storage. Safe.
- **`TrustedBypassConfigData` shape change.** The Data-layer DTO goes from single `AnnouncementMessage` to `AnnouncementMessageAdmin` + `AnnouncementMessageTrusted`. Feature is unreleased (still on the `feat/trusted-user-bypass-welcome` branch, not merged to `develop` or `master`), so no production JSONB rows have the old shape. Dev/test databases on the branch may have rows with the old field — they'll deserialize both new fields as null/empty (skip announcement), and admins can re-save to populate. No migration/compat shim needed.
