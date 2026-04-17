# Trusted User Bypass for Welcome System — Design

**Date:** 2026-04-17
**Status:** Draft
**Branch:** `feat/trusted-user-bypass-welcome`

## Overview

Add an opt-in per-chat capability to let auto-trusted users (and linked web administrators) skip the welcome consent flow and all join-time security checks. A short, auto-deleting announcement is posted in the chat whenever a bypass fires so admins and members see why the user walked in clean.

## Motivation

TGA already tracks "trust" as a global flag on `telegram_users` — users who post N non-spam messages (subject to an account-age gate) are automatically trusted across all managed chats. Today, these trusted users still go through the welcome consent flow (restrict, Accept/Deny or entrance exam, timeout) every time they join a new chat, even though they've already demonstrated non-spam behavior elsewhere. That's friction admins don't want.

Linked web administrators (Owner, GlobalAdmin) face the same friction — there's no mechanism today to recognize a logged-in TGA web admin when they join a chat as a Telegram user. We want them to walk in clean, always.

## Scope

### In scope (this PR)

1. New `TrustedBypassConfig` nested on `WelcomeConfig`
2. New `IWelcomeBypassResolver` service that evaluates eligibility
3. `WelcomeService.HandleChatMemberUpdateAsync` gains a bypass early-exit
4. New `UserActionType.WelcomeBypass = 11` enum value and audit handler method
5. New `Actor.WelcomeBypass` system actor, plus a one-time refactor of all existing system-actor magic strings into a new `SystemActorIds` constants class
6. New expansion panel in `WelcomeSystemConfig.razor`
7. New announcement message delivery using existing `IBotMessageService` + `DeleteMessageJob`
8. Test coverage: unit, integration, and bUnit component tests (no new E2E)

### Out of scope (explicitly deferred)

- **Admin-on-admin moderation with hierarchy + confirmation UX** — tracked as a follow-up GitHub issue, to be filed when we reach that step in the implementation plan. Today `/ban`, `/spam`, `/tempban`, and the Blazor admin UI have no safeguards against actioning a higher-privilege admin's messages. A proper fix requires hierarchy semantics (Owner moderates GlobalAdmin/Admin, GlobalAdmin moderates Admin), inline-keyboard confirmation in chat, MudBlazor confirmation dialog in the UI, and gating on the bot's `can_promote_members` permission. That's its own PR.

## Requirements

**Bypass rules, in priority order (first match wins):**

1. **Telegram chat admin / creator** — already handled today at `WelcomeService.cs:138`. Unchanged.
2. **Linked web admin** — joining user's Telegram ID resolves via `ITelegramUserMappingRepository.GetPermissionLevelByTelegramIdAsync` to permission level `GlobalAdmin (1)` or `Owner (2)`. **Always on**, not configurable.
3. **Trusted user** — `telegram_users.is_trusted = true` AND `WelcomeConfig.TrustedBypass.Enabled = true` for the chat. **Toggle-gated**, default off.

When rule 2 or 3 fires (rule 1 keeps its existing silent skip):
- User is NOT restricted, NO security checks run (CAS, username blacklist, impersonation, profile scan), NO welcome consent flow
- User is marked active via `ITelegramUserRepository.ActivateAsync`
- An audit log row is written to `user_actions` with `action_type = WelcomeBypass` and a reason string describing which rule fired
- An announcement message is posted in the chat and scheduled for auto-deletion after the configured TTL

## Design

### 1. Configuration shape

New nested config object, stored inside the existing Welcome JSONB config row. **No database migration required.**

```csharp
// TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs
public class TrustedBypassConfig
{
    public bool Enabled { get; set; } = false;

    public string AnnouncementMessage { get; set; }
        = "{username} welcomed automatically — trusted from other groups.";

    public int AnnouncementTtlSeconds { get; set; } = 30;
}
```

Wired onto `WelcomeConfig`:
```csharp
public class WelcomeConfig
{
    // ... existing fields ...
    public TrustedBypassConfig TrustedBypass { get; set; } = new();
}
```

**Mappings:** `WelcomeConfigMappings.cs` gets two new lines (one each in `ToModel` and `ToData`) to round-trip the nested object. Defensive null-handling on read: `data.TrustedBypass?.ToModel() ?? new TrustedBypassConfig()` ensures existing chats without the field in their JSONB blob load with defaults.

### 2. Bypass resolver service

```csharp
// TelegramGroupsAdmin.Telegram/Services/Welcome/IWelcomeBypassResolver.cs
public interface IWelcomeBypassResolver
{
    Task<BypassDecision> ResolveAsync(
        UserIdentity user,
        ChatIdentity chat,
        CancellationToken cancellationToken);
}

public enum BypassDecision
{
    None = 0,
    WebAdmin = 1,
    Trusted = 2,
}
```

**Implementation** is a singleton using `IServiceScopeFactory` for scoped dependencies (matching the `WelcomeAdmissionHandler` precedent):

```csharp
public sealed class WelcomeBypassResolver(
    IServiceScopeFactory scopeFactory,
    ILogger<WelcomeBypassResolver> logger) : IWelcomeBypassResolver
{
    public async Task<BypassDecision> ResolveAsync(
        UserIdentity user, ChatIdentity chat, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Rule 2: Web admin (always on)
        var mappingRepo = sp.GetRequiredService<ITelegramUserMappingRepository>();
        var permissionLevel = await mappingRepo.GetPermissionLevelByTelegramIdAsync(user.Id, cancellationToken);
        if (permissionLevel is (int)PermissionLevel.GlobalAdmin or (int)PermissionLevel.Owner)
        {
            logger.LogInformation(
                "Welcome bypass: {User} in {Chat} - linked web admin (level {Level})",
                user.ToLogInfo(), chat.ToLogInfo(), permissionLevel);
            return BypassDecision.WebAdmin;
        }

        // Rule 3: Trusted user (toggle-gated)
        var configService = sp.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chat.Id)
                     ?? WelcomeConfig.Default;
        if (!config.TrustedBypass.Enabled)
        {
            return BypassDecision.None;
        }

        var userRepo = sp.GetRequiredService<ITelegramUserRepository>();
        if (await userRepo.IsTrustedAsync(user.Id, cancellationToken))
        {
            logger.LogInformation(
                "Welcome bypass: {User} in {Chat} - trusted user, bypass enabled",
                user.ToLogInfo(), chat.ToLogInfo());
            return BypassDecision.Trusted;
        }

        return BypassDecision.None;
    }
}
```

**DI:** `services.AddSingleton<IWelcomeBypassResolver, WelcomeBypassResolver>()` in `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`.

### 3. WelcomeService flow change + audit + announcement

A new "Step 2.5" is inserted into `WelcomeService.HandleChatMemberUpdateAsync`, after the pre-banned early-out (Step 2) and before permission restriction (Step 3):

```csharp
// Step 2: Pre-banned early-out (existing, unchanged)

// Step 2.5: NEW - Trusted / web-admin bypass
var bypassDecision = await bypassResolver.ResolveAsync(userIdentity, chatIdentity, cancellationToken);
if (bypassDecision != BypassDecision.None)
{
    await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);
    await auditHandler.LogWelcomeBypassAsync(userIdentity, chatIdentity, bypassDecision, cancellationToken);
    await PostBypassAnnouncementIfConfiguredAsync(
        chatMemberUpdate.Chat, user, config, cancellationToken: cancellationToken);

    var outcome = bypassDecision == BypassDecision.WebAdmin
        ? "skipped_bypass_webadmin"
        : "skipped_bypass_trusted";
    welcomeMetrics.RecordWelcomeOutcome(outcome,
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    return;
}

// Step 3: Restrict permissions (existing, unchanged)
```

**Ordering invariant:** Bypass runs *after* the pre-banned check, so a pre-banned user cannot bypass regardless of trust status.

**Announcement helper** (private method on `WelcomeService`):
```csharp
private async Task PostBypassAnnouncementIfConfiguredAsync(
    Chat chat, User user, WelcomeConfig config, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(config.TrustedBypass.AnnouncementMessage))
        return;
    if (config.TrustedBypass.AnnouncementTtlSeconds <= 0)
        return;

    var text = config.TrustedBypass.AnnouncementMessage
        .Replace("{username}", TelegramDisplayName.FormatMention(user))
        .Replace("{chat_name}", chat.Title ?? string.Empty);

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

Reuses the existing "send a message, schedule its deletion" pattern already used by welcome verify-msg cleanup and ban-celebration GIFs.

**New DI injections into `WelcomeService`:** `IWelcomeBypassResolver bypassResolver`, plus `IJobScheduler` and `IAuditHandler` if not already present.

**No `welcome_responses` row is written.** The existing `WelcomeAdmissionHandler` treats `null = no row = gate cleared`, so any downstream caller correctly admits. We leverage the existing null-semantic instead of introducing a new state.

### 4. Audit log integration

**New `UserActionType` enum value** in both `TelegramGroupsAdmin.Data/Models/UserActionType.cs` and `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`:

```csharp
/// <summary>
/// User auto-admitted past the welcome flow due to trusted status or linked web admin identity.
/// </summary>
WelcomeBypass = 11
```

**New method on `IAuditHandler`:**
```csharp
Task LogWelcomeBypassAsync(
    UserIdentity user,
    ChatIdentity chat,
    BypassDecision decision,
    CancellationToken cancellationToken = default);
```

Implementation writes a `UserActionRecord` with:
- `ActionType = UserActionType.WelcomeBypass`
- `IssuedBy = Actor.WelcomeBypass`
- `ChatId = chat.Id`
- `Reason = decision switch { BypassDecision.WebAdmin => "Linked web admin (GlobalAdmin/Owner)", BypassDecision.Trusted => "Auto-trusted user, bypass enabled", _ => "Bypass" }`

### 5. `SystemActorIds` constants refactor

Create `TelegramGroupsAdmin.Core/Models/SystemActorIds.cs` as the single source of truth for system-actor ID strings. Today these strings are duplicated in two places inside `Actor.cs` (the `public static readonly Actor` declarations and the display-name switch inside `FromSystem`). The one-time refactor lands in this PR because `Actor.cs` is already being edited for the new entry.

```csharp
// TelegramGroupsAdmin.Core/Models/SystemActorIds.cs
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
    public const string WelcomeBypass = "welcome_bypass"; // NEW
}
```

`Actor.cs` is updated so both usage sites reference the constants:
```csharp
public static readonly Actor AutoDetection = FromSystem(SystemActorIds.AutoDetection);
// ... and so on for all 18 existing + 1 new ...
public static readonly Actor WelcomeBypass = FromSystem(SystemActorIds.WelcomeBypass);

// Display-name switch becomes:
var displayName = systemIdentifier switch
{
    SystemActorIds.AutoDetection => "Auto-Detection",
    // ...
    SystemActorIds.WelcomeBypass => "Welcome Bypass",
    _ => systemIdentifier
};
```

### 6. UI: `WelcomeSystemConfig.razor` edits

New expansion panel titled "Trusted User Bypass", placed below the "Security on Join" block and above the welcome-mode selection. Mirrors the existing CAS / Impersonation / Username Blacklist pattern: a `MudSwitch` in `TitleContent` plus caption, with configuration fields in `ChildContent` that are `Disabled` (not hidden) when the toggle is off.

```razor
<MudItem xs="12">
    <MudDivider Class="my-2" />
    <MudText Typo="Typo.subtitle2" Class="mb-2">
        <MudIcon Icon="@Icons.Material.Filled.VerifiedUser" Size="Size.Small" Class="mr-1" />
        Trusted User Bypass
    </MudText>
    <MudText Typo="Typo.caption" Class="mud-text-secondary mb-3">
        When enabled, users with trusted status (auto-trust or manual) skip the welcome flow
        and all security checks. Linked web administrators (Owner, GlobalAdmin) always bypass
        regardless of this toggle.
    </MudText>
</MudItem>

<MudItem xs="12">
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
                bypassed - whether trusted or a linked web admin - so other admins know
                this user was auto-admitted.
            </MudText>
            <MudGrid>
                <MudItem xs="12">
                    <MudTextField @bind-Value="_config.TrustedBypass.AnnouncementMessage"
                                  Label="Announcement Message"
                                  Lines="3"
                                  Disabled="!_config.TrustedBypass.Enabled"
                                  HelperText="Variables: {username}, {chat_name}" />
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
</MudItem>
```

**Global vs per-chat override** is free via the existing `IConfigService.GetEffectiveAsync<WelcomeConfig>` inheritance model — no new inheritance code needed.

### 7. Testing strategy

**TelegramGroupsAdmin.UnitTests:**
- `TrustedBypassConfigTests` — defaults, JSON round-trip
- `WelcomeConfigMappingsTests` — round-trip the nested object, null-safe handling
- `WelcomeBypassResolverTests` — parameterized matrix of 7 cases (Owner linked, GlobalAdmin linked, chat-only Admin, unlinked+trusted+toggle on, unlinked+trusted+toggle off, unlinked+untrusted, null config)
- `WelcomeServiceTests` — new bypass test group covering WebAdmin/Trusted/None branches, pre-banned ordering invariant, empty announcement, zero TTL
- `AuditHandlerTests` — `LogWelcomeBypassAsync` writes correct row
- `SystemActorIdsTests` — every `Actor` static field derives from `SystemActorIds.*`

**TelegramGroupsAdmin.IntegrationTests:**
- `WelcomeFlowBypassIntegrationTests` — real Postgres, synthetic `ChatMemberUpdated`, assert `user_actions` row exists, `messages` row exists, Quartz job enqueued, no `welcome_responses` row, pre-banned still banned
- `DeleteMessageJobBypassTests` — verify scheduled delete actually executes with short TTL

**TelegramGroupsAdmin.ComponentTests:**
- `WelcomeSystemConfigTests` extension — new panel renders, toggle controls field disable state, save sends `TrustedBypass` to `ConfigService.SetAsync`

**TelegramGroupsAdmin.E2ETests:** None. The feature's UI behavior is a standard config section; bUnit covers all component-level concerns, and the generic "save and reload per-chat config" plumbing is already validated by existing E2E tests for other config sections.

**New test infrastructure:**
- `TestWelcomeConfigBuilder.WithTrustedBypass(enabled, message, ttlSeconds)` helper
- New SQL fixture seeding a `telegram_user_mappings` row linking a test Owner web user to a test Telegram user ID

### 8. Observability

**Prometheus (via `WelcomeMetrics.RecordWelcomeOutcome`):** Two new label values on the existing `welcome_outcome` counter:
- `skipped_bypass_webadmin`
- `skipped_bypass_trusted`

No new instruments, no new meters.

**Logs:** The resolver logs at INFO when a bypass fires (with structured user/chat/decision fields). `WelcomeService` relies on the existing outcome log + metric pattern. `AuditHandler.LogWelcomeBypassAsync` logs at DEBUG.

**Audit page (`Audit.razor`):** The new `UserActionType.WelcomeBypass` appears automatically in the filter dropdown (populated by `Enum.GetValues`). The new `Actor.WelcomeBypass` renders as "System: Welcome Bypass" via the `FromSystem` display-name switch. No new queries or views required.

**Welcome analytics page (`WelcomeAnalytics.razor`):** Intentionally unchanged. Bypassed users don't create `welcome_responses` rows, so they don't dilute the accept/deny/timeout distributions the analytics page is designed to show. Admins who want a bypass count filter the Audit page by `UserActionType.WelcomeBypass`.

## Implementation plan tasks (feeds into writing-plans)

1. Add `SystemActorIds` constants class and refactor `Actor.cs` to use it
2. Add `UserActionType.WelcomeBypass = 11` enum value (both copies)
3. Add `Actor.WelcomeBypass` static field
4. Create `TrustedBypassConfig`, wire onto `WelcomeConfig`, update `WelcomeConfigMappings`
5. Create `IWelcomeBypassResolver` + `WelcomeBypassResolver` + `BypassDecision` enum; DI registration
6. Add `IAuditHandler.LogWelcomeBypassAsync` + implementation
7. Modify `WelcomeService.HandleChatMemberUpdateAsync` — inject resolver/job-scheduler/audit, add Step 2.5, add `PostBypassAnnouncementIfConfiguredAsync` helper
8. Add new metric label usage in `WelcomeMetrics` (no class changes — labels are strings)
9. Update `WelcomeSystemConfig.razor` with new expansion panel
10. Update `TestWelcomeConfigBuilder`
11. Add SQL fixture for linked-admin test seed
12. Write unit tests (config, mappings, resolver, welcome service, audit handler, actor ids)
13. Write integration tests (welcome flow bypass, delete job)
14. Extend bUnit component tests
15. **File GitHub issue for admin-on-admin moderation hierarchy + confirmation UX** as a follow-up to this PR; file it at whichever implementation step the plan assigns it to, not before
16. Verify metrics dashboard auto-picks up new outcome labels in Grafana
17. Manual smoke test: enable bypass in a test chat, have a trusted user join, verify announcement appears and is auto-deleted after TTL, verify audit row in UI

## Risks & mitigations

- **Risk:** A compromised admin account could enable bypass globally + manually trust a malicious user, walking them past all checks.
  **Mitigation:** Detection via audit trail — every bypass generates a `user_actions` row attributable to `Actor.WelcomeBypass` with `chat_id` populated. Remediation is `UntrustUserAsync` + ban. Threshold is set by admin-account security, not by this feature.

- **Risk:** Admin confusion about why a user joined without the usual welcome UX.
  **Mitigation:** The auto-announcement message is precisely for this — it explicitly states the bypass reason in-chat. Audit page provides after-the-fact investigation.

- **Risk:** Silent failure of announcement delivery (rate limit, network) could make admins unaware of a bypass.
  **Mitigation:** Announcement delivery failure logs at ERROR via existing `WelcomeService` try/catch. Audit row is written *before* announcement, so the audit trail is authoritative even if the announcement fails.

- **Risk:** Existing chats upgrading suddenly start bypassing.
  **Mitigation:** Default is `TrustedBypass.Enabled = false`. Bypass is strictly opt-in per chat. Web-admin bypass only fires for users with an active `telegram_user_mappings` row, which requires an explicit `/link` command flow — no retroactive behavior change.

## Open questions

None at the time of writing. All design decisions are settled.
