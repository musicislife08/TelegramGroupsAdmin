# Users Page: Pending Tab + Action History Chat Context — Design

**Date:** 2026-09-13
**Issues:** none filed yet — two long-standing UI bugs reported directly in session.

## Summary

Two bugs on the Users page, one root cause each.

**Bug 1 — newly joined users are invisible on the Active tab.** `telegram_users.is_active`
means "passed the join gate": it is inserted `false` by `GetOrCreateAsync` when a user joins,
flipped `true` by `ActivateAsync` on admission or by `UpsertAsync` on their first message, and
is **never set back to `false`**. The Users page, however, reads it as "not kicked":

| Tab | Today's predicate | What it actually shows |
|---|---|---|
| Active | `is_active && !is_banned` | users who passed the gate — hides everyone still pending welcome / exam / profile review |
| Kicked | `!is_active && !is_banned` | everyone who has *not* passed the gate — pending joiners and never-kicked legacy rows included |
| Trusted | `is_active && is_trusted` | hides trusted users who never passed the gate |

Search and sort on Active cannot surface a pending joiner because the row is excluded before
paging. Prod snapshot (2026-09-13): 960 active vs 1143 inactive non-banned users; of the
inactive, 49 have a latest welcome response of Pending/Accepted (in the pipeline, never
removed) and the rest were removed (Timeout 1036, Left 41, Denied 11) or predate the welcome
flow (6). 54 users who were kicked *after* becoming active still show on Active.

A second, independent defect in the same area: allowing a profile-scan alert
(`ProfileScanHandler.AllowAsync`) admits the user through `WelcomeAdmissionHandler` but never
calls `ActivateAsync`. Activation is duplicated across four `WelcomeService` sites and one
`ExamFlowService` site, and this fifth admission path was simply missed.

**Bug 2 — action history entries do not say which group.** `UserActionRecord` carries `ChatId`
but no chat name, and the dialog timeline never renders either. Newer RestorePermissions,
Kick, Mute, Delete, and WelcomeBypass rows all have `chat_id` populated; the data is there, it
is a lookup-and-render gap.

## Decisions (from brainstorm)

- **Keep `is_active` semantics as "passed the join gate".** Do not redefine it or add a new
  column. Kass chose this over a membership-based redefinition of Active.
- **Add a Pending tab** for inactive, non-banned users still in the join pipeline. Active keeps
  its current predicate.
- **Kicked = inactive, non-banned, and not Pending.** This deliberately does not use
  `kick_count > 0`: in prod, `kick_count` misses 101 users who have a Kick audit row and 229
  welcome-timeout users from before Kick rows were written. Both groups belong under Kicked.
- **Pending is decided by the user's latest `welcome_responses` row.** Latest row Pending or
  Accepted ⇒ Pending; latest row Denied / Timeout / Left, or no row at all ⇒ Kicked. Each join
  inserts a fresh row, so a re-joiner whose earlier attempt timed out correctly moves back to
  Pending. Accepted-but-inactive covers both the profile-review hold and the missed-activation
  bug; both are truthfully "not yet active".
- **Profile-scan hold is not queried directly.** The pending alert lives in `reports.context`
  JSONB keyed by user id; a per-row `JsonContains` with a dynamic string is not translatable in
  a paged LINQ predicate. The latest-welcome-response rule already classifies those users as
  Pending.
- **Centralize activation in `WelcomeAdmissionHandler`.** `ActivateAsync` runs once inside
  `TryAdmitUserAsync` after a successful `RestoreUserPermissionsAsync`. The `WelcomeService`
  and `ExamFlowService` call sites that follow an `Admitted` result are removed. The
  `WelcomeBypass` path does not go through the admission handler and keeps its own call.
- **Drop the `is_active` condition from the Trusted tab and its count** (proposal — flag for
  Kass). Trust is global and set explicitly; a trusted user should be listed regardless of gate
  state. Two prod users are hidden today. If rejected, Trusted stays as is and this bullet is
  removed.
- **Bug 2: add `ChatName` to `UserActionRecord`, render "in {chat}".** Follow the existing
  `Target*` enrichment pattern: an optional trailing record field populated by a
  `LeftJoin` on `managed_chats` in the detail query. Fall back to the raw chat id when the chat
  is no longer managed.
- **No data backfill.** The 40 Accepted-but-inactive legacy rows are left alone; they surface
  on the Pending tab where an admin can see them. A one-off `UPDATE` is out of scope.

## Invariants

- **Active + Pending + Kicked + Banned partition the non-system users.** Every
  `telegram_user_id != 0` row appears in exactly one of the four (Tagged and Trusted are
  overlays). Tab counts must sum accordingly under global scope.
- **A user with an `Admitted` result from `TryAdmitUserAsync` is `is_active = true`**,
  regardless of which caller (welcome, DM welcome, exam, profile-scan allow) triggered it.
- **Zero change to `is_active` write semantics** beyond consolidating where the existing
  `ActivateAsync` call happens.
- **Chat-scoped admins see the same filtering on Pending as on every other tab** (the existing
  `chatIds` message-scope filter applies unchanged).

## Part 1 — Tab predicates

### Enum and counts

- `UserListFilter` gains `Pending` (`Active, Pending, Tagged, Trusted, Kicked`).
- `UserTabCounts` gains `PendingCount`.

### Repository (`TelegramUserRepository`)

Two private query helpers keep the predicate in one place and make Kicked the exact complement:

```csharp
// Latest welcome response for the user, or null when none exists.
// Nullable projection so "no rows" never collapses to the default enum value (Pending = 0).
private static IQueryable<TelegramUserDto> WherePending(IQueryable<TelegramUserDto> q, AppDbContext ctx)
    => q.Where(u => !u.IsActive && !u.IsBanned &&
        ctx.WelcomeResponses
            .Where(w => w.UserId == u.TelegramUserId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => (WelcomeResponseType?)w.Response)
            .FirstOrDefault() is WelcomeResponseType.Pending or WelcomeResponseType.Accepted);

private static IQueryable<TelegramUserDto> WhereKicked(IQueryable<TelegramUserDto> q, AppDbContext ctx)
    => q.Where(u => !u.IsActive && !u.IsBanned &&
        !(ctx.WelcomeResponses ... same projection ... is Pending or Accepted));
```

(Exact expression form is the implementer's call; `is ... or ...` may need to be written as
`== a || == b` for translation. The requirement is: nullable latest-response projection, and
Kicked is the literal negation of Pending's response clause.)

- `GetPagedUsersAsync`: add `case Pending` → `WherePending`; change `case Kicked` →
  `WhereKicked`. Trusted drops `u.IsActive &&` (per the flagged decision).
- `GetUserTabCountsAsync`: add `pendingCount`; `kickedCount` uses `WhereKicked`; Trusted count
  drops `IsActive`.
- `PopulateUserStatsAsync` (the list enrichment): the "skip banned lookup" shortcut extends to
  `Pending` (predicate guarantees `!IsBanned`).
- `IsBanned` on inactive rows: both helpers already require `!IsBanned`, matching today's
  Kicked behaviour.

### Users page (`Users.razor`)

- New `MudTabPanel Text="Pending"` between Active and Tagged. Icon `HourglassEmpty`, badge
  `PendingCount`, badge colour `Color.Info`. Description: "Users who joined and have not yet
  passed welcome verification, the entrance exam, or profile review."
- Columns: User, Joined (`FirstSeenAt`), Last Seen, Actions (View Details, Trust User). This
  needs `FirstSeenAt` added to `TelegramUserListItem` and projected in `GetPagedUsersAsync`.
- New `_pendingTable` ref and `LoadPendingServerDataAsync` delegating to the shared loader.
- Kicked tab description updated: "Users who were removed from the group but not banned —
  welcome timeout, declined rules, left before verifying, or admin kicks."
- The existing reload block that calls `ReloadServerData()` on each table ref (after trust /
  ban actions) gains the `_pendingTable` line.

## Part 2 — Activation consolidation

`WelcomeAdmissionHandler.TryAdmitUserAsync` today awaits `RestoreUserPermissionsAsync` without
inspecting its result and returns `Admitted` unconditionally once all gates are clear. Every
caller then activates on `Admitted`. To keep parity, the handler resolves
`ITelegramUserRepository` from the scope and calls `ActivateAsync(user.Id, ct)` immediately
after the restore call, on the same unconditional path, before returning `Admitted`. Gating
activation on restore success would be a behaviour change and is out of scope.

Remove the now-redundant `ActivateAsync` calls that immediately follow an `Admitted` check:

| File | Site | Action |
|---|---|---|
| `WelcomeService.cs` | ~461 (security passed, welcome disabled) | remove |
| `WelcomeService.cs` | ~1100 (completed welcome in group) | remove |
| `WelcomeService.cs` | ~1266 (completed welcome via DM) | remove |
| `ExamFlowService.cs` | ~779 (exam admitted) | remove |
| `WelcomeService.cs` | ~174 (WelcomeBypass) | **keep** — bypass does not use the admission handler |
| `ProfileScanHandler.AllowAsync` | — | nothing to add; now activated via the handler |

If `ITelegramUserRepository` becomes unused in `WelcomeService` or `ExamFlowService` after
this, remove the injection. It will still be used by `WelcomeService` for `GetOrCreateAsync`
and the bypass activation.

## Part 3 — Action history chat context

- `UserActionRecord` gains `string? ChatName = null` as the final optional parameter.
- `UserActionMappings.ToModel(...)` gains `string? chatName = null` and passes it through.
- `GetUserDetailAsync` actions query adds a fourth `LeftJoin(context.ManagedChats,
  x => x.ua.ChatId, c => c.ChatId, ...)` projecting `ChatName = c != null ? c.ChatName : null`.
- `UserDetailDialog.razor` timeline: when `action.ChatId is not null`, render a second caption
  line `in {action.ChatName ?? action.ChatId.ToString()}` between the reason and the
  "by … · timestamp" line. No change when `ChatId` is null (global actions such as Trust/Ban
  stay as they are).
- The `UserActionsRepository` list methods (moderation queue, audit views) are **not** changed;
  only the detail dialog is in scope.

## Testing

Test types follow the repo's four-type boundary rule.

**Integration (`TelegramUserRepositoryTests`)**
- `GetPagedUsersAsync_Pending_ReturnsInactiveUserWithLatestPendingResponse`
- `GetPagedUsersAsync_Pending_ReturnsInactiveUserWithLatestAcceptedResponse`
- `GetPagedUsersAsync_Pending_ExcludesUserWhoseLatestResponseIsTimeout`
- `GetPagedUsersAsync_Kicked_ReturnsUserWithLatestTimeout_AndUserWithNoWelcomeResponse`
- `GetPagedUsersAsync_Pending_RejoinAfterTimeout_LandsInPendingNotKicked` (two rows, newer Pending)
- `GetPagedUsersAsync_Active_ExcludesPendingUser` (regression guard for current behaviour)
- `GetUserTabCountsAsync_PendingPlusKickedEqualsInactiveNonBanned`
- `GetPagedUsersAsync_Trusted_IncludesInactiveTrustedUser` (only if the Trusted change is approved)
- `GetUserDetailAsync_Actions_IncludeChatNameForManagedChat_AndNullForUnknownChat`

**Unit**
- `WelcomeAdmissionHandlerTests`: `TryAdmitUserAsync_Admitted_ActivatesUser`;
  `TryAdmitUserAsync_ProfileHold_DoesNotActivate`; `TryAdmitUserAsync_WelcomePending_DoesNotActivate`.
- `WelcomeServiceTests` / `ExamFlowServiceTests`: remove or invert any assertion that the
  service itself calls `ActivateAsync` (one existing reference in `WelcomeServiceTests`).
- `ProfileScanHandlerTests`: no direct activation assertion needed — the handler is mocked;
  covered by the admission handler tests.

**Component (`UserDetailDialogTests`)**
- Timeline renders "in {ChatName}" when an action has a chat name.
- Timeline renders the raw chat id when `ChatId` is set and `ChatName` is null.
- Timeline renders no chat line when `ChatId` is null.

**E2E (`UsersTests`)**
- `Users_HasExpectedTabs` asserts the new "Pending" tab is present.

## Out of scope

- Redefining `is_active`, adding a membership/status column, or clearing `is_active` on kick
  (the 54 kicked-after-active users stay on Active; revisit if it bites).
- Backfilling legacy Accepted-but-inactive rows.
- Querying the profile-scan hold directly for the Pending predicate.
- Chat names on the moderation-queue / audit list views that also consume `UserActionRecord`.
