# Trusted User Bypass for Welcome System — Design

**Date:** 2026-04-17
**Status:** Draft
**Branch:** `feat/trusted-user-bypass-welcome`

## Overview

Add an opt-in per-chat capability to let trusted users (and linked web administrators) skip the welcome consent flow and all join-time security checks. A short, auto-deleting announcement is posted in the chat whenever a bypass fires so admins and members see why the user walked in clean.

## Motivation

TGA tracks "trust" as a global flag on `telegram_users` that can be earned automatically (posting N non-spam messages subject to an account-age gate) or granted manually by an admin. Either way, trusted users still go through the full welcome consent flow (restrict, Accept/Deny or entrance exam, timeout) every time they join a new chat, even though they've already been vetted. That's friction admins don't want.

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

**Bypass rules, in priority order (first match wins). All three share identical effective behavior; only the audit reason and metric label differ.**

1. **Telegram chat admin / creator** — `ChatMember.Status` is `Administrator` or `Creator`. **Always on**, not configurable.
2. **Linked web admin** — joining user's Telegram ID resolves via `ITelegramUserMappingRepository.GetPermissionLevelByTelegramIdAsync` to permission level `GlobalAdmin (1)` or `Owner (2)`. **Always on**, not configurable.
3. **Trusted user** — `telegram_users.is_trusted = true` AND `WelcomeConfig.TrustedBypass.Enabled = true` for the chat. **Toggle-gated**, default off.

When any rule fires, the behavior is identical:
- User is NOT restricted, NO security checks run (CAS, username blacklist, impersonation, profile scan), NO welcome consent flow
- User is marked active via `ITelegramUserRepository.ActivateAsync`
- An audit log row is written to `user_actions` with `action_type = WelcomeBypass` and a reason string describing which rule fired
- An announcement message is posted in the chat and scheduled for auto-deletion after the configured TTL

**Behavioral change from today:** the existing Step 1 silent-skip for Telegram chat admins (`WelcomeService.cs:138`) is replaced by the unified bypass path. Chat admins will now create audit rows and trigger announcements, matching the other bypass paths. The existing `skipped_admin` metric label is retired in favor of `skipped_bypass_chatadmin`.

## Design

### Constants policy

Every user-visible or DB-persisted string literal introduced by this feature is declared as a named constant, never inlined at the call site. The rule:

- **Used by multiple classes or projects** → lives in its own `static class` (e.g., `SystemActorIds`).
- **Used only within a single class** → a `private const string` at the top of that class (e.g., log format strings in the resolver, reason strings in the audit handler, error strings in switch-expression default arms).
- **Exposed for consumers who need to reference the token** (e.g., UI helper text showing valid template variables) → `public const string` on the owning config class.

This applies to metric labels, audit reason strings, log-message format strings, template variable tokens (`{username}`, `{chat_name}`), and switch-expression error messages. `BackgroundJobNames.DeleteMessage` and similar established constant stores are reused without duplication.

### 1. Configuration shape

New nested config object, stored inside the existing Welcome JSONB config row. **No database migration required.**

```csharp
// TelegramGroupsAdmin.Configuration/Models/Welcome/TrustedBypassConfig.cs
public class TrustedBypassConfig
{
    // Public so UI helper text and service code reference the same token.
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";

    // Defaults are internal consts so tests, UI reset-to-default, and .Default
    // factories all share one source of truth.
    internal const string DefaultAnnouncementMessage =
        UsernameVariable + " welcomed automatically — trusted from other groups.";
    internal const int DefaultAnnouncementTtlSeconds = 30;

    public bool Enabled { get; set; } = false;

    public string AnnouncementMessage { get; set; } = DefaultAnnouncementMessage;

    public int AnnouncementTtlSeconds { get; set; } = DefaultAnnouncementTtlSeconds;
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
    ChatAdmin = 1,   // Telegram chat admin or creator
    WebAdmin = 2,    // Linked GlobalAdmin/Owner
    Trusted = 3,     // IsTrusted + toggle enabled
}
```

**Implementation** is a singleton using `IServiceScopeFactory` for scoped dependencies (matching the `WelcomeAdmissionHandler` precedent):

```csharp
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
                     ?? WelcomeConfig.Default;
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

**DI:** `services.AddSingleton<IWelcomeBypassResolver, WelcomeBypassResolver>()` in `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs`.

### 3. WelcomeService flow change + audit + announcement

A new "Step 2.5" is inserted into `WelcomeService.HandleChatMemberUpdateAsync`, after the pre-banned early-out (Step 2) and before permission restriction (Step 3):

```csharp
// At top of WelcomeService class — error messages for the impossible-to-reach
// switch arms. Only used by the handler below, so they live here, not in a
// shared file.
private const string BypassOutcomeNoneUnreachable =
    "Reached outcome mapping with BypassDecision.None";
private const string BypassOutcomeUnmappedFormat =
    "Unmapped bypass decision: {0}";

// REMOVED: the existing Step 1 "if admin/creator, skip" block at the original
// line 138 is deleted. That responsibility moves into the resolver.

// Step 2: Pre-banned early-out (existing, unchanged)

// Step 2.5: NEW - unified privileged/trusted bypass (chat admin, web admin, or trusted user)
var bypassDecision = await bypassResolver.ResolveAsync(userIdentity, chatIdentity, cancellationToken);
if (bypassDecision != BypassDecision.None)
{
    await telegramUserRepository.ActivateAsync(user.Id, cancellationToken);
    await auditHandler.LogWelcomeBypassAsync(userIdentity, chatIdentity, bypassDecision, cancellationToken);
    await PostBypassAnnouncementIfConfiguredAsync(
        chatMemberUpdate.Chat, user, config, cancellationToken: cancellationToken);

    welcomeMetrics.RecordBypassOutcome(bypassDecision,
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
    return;
}

// Step 3: Restrict permissions (existing, unchanged)
```

The metric label routing is pushed down into `WelcomeMetrics` as a dedicated recording method (per the project's metrics convention — see `TelegramGroupsAdmin.Core/CLAUDE.md`):

```csharp
// TelegramGroupsAdmin.Telegram/Metrics/WelcomeMetrics.cs
public sealed class WelcomeMetrics
{
    // Outcome labels live on the metrics class because it owns their schema.
    // They're private because callers use the typed recording method below.
    private const string OutcomeBypassChatAdmin = "skipped_bypass_chatadmin";
    private const string OutcomeBypassWebAdmin  = "skipped_bypass_webadmin";
    private const string OutcomeBypassTrusted   = "skipped_bypass_trusted";

    public void RecordBypassOutcome(BypassDecision decision, double elapsedMs)
    {
        var outcome = decision switch
        {
            BypassDecision.ChatAdmin => OutcomeBypassChatAdmin,
            BypassDecision.WebAdmin  => OutcomeBypassWebAdmin,
            BypassDecision.Trusted   => OutcomeBypassTrusted,
            BypassDecision.None      => throw new InvalidOperationException(
                                          BypassOutcomeNoneUnreachable),
            _                        => throw new InvalidOperationException(
                                          string.Format(BypassOutcomeUnmappedFormat, decision)),
        };
        RecordWelcomeOutcome(outcome, elapsedMs);
    }
}
```

This keeps the metric-label strings encapsulated in the metrics class (nothing outside `WelcomeMetrics` knows the raw label values), and the handler in `WelcomeService` just calls `welcomeMetrics.RecordBypassOutcome(decision, elapsedMs)`. The error strings are shared between `WelcomeService` and `WelcomeMetrics` because the switch can appear in either — move them to `TelegramGroupsAdmin.Telegram/Services/Welcome/BypassErrorMessages.cs` if both classes end up needing them; otherwise keep as `private const` on whichever is the sole owner.

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

    // Tokens come from TrustedBypassConfig so UI helper text and service code
    // reference the exact same string.
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

Reuses the existing "send a message, schedule its deletion" pattern already used by welcome verify-msg cleanup and ban-celebration GIFs.

**New DI injections into `WelcomeService`:** `IWelcomeBypassResolver bypassResolver`, plus `IJobScheduler` and `IAuditHandler` if not already present.

**No `welcome_responses` row is written.** The existing `WelcomeAdmissionHandler` treats `null = no row = gate cleared`, so any downstream caller correctly admits. We leverage the existing null-semantic instead of introducing a new state.

### 4. Audit log integration

**New `UserActionType` enum value** in both `TelegramGroupsAdmin.Data/Models/UserActionType.cs` and `TelegramGroupsAdmin.Telegram/Models/UserActionType.cs`:

```csharp
/// <summary>
/// User auto-admitted past the welcome flow due to privileged status (Telegram chat admin, linked web admin) or trusted status.
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

Implementation writes a `UserActionRecord` with `ActionType = UserActionType.WelcomeBypass`, `IssuedBy = Actor.WelcomeBypass`, `ChatId = chat.Id`, and the reason string selected from constants declared at the top of `AuditHandler`:

```csharp
// At top of AuditHandler class — only this class emits these strings.
private const string BypassReasonChatAdmin = "Telegram chat admin/creator";
private const string BypassReasonWebAdmin  = "Linked web admin (GlobalAdmin/Owner)";
private const string BypassReasonTrusted   = "Trusted user, bypass enabled";
private const string BypassReasonFallback  = "Bypass";

// Inside LogWelcomeBypassAsync:
var reason = decision switch
{
    BypassDecision.ChatAdmin => BypassReasonChatAdmin,
    BypassDecision.WebAdmin  => BypassReasonWebAdmin,
    BypassDecision.Trusted   => BypassReasonTrusted,
    _                        => BypassReasonFallback,
};
```

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
        When enabled, trusted users skip the welcome flow
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
</MudItem>
```

**Global vs per-chat override** is free via the existing `IConfigService.GetEffectiveAsync<WelcomeConfig>` inheritance model — no new inheritance code needed.

### 7. Testing strategy

**TelegramGroupsAdmin.UnitTests:**
- `TrustedBypassConfigTests` — defaults, JSON round-trip
- `WelcomeConfigMappingsTests` — round-trip the nested object, null-safe handling
- `WelcomeBypassResolverTests` — parameterized matrix of 9 cases: Telegram chat admin → `ChatAdmin`; Telegram chat creator → `ChatAdmin`; Owner linked (not chat admin) → `WebAdmin`; GlobalAdmin linked (not chat admin) → `WebAdmin`; chat-level Admin linked (level 0, not chat admin) → falls through, evaluates trust; unlinked+trusted+toggle on → `Trusted`; unlinked+trusted+toggle off → `None`; unlinked+untrusted → `None`; null config (chat has no welcome config yet) → `None`
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

**Prometheus (via a new `WelcomeMetrics.RecordBypassOutcome(BypassDecision, elapsedMs)` method):** Three new label values on the existing `welcome_outcome` counter, replacing the legacy `skipped_admin` label. The labels are declared as `private const string` fields on `WelcomeMetrics` so no caller sees the raw strings:
- `skipped_bypass_chatadmin` (replaces `skipped_admin` — Telegram chat admins/creators)
- `skipped_bypass_webadmin` (linked GlobalAdmin/Owner)
- `skipped_bypass_trusted` (IsTrusted + toggle on)

No new instruments, no new meters — just one new recording method.

**Dashboard migration:** any Grafana panel filtering on `welcome_outcome{outcome="skipped_admin"}` must be updated to `outcome="skipped_bypass_chatadmin"`. Other existing labels (`accepted`, `denied`, `timeout`, `banned`, `pre_banned`, etc.) are unchanged.

**Logs:** The resolver logs at INFO when a bypass fires (with structured user/chat/decision fields). `WelcomeService` relies on the existing outcome log + metric pattern. `AuditHandler.LogWelcomeBypassAsync` logs at DEBUG.

**Audit page (`Audit.razor`):** The new `UserActionType.WelcomeBypass` appears automatically in the filter dropdown (populated by `Enum.GetValues`). The new `Actor.WelcomeBypass` renders as "System: Welcome Bypass" via the `FromSystem` display-name switch. No new queries or views required.

**Welcome analytics page (`WelcomeAnalytics.razor`):** Intentionally unchanged. Bypassed users don't create `welcome_responses` rows, so they don't dilute the accept/deny/timeout distributions the analytics page is designed to show. Admins who want a bypass count filter the Audit page by `UserActionType.WelcomeBypass`.

## Implementation plan tasks (feeds into writing-plans)

1. Add `SystemActorIds` constants class and refactor `Actor.cs` to use it
2. Add `UserActionType.WelcomeBypass = 11` enum value (both copies)
3. Add `Actor.WelcomeBypass` static field
4. Create `TrustedBypassConfig` with public const template variables (`UsernameVariable`, `ChatNameVariable`) and internal default constants; wire onto `WelcomeConfig`; update `WelcomeConfigMappings`
5. Create `IWelcomeBypassResolver` + `WelcomeBypassResolver` + `BypassDecision` enum; declare log-format `private const` strings at the top of the resolver class; DI registration
6. Add `IAuditHandler.LogWelcomeBypassAsync` + implementation with `private const` bypass-reason strings at the top of `AuditHandler`
7. Modify `WelcomeService.HandleChatMemberUpdateAsync` — inject resolver/job-scheduler/audit, **remove the existing Step 1 chat-admin/creator skip block** (now handled by the resolver), add Step 2.5 for the unified bypass, add `PostBypassAnnouncementIfConfiguredAsync` helper that uses `TrustedBypassConfig.UsernameVariable` / `ChatNameVariable` constants for token replacement
8. Add `WelcomeMetrics.RecordBypassOutcome(BypassDecision, double)` method with `private const` outcome-label strings on the class; update existing `skipped_admin` call sites (the retired block in `WelcomeService`) to flow through the new method
9. Update `WelcomeSystemConfig.razor` with new expansion panel
10. Update `TestWelcomeConfigBuilder`
11. Add SQL fixture for linked-admin test seed
12. Write unit tests (config, mappings, resolver, welcome service, audit handler, actor ids)
13. Write integration tests (welcome flow bypass, delete job)
14. Extend bUnit component tests
15. **File GitHub issue for admin-on-admin moderation hierarchy + confirmation UX** as a follow-up to this PR; file it at whichever implementation step the plan assigns it to, not before
16. Verify metrics dashboard auto-picks up new outcome labels in Grafana
17. Manual smoke test: enable bypass in a test chat, have a trusted user join, verify announcement appears and is auto-deleted after TTL, verify audit row in UI
18. When opening the PR, check open issues for related work and reference them in the PR body with `See also #N` (do not use closing keywords) — at the time of design, **#411** ("refactor: Simplify WelcomeService join security pipeline") is the known related open issue. Our change adds a new early-exit (Step 2.5) in the same method #411 plans to reorganize, so the linkage is useful to whoever picks up #411

## Risks & mitigations

- **Risk:** A compromised admin account could enable bypass globally + manually trust a malicious user, walking them past all checks.
  **Mitigation:** Detection via audit trail — every bypass generates a `user_actions` row attributable to `Actor.WelcomeBypass` with `chat_id` populated. Remediation is `UntrustUserAsync` + ban. Threshold is set by admin-account security, not by this feature.

- **Risk:** Admin confusion about why a user joined without the usual welcome UX.
  **Mitigation:** The auto-announcement message is precisely for this — it explicitly states the bypass reason in-chat. Audit page provides after-the-fact investigation.

- **Risk:** Silent failure of announcement delivery (rate limit, network) could make admins unaware of a bypass.
  **Mitigation:** Audit row is written *before* the announcement, so the admin-facing audit trail is authoritative even if in-chat delivery fails — admins investigating via the Audit page will still see every bypass event. Announcement delivery failure also logs at ERROR via existing `WelcomeService` try/catch, but **log access is currently owner-only** — the Internal Logs Page that will give admins log visibility is tracked by **#274 feat: Internal Logs Page (Replace Seq)** and is not yet shipped. Until #274 lands, only the instance owner (via Seq / OTEL backend) sees delivery-failure ERRORs in real time; admins rely on the audit trail alone.

- **Risk:** Existing chats upgrading suddenly start bypassing.
  **Mitigation:** Default is `TrustedBypass.Enabled = false`. Bypass is strictly opt-in per chat. Web-admin bypass only fires for users with an active `telegram_user_mappings` row, which requires an explicit `/link` command flow — no retroactive behavior change.

## Open questions

None at the time of writing. All design decisions are settled.
