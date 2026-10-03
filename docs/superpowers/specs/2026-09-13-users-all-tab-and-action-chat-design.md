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

**Bug 2 — action history entries do not say which group.** The audit row is a log entry, and
its `reason` is written without the chat even though every chat-scoped write site in
`AuditHandler` holds a `ChatIdentity` at that moment and drops it to `chat_id` one hop before
storage. The description must be composed where the identity exists, at write time, with the
log formatter; the Data project stores ids only and the read path never reconstructs names for
this purpose.

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
- **Bug 2: tag the audit reason with the chat at write time — `[Main Community] Welcome timeout`.**
  Kass (2026-09-13): the audit table is a form of log, so the log formatter is the right tool,
  and the string is computed above the Data project where the `ChatIdentity` already exists.
  Prefix chosen over a suffix or per-action sentence because it never produces a broken
  fragment (830 Delete rows have no reason at all) and needs nothing from the action type.
  `chat.ToLogInfo()` supplies the name or `Chat {id}`. Names are frozen at write time, as in any
  log line; existing rows keep their current text, no backfill. The read path and the dialog do
  not change for this: the timeline already renders `Reason`.
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

- New `TelegramGroupsAdmin.Core/Utilities/AuditReason.cs` (one type per file):
  `static string? WithChatTag(ChatIdentity? chat, string? reason)` — returns `reason` unchanged
  when `chat` is null; otherwise `"[" + chat.ToLogInfo() + "]"`, followed by a space and the
  reason when the reason is non-blank.
- `AuditHandler.CreateRecord` takes `ChatIdentity? chat` instead of `long? chatId`, stores
  `ChatId: chat?.Id`, and stores `Reason: AuditReason.WithChatTag(chat, reason)`. The five
  chat-scoped `Log*Async` methods (Delete, Restrict, RestorePermissions, Kick, WelcomeBypass)
  pass their `ChatIdentity` through. Global actions (Ban, TempBan, Unban, Warn, Trust, Untrust)
  are untouched.
- The one direct `user_actions` write outside `AuditHandler` that carries a chat — the
  ProfileChange row in `MessageProcessingService` — uses the same helper with
  `ChatIdentity.From(message.Chat)`.
- The two `BotChatService` auto-trust rows that hand-build `"Admin in chat {id} ({title})"`
  into the reason switch to `$"Admin in {ChatIdentity.From(chat).ToLogInfo()}"`. Their
  `ChatId` stays null (trust is global); this is the same formatter rule applied consistently.
- **Reverted from the earlier design:** no `ChatName` on `UserActionRecord`, no `managed_chats`
  join in `GetUserDetailAsync`, no chat line in the dialog timeline. The timeline renders
  `Reason` as it always did.

## Testing

Test types follow the repo's four-type boundary rule.

**Integration (`TelegramUserRepositoryTests`)** — preconditions come from canonical
(golden) rows pinned in `GoldenDatasetConstants`, never from SUT writes or raw inserts in the
test body (Kass, 2026-09-13; see the integration-test data rules in context-keep). Canonical
already holds four welcome-timeout kicked joiners (`is_active=false`, no messages); two of
them are edited in place so the dataset stays scrubbed-real rather than growing synthetic
rows:

| Anchor | Canonical row | Edit |
|---|---|---|
| Kicked joiner | `9171379870502` (@luminanceflagstick) | none |
| Trusted kicked joiner | `9301917046112` (@tadpolesleek) | `is_trusted` → `true` |
| Expired temp-ban, flag still set | `9995544961449` (@curveabdominal) | `is_banned` → `true`, `ban_expires_at` → 12h after the kick, `banned_at` → kick time |

- `GetPagedUsersAsync_All_ReturnsKickedJoiner_ThatActiveHides`
- `GetPagedUsersAsync_All_ReturnsUserWithExpiredBanFlagStillSet` (the gap no other tab covers; guards its precondition by reading the row)
- `GetPagedUsersAsync_All_ExcludesSystemUser`
- `GetPagedUsersAsync_All_ProjectsIsActive`
- `GetPagedUsersAsync_All_SearchFindsKickedJoiner`
- `GetUserTabCountsAsync_AllCount_EqualsNonSystemRowCount_UnderGlobalScope`
- `GetPagedUsersAsync_All_RespectsChatScope` (kicked joiner has no messages, so a MainChat-scoped admin does not see them)
- `GetPagedUsersAsync_Trusted_IncludesInactiveTrustedUser` (guards its precondition by reading the row)

**Unit**
- `WelcomeAdmissionHandlerTests`: `TryAdmitUserAsync_Admitted_ActivatesUser`;
  `TryAdmitUserAsync_ProfileHold_DoesNotActivate`; `TryAdmitUserAsync_WelcomePending_DoesNotActivate`.
- `WelcomeServiceTests` / `ExamFlowServiceTests`: remove or invert any assertion that the
  service itself calls `ActivateAsync` (one existing reference in `WelcomeServiceTests`).
- `ProfileScanHandlerTests`: no direct activation assertion needed — the handler is mocked;
  covered by the admission handler tests.
- `AuditReasonTests` (Core utilities): null chat → reason unchanged; named chat + reason →
  `[Main Community] reason`; named chat + null/blank reason → `[Main Community]`; unnamed chat →
  `[Chat -100…] reason`.
- `AuditHandlerTests`: every chat-scoped `Log*Async` stores the tagged reason (Kick, Delete with
  null reason, RestorePermissions, Restrict with and without chat, WelcomeBypass); a global
  action (Ban) stores the caller's reason verbatim. The three existing WelcomeBypass tests that
  asserted the caller text verbatim now assert the tagged form.

**Component (`UserDetailDialogTests`)**
- No chat-specific assertions; the timeline renders `Reason` unchanged. The fixture keeps the
  `[SetUp]` that disposes rendered components between tests.

**E2E (`UsersTests`)**
- `Users_HasExpectedTabs` asserts the new "All" tab is present and first.

## Out of scope

- Redefining `is_active`, adding a membership/status column, or clearing `is_active` on kick.
- Changing the Active, Tagged, Kicked, or Banned predicates.
- Backfilling legacy admitted-but-inactive rows or clearing expired ban flags.
- Backfilling the chat tag onto existing audit rows.
- Any read-time name resolution for audit rows (the description is complete when written).
