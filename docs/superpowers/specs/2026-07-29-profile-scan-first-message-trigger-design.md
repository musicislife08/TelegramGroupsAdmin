# Profile scan: first-message trigger

Live correctness bug. Users whose first observed activity is a message never get a profile
scan, so spam accounts that reply to channel posts bypass the profile gate entirely.

## Problem

Profile scans have exactly two triggers today:

1. `WelcomeService.cs:397` on join, gated on `ProfileScan.Enabled && ScanOnJoin`. Requires a
   `ChatMember` join update.
2. `MessageProcessingService.cs:671` on message, gated on
   `existingUser is not null && ProfileDiffDetected(...)`.

`ScheduleProfileScanAsync` has one caller, inside that diff branch. A user whose first
observed activity is a message therefore hits neither path: there is no join event, and there
is no prior record to diff against. The comment at `MessageProcessingService.cs:669` states
the assumption that fails:

```csharp
// New users (existingUser == null) get scanned on join, not here.
```

That only holds if every user is seen joining. Accounts replying to auto-forwarded channel
posts in a linked discussion group appear as message authors with no member-join update. They
become scannable only if they later change their profile.

### Evidence

Andrea Ruiz (`8872589479` / `AndreaRuiz83`), 2026-07-29:

```
first_seen_at = created_at = 2026-07-29 20:25:27 UTC
```

The row was created by the spam message itself. No `New user joined` event exists for her.

```
13:25:27  AI veto abstained (Combined text too short (< 20 chars)), TotalScore=0.8
13:25:41  /report by a community member
13:26:01  banned manually by an admin
```

Users who arrived via join the same afternoon were scanned normally: Andrey joined 13:36:50
and was scanned at 13:36:58; Steve joined 13:57:48 and was scanned at 13:57:54.

Scope at time of writing:

| Metric | Count |
|---|---|
| Rows in `telegram_users` | 2,971 |
| Rows with a profile scan result | 889 |
| Unscanned, not banned, not trusted | 1,023 |
| Unscanned posters in the affected discussion group (30d) | 104 |
| Banned spam accounts in the last 14d that were never scanned | 20 of 20 |

## Secondary bug: admin trust is never reconciled

`BotChatService.cs:456` applies admin auto-trust only on the promotion event:

```csharp
var wasNew = !cachedAdminIds.Contains(admin.User.Id);
if (wasNew) { /* AUTO-TRUST */ }
```

An admin promoted before auto-trust existed, or whose one-shot trust write failed inside the
try/catch at line 493, is never back-filled. One active admin is currently in this state
(`2132334638`, promoted 2025-10-24), verified against prod: 41 of 42 active admins carry
`is_trusted`.

This is in scope because the new gate's safety rests on the invariant "active admin implies
trusted". Fixing the invariant is preferable to working around it.

## Design

### The gate

New scoped service in `TelegramGroupsAdmin.Telegram/Services/UserApi/`:

```csharp
public interface IProfileScanGate
{
    /// Runs a profile scan if this trigger is eligible. Returns null when skipped.
    Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user, ChatIdentity? chat, ProfileScanTrigger trigger, CancellationToken ct);
}

public enum ProfileScanTrigger { Join, FirstMessage, ProfileChange }
```

Dependencies: `IConfigService`, `ITelegramUserRepository`, `ITelegramSessionManager`,
`IProfileScanService`, logger.

The gate makes the entire eligibility decision. It does not delegate to a helper predicate.
Checks run cheapest first:

| # | Check | Skip when |
|---|---|---|
| 1 | `ProfileScan.Enabled` | disabled |
| 2 | Trigger flag: `Join`→`ScanOnJoin`, `FirstMessage`→`ScanOnFirstMessage`, `ProfileChange`→`ScanOnProfileChange` | flag off |
| 3 | Load user once via `GetByTelegramIdAsync` | n/a |
| 4 | `IsTrusted` | trusted |
| 5 | `ProfileScannedAt is null`, **`FirstMessage` only** | already scanned |
| 6 | `HasAnyActiveSessionAsync` | no User API session |
| 7 | Delegate to `ScanUserProfileAsync(user, chat, ct)` | n/a |

A null user row (check 3 returns nothing) is treated as not trusted and never scanned, so a
brand-new author is eligible. This is the common case for the bug being fixed: the message
itself creates the row, and the gate runs after that upsert, but the row is also legitimately
absent in the join path.

`Join` deliberately omits check 5 so joins always rescan, preserving current behavior.
Check 2 wires up `ScanOnProfileChange`, which is currently read by no runtime code.

Skips are recorded through the existing `PipelineMetrics.RecordProfileScanSkipped(reason)`.

The gate propagates exceptions rather than swallowing them. Each caller decides what a
failed scan means.

No service-message special case is needed. A join arrives as two updates (`ChatMember` plus
the join service message); whichever lands first performs the one real scan and stamps
`ProfileScannedAt`, and the second then fails check 5 or the 60s freshness dedup inside
`ScanUserProfileAsync`. Exactly one scan results either way.

### Callers

| Caller | Trigger | Change |
|---|---|---|
| `WelcomeService.cs:397` | `Join` | Inline gate replaced by gate call. Welcome-specific UI (delete verifying message, hold message, metrics) stays in `WelcomeService` |
| `MessageProcessingService`, after line 711 | `FirstMessage` | New call site |
| `MessageProcessingService.cs:671` | `ProfileChange` | Switches from scheduling the Quartz job to calling the gate inline |
| `UserDetailDialog.razor:1159` | none | Unchanged. Manual admin rescan intentionally bypasses the gate |
| `ProfileRescanJob.cs:84` | none | Unchanged. Bulk rescan intentionally bypasses the gate |

Gated equals automatic; direct equals admin-initiated. That split exists today but is
undocumented; the gate makes it explicit.

### Placement and ordering

The `FirstMessage` call goes after `UpsertAsync` (line 711) and before content detection
(line 746). After the upsert because the scan writes `profile_scan_results` and may create a
`ProfileScanAlert` report, both FK'd to `telegram_users` — the same reason the join path calls
`GetOrCreateAsync` before scanning. It passes `ChatIdentity.From(message.Chat)` so a
held-for-review alert lands in the triggering chat.

Mechanism matches join exactly: inline and awaited, not queued. The bot processes updates
sequentially by design, and the join path already scans inline on that same thread, so this
inherits proven behavior. `ScanUserProfileAsync` bounds itself at 45s; observed scans run 6-8s.

The gate is called unconditionally for `FirstMessage`, with no pre-check at the call site,
even though `MessageProcessingService` already holds a user row from line 648. That row is
pre-upsert, and splitting the decision across the call site would defeat the point of the
gate. The cost is one extra primary-key lookup per message, which is accepted deliberately in
exchange for the gate owning the whole decision.

### Auto-ban interaction

When the scan returns `Banned`, `ProfileScanService` has already banned the user and scheduled
message cleanup. Content detection is short-circuited in that case: re-deciding a settled case
costs an AI call for no benefit. The message row is already persisted by that point.

### Error handling

`MessageProcessingService` wraps the gate call in a try/catch that logs a warning and
continues processing the message. A failed scan must never cost the message. This mirrors the
existing handling at line 705. `WelcomeService` keeps its current outer catch at line 575,
which fails open, so join behavior is unchanged.

### Retry policy

A scan that fails leaves `ProfileScannedAt` null, so the next message from that user retries,
unbounded. Accepted deliberately: spam accounts post once or twice before being banned, so it
self-limits in practice, and no schema change or backoff state is needed.

### Configuration

`ProfileScanConfig` gains `ScanOnFirstMessage`, default `false`. The per-trigger switches are
kept rather than collapsed into `Enabled`, preserving configurability.

Default-off is deliberate: this change adds inline scan latency to the message path on a
sequential pipeline, and the flag is the rollback that does not need a redeploy.

No DB migration. `ConfigRepository.JsonOptions` uses camelCase with default
`UnmappedMemberHandling`, so the absent `scanOnFirstMessage` key in the existing `configs` row
deserializes to the C# default of `false`.

Touches: `ProfileScanConfig.cs`, `Data/Models/Configs/ProfileScanConfigData.cs`,
`WelcomeConfigMappings.cs` (both directions), `WelcomeSystemConfig.razor` (new switch,
disabled when the master `Enabled` is off).

**Post-deploy step: the flag must be enabled in the UI. The bug persists until then.**

### Admin trust reconciliation

At `BotChatService.cs:456`, `GetOrCreateAsync` on line 453 already returns the user record, so
the auto-trust condition becomes `wasNew || !existing.IsTrusted`. The `UserActionRecord` is
inserted only when trust actually flips, so refresh passes do not spam the audit trail for the
41 already-trusted admins.

### No backfill

The 1,023 existing unscanned users are not swept. They get scanned lazily as they post. This
avoids a mass scan against the WTelegram User API, which is the raid scenario open issue #347
anticipates.

### Dead code removal

Moving `ProfileChange` inline orphans the on-demand scan job. These come out in a separate
commit so they are easy to drop in review:

- `BackgroundJobs/Jobs/ProfileScanJob.cs`
- `BackgroundJobScheduler.ScheduleProfileScanAsync`
- `Core/JobPayloads/ProfileScanPayload.cs`
- `BackgroundJobNames.ProfileScan`
- Its Quartz registration in `BackgroundJobs/Extensions/ServiceCollectionExtensions.cs:114`
- Its mapping in `QuartzJobScheduler.cs:109`

`ProfileRescanJob` (bulk, separate job) is unaffected.

## Testing

### Unit: `ProfileScanGateTests` (new)

Primary coverage, since the decision lives here. Substitutes for the four dependencies,
matching `WelcomeServiceTests`. Assertions are on the gate's return value (null when skipped,
result object when scanned), not on mock call counts.

| Case | Expect |
|---|---|
| `FirstMessage`, untrusted, never scanned, flag on, session active | scans (the regression) |
| `FirstMessage`, user row does not exist yet | scans |
| `FirstMessage`, `ProfileScannedAt` set | null |
| `FirstMessage`, trusted | null |
| `FirstMessage`, `ScanOnFirstMessage` off | null |
| `Join`, already scanned | scans |
| `Join`, `ScanOnJoin` off | null |
| `ProfileChange`, `ScanOnProfileChange` off | null |
| any trigger, `Enabled` off | null |
| no active session | null |
| scan throws | propagates |

### Unit: `WelcomeServiceTests` (existing, must change)

Three tests at lines 328, 470, and 570 assert `DidNotReceive().ScanUserProfileAsync(...)`.
They break once `WelcomeService` calls the gate, and must be retargeted to `IProfileScanGate`.
Their breaking is the signal that the join path was actually rewired.

### Unit: `WelcomeConfigMappingsTests` (existing)

`ScanOnFirstMessage` round-trips through the JSONB DTO in both directions and defaults to
`false` when the key is absent.

### Component: `WelcomeSystemConfigTests` (existing)

The new switch renders and binds, and is disabled when the master `Enabled` is off.

### Integration: `BotChatServiceTests` (existing)

An active admin with `is_trusted = false` gets trusted on refresh. An already-trusted admin
gets no duplicate `user_actions` row.

### Integration: message-path harness (new, may be abandoned)

Proves `MessageProcessingService` calls the gate at the right point with the right trigger.
No existing harness drives `HandleMessageAsync`; the integration file named
`MessageProcessingServiceTests` only reflection-tests static helpers.

`HandleMessageAsync` resolves 24 distinct services across 35 sites, seven of them concrete
handler classes whose non-virtual methods NSubstitute cannot intercept. The approach that
makes this tractable: arrange the substituted `IProfileScanService` to return `Banned`, which
triggers the short-circuit and means `ContentDetectionOrchestrator` (the heaviest leaf,
pulling in the detection engine and AI) is never resolved. Real repos for
`ITelegramUserRepository`, `IMessageHistoryRepository`, `IUserActionsRepository`,
`IUsernameHistoryRepository`, and `IConfigService` against a golden-template clone;
substitutes for the interface-based remainder; the real gate.

**Approved unknown:** if the concrete handlers on the pre-detection path make this
unworkable, or the test proves flaky, abandon it. The regression itself is covered by
`ProfileScanGateTests`; what is lost is coverage of the wiring.

## Out of scope

**Short-text veto abstain.** The single-emoji reply also evaded content detection:
`Combined text too short (< 20 chars)` makes the AI veto abstain, and a rule score of 0.8
alone did not auto-action. Emoji-only spam will still need a human report until this is
addressed. Tracked separately to keep this branch reviewable.

**Backfill of the 1,023 unscanned users.** Deliberate, see above.
