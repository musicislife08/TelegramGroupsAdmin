# Users Page: All Tab + Action History Chat Context — Design

**Date:** 2026-09-13
**Issues:** none filed yet — two long-standing UI bugs reported directly in session.

## Summary

Two bugs on the Users page, one root cause each.

**Bug 1 — newly joined users are invisible on the Active tab, and can be invisible on every
tab.** `telegram_users.is_active` means "passed the join gate": it is inserted `false` by
`GetOrCreateAsync` when a user joins, flipped `true` by `ActivateAsync` on admission or by
`UpsertAsync` on their first message, and is **never set back to `false`**. The Users page,
however, reads it as "not kicked":

| Tab | Today's predicate | What it actually shows |
|---|---|---|
| Active | `is_active && !is_banned` | users who passed the gate — hides everyone still pending welcome / exam / profile review |
| Kicked | `!is_active && !is_banned` | everyone who has *not* passed the gate — pending joiners and never-kicked legacy rows included |
| Trusted | `is_active && is_trusted` | hides trusted users who never passed the gate |
| Banned | `is_banned && (expiry null or future)` | drops a user whose temp ban expired but was not yet cleared — and every other tab excludes them via `!is_banned` |

Search and sort on a tab cannot surface a user the tab predicate excluded, because the row is
removed before paging. Every tab is a predicate, so any user the predicates disagree about
appears nowhere. That is the core problem: **there is no view that is guaranteed to contain a
given user.**

Prod snapshot (2026-09-13): 960 active vs 1143 inactive non-banned users; of the inactive, 49
have a latest welcome response of Pending/Accepted (in the pipeline, never removed) and the
rest were removed (Timeout 1036, Left 41, Denied 11) or predate the welcome flow (6). 54 users
who were kicked *after* becoming active still show on Active.

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
- **Add an All tab, first and default.** No status predicate at all — only the existing system
  user exclusion and the chat-scope filter apply. This is the guarantee: every user an admin is
  allowed to see is findable, searchable, and sortable here regardless of what the status
  columns say. Kass chose this over a Pending tab precisely because a Pending tab is one more
  predicate that can leave gaps.
- **Show gate state on All rows.** Without it a pending joiner and an active member look
  identical. `TelegramUserListItem` gains `IsActive`; the All tab renders a small
  "Unverified" chip when `!IsActive && !IsBanned`. Existing status badges (trusted, banned,
  warned, tagged, admin) continue to render as today.
- **Leave Active, Tagged, Kicked, Banned predicates unchanged.** They are filtered views; All is
  the source of truth. Fixing Kicked's mislabeling of pending users is not needed once All
  exists, and stays out of scope. The Kicked tab description is reworded to be honest about
  what it shows.
- **Drop the `is_active` condition from the Trusted tab and its count** (proposal — flag for
  Kass). Trust is global and set explicitly; a trusted user should be listed regardless of gate
  state. Two prod users are hidden today. If rejected, Trusted stays as is and this bullet is
  removed.
- **Centralize activation in `WelcomeAdmissionHandler`.** `ActivateAsync` runs once inside
  `TryAdmitUserAsync` on the `Admitted` path. The `WelcomeService` and `ExamFlowService` call
  sites that follow an `Admitted` result are removed. The `WelcomeBypass` path does not go
  through the admission handler and keeps its own call.
- **Bug 2: add `ChatName` to `UserActionRecord`, render "in {chat}".** Follow the existing
  `Target*` enrichment pattern: an optional trailing record field populated by a
  `LeftJoin` on `managed_chats` in the detail query. Fall back to the raw chat id when the chat
  is no longer managed.
- **No data backfill.** Legacy admitted-but-inactive rows are left alone; they are visible on
  All with the Unverified chip.

## Invariants

- **Every `telegram_user_id != 0` row visible under the caller's chat scope appears on All.**
  No status column may exclude a row from All. Under global scope, All's count equals the
  table row count minus the system user.
- **A user with an `Admitted` result from `TryAdmitUserAsync` is `is_active = true`**,
  regardless of which caller (welcome, DM welcome, exam, profile-scan allow) triggered it.
- **Zero change to `is_active` write semantics** beyond consolidating where the existing
  `ActivateAsync` call happens.
- **Chat-scoped admins see the same message-scope filtering on All as on every other tab.**

## Part 1 — All tab

### Enum, counts, list item

- `UserListFilter` gains `All` as the first member (`All, Active, Tagged, Trusted, Kicked`).
- `UserTabCounts` gains `AllCount`.
- `TelegramUserListItem` gains `bool IsActive`.

### Repository (`TelegramUserRepository`)

- `GetPagedUsersAsync`: `case All` applies no predicate (the `TelegramUserId != 0` base filter,
  chat scope, and search still apply). Projection adds `IsActive = u.IsActive`. Trusted drops
  `u.IsActive &&` (per the flagged decision).
- `GetUserTabCountsAsync`: add `allCount = baseQuery.CountAsync()`; Trusted count drops
  `IsActive`.
- `PopulateUserStatsAsync` (list enrichment): the "skip banned lookup" shortcut remains for
  Active/Kicked only; All needs the banned lookup so the banned badge renders. Everything else
  is unchanged.

### Users page (`Users.razor`)

- New `MudTabPanel Text="All"` as the first panel. Icon `Groups`, badge `AllCount`, badge
  colour `Color.Default`. Description: "Every user the bot has seen in your groups. Use this
  tab when you cannot find someone elsewhere."
- Columns: User (with badges), Status (the existing status chip; sortable), Last Seen (default
  sort descending so new joiners are on top), Actions (View Details; Trust / Remove Trust
  toggle). The Status cell additionally renders an "Unverified" `MudChip` (size small,
  `Color.Default`, outlined) next to the status chip when `!context.IsActive && !context.IsBanned`.
- `_allTable` ref, `LoadAllServerDataAsync` delegating to the shared loader, and an `_allTable`
  line in the existing `ReloadServerData()` block that runs after trust / ban actions.
- `MudTabs` default `ActivePanelIndex` is the All tab (index 0 after insertion).
- Kicked tab description reworded: "Users who have not passed the join gate and are not
  banned — welcome timeouts, declined rules, left before verifying, admin kicks, and users
  still waiting to verify."

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

If `ITelegramUserRepository` becomes unused in `ExamFlowService` after this, remove the
injection. `WelcomeService` still uses it for `GetOrCreateAsync` and the bypass activation.

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
- `GetPagedUsersAsync_All_ReturnsInactiveNonBannedUser` (the pending-joiner case)
- `GetPagedUsersAsync_All_ReturnsUserWithExpiredBanFlagStillSet` (the gap no other tab covers)
- `GetPagedUsersAsync_All_ExcludesSystemUser`
- `GetPagedUsersAsync_All_ProjectsIsActive`
- `GetPagedUsersAsync_All_SearchFindsInactiveUser`
- `GetUserTabCountsAsync_AllCount_EqualsNonSystemRowCount_UnderGlobalScope`
- `GetPagedUsersAsync_All_RespectsChatScope` (scoped admin, user without messages in scope is absent)
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
- `Users_HasExpectedTabs` asserts the new "All" tab is present and first.

## Out of scope

- Redefining `is_active`, adding a membership/status column, or clearing `is_active` on kick.
- Changing the Active, Tagged, Kicked, or Banned predicates.
- Backfilling legacy admitted-but-inactive rows or clearing expired ban flags.
- Chat names on the moderation-queue / audit list views that also consume `UserActionRecord`.
