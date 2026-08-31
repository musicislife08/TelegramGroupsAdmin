# Join-gate cleanup — report cross-close, stranded welcome message, DM chat fallback

Three related defects in the join gate (welcome → profile scan → exam → admin review). Not tracked
in GitHub at time of writing.

One branch (`fix/join-gate-cleanup`) → one PR to `develop`, with a commit per bug plus doc commits.

---

## Shared background — how the join gate is wired

Facts that all three sections depend on:

1. **One message, edited in place.** `WelcomeService.HandleChatMemberUpdateAsync` posts a
   "⏳ Verifying..." message (`WelcomeService.cs:194`), then *edits* it into the welcome / exam
   teaser (`:502`). `welcome_responses.welcome_message_id` is the only handle anyone has for
   deleting it later.
2. **Dual admission gate.** `WelcomeAdmissionHandler.TryAdmitUserAsync` clears the user only when
   *both* gates pass: no pending `ProfileScanAlert` for user+chat, and the `WelcomeResponse` is not
   `Pending`. This is why a profile scan and an exam can be open for the same join at the same time.
3. **`WelcomeTimeoutJob` is the only unconditional cleanup path** for the welcome message — and it is
   cancelled the moment the user responds (`WelcomeService.cs:1036`, `:1213`). Every post-response
   path must clean up for itself.
4. **`ReportBase.SubjectUserId` is always `null` today.** `EnrichedReportMappings.ToBaseModel`
   (`:141`) never assigns it, despite the XML doc on `ReportBase` describing per-type semantics.

## Architecture decision — boss tells, workers do

Per the Boss/Worker contract in `TelegramGroupsAdmin.Telegram/Services/Moderation/CLAUDE.md`:
*"When to add logic to the orchestrator: it's a business rule that spans multiple handlers."*

Two new cross-cutting rules land on `BotModerationService` (the boss):

- **A ban/kick closes that user's open reports.**
- **A ban/kick deletes that user's stranded welcome message.**

Each is executed by a new *worker* that owns one domain and decides nothing. Report action handlers
(`ProfileScanHandler`, `ExamHandler`) lose their hand-rolled cleanup — they fetch, call moderation,
and update their own report status, nothing more.

### Deliberate carve-out: `ProfileScanHandler.AllowAsync`

"Allow" performs no moderation action, so the boss is never invoked. Sibling closure for Allow stays
in `ProfileScanHandler`, but is narrowed to what it already does today: close sibling **profile scan
alerts only**, same user, via `GetPendingProfileScanAlertsForUserAsync`. It must *not* be widened to
all report types — an admin allowing a profile scan is a weaker signal than a ban and must not
auto-dismiss a pending exam failure or content report.

---

## Bug 1 — a ban on one report leaves the sibling report open

### Problem

A user joins, a profile scan holds them for review, and an exam failure lands for the same join. Two
rows sit in the reports queue. An admin bans on one; the other stays `Pending` forever.

Two asymmetric holes:

1. `ProfileScanHandler.CleanupSiblingAlertsAsync` (`ReportActions/ProfileScanHandler.cs:188`) queries
   `GetPendingProfileScanAlertsForUserAsync` — **profile scan alerts only**. A pending `ExamFailure`,
   `ImpersonationAlert`, or `ContentReport` for the same user survives the ban.
2. `ReportActions/ExamHandler.cs` has **no sibling cleanup at all**. `DenyAndBanAsync` bans the user
   globally and leaves every other report for them pending — including the profile scan alert that
   is still blocking admission.

There is no cross-cutting rule anywhere. `BotModerationService.BanUserAsync` (`:179`) revokes trust,
notifies admins, and schedules message cleanup, but never touches the reports queue.

### Scope decision

**All four report types, all chats, on ban. Same chat only, on kick.** A global ban is a statement
about the user; a kick is a statement about one chat.

### Blocker — the subject user is not queryable for all types

`enriched_reports` exposes a subject-user column for three of the four types
(`suspected_user_id`, `exam_user_id`, `profile_user_id`). **`ContentReport` has none** — its subject
is the reported message's author, reachable only via `reports.message_id + chat_id → messages.user_id`.

`messages` has PK `(message_id, chat_id)` (`AppDbContext.cs:105`), so that join is index-covered.

### Approach

**1. View migration.** Add a `content_user_id` column to `EnrichedReportView.CreateViewSql`:

```sql
-- ContentReport: message author (type = 0)
content_msg.user_id AS content_user_id,
```

with the join:

```sql
-- ContentReport author (only for type = 0)
LEFT JOIN messages content_msg
    ON r.type = 0
    AND content_msg.chat_id = r.chat_id
    AND content_msg.message_id = r.message_id
```

No `telegram_users` join — only the id is needed for filtering. Migration follows the existing
drop/recreate pattern from `20260223213357_UpdateEnrichedReportsViewForProfileScan`.

**2. Populate `ReportBase.SubjectUserId`** in `ToBaseModel`, switching on `view.Type`:
ContentReport → `ContentUserId`, ImpersonationAlert → `SuspectedUserId`, ExamFailure → `ExamUserId`,
ProfileScanAlert → `ProfileUserId`. This fixes the always-null field and is what the new worker reads.

**3. New repository method** on `IReportsRepository`:

```csharp
Task<List<ReportBase>> GetPendingForUserAsync(
    long userId,
    long? chatId = null,
    CancellationToken cancellationToken = default);
```

Pending rows where any of the four subject-user columns equals `userId`; `chatId` narrows to one chat
when supplied.

**4. New worker** `Moderation/Actions/ReportCleanupHandler` (`IReportCleanupHandler`):

```csharp
Task<int> CloseOpenReportsAsync(
    UserIdentity user,
    ChatIdentity? chat,
    Actor executor,
    string actionName,
    long? excludeReportId,
    CancellationToken cancellationToken = default);
```

Fetches pending reports, skips `excludeReportId`, calls `TryUpdateStatusAsync(id,
ReportStatus.Reviewed, executor.GetDisplayText(), $"Auto-{actionName}", note)` per row, returns the
count closed. `TryUpdateStatusAsync` is already atomic-on-pending, so a concurrent admin decision
wins the race and is left alone.

**5. Boss wiring.** `BanUserAsync` calls it with `chat: null` (all chats); `KickUserFromChatAsync`
calls it with `chat: intent.Chat`. Both inside `SafeExecuteAsync` — cleanup must never fail a ban
that already landed on Telegram.

### Regression trap — the originating report closes itself

`ProfileScanHandler.BanAsync` calls `moderationService.BanUserAsync` **first**, then marks its own
alert `Reviewed`. Once the boss closes every pending report for the user, it closes the originating
alert too — and the handler's `ReportStatusHelper.TryUpdateStatusAsync` then loses the race, falls
into `onRaceLost`, and returns *"Already handled by Auto-Ban…"* to the admin **as a failure**.

Fix: carry the originating report id on the intent so the boss skips it. Add to the base record
`ModerationIntent` (so `BanIntent` and `KickIntent` both get it):

```csharp
/// <summary>
/// Report id that triggered this action, when any. The orchestrator's report-cleanup
/// rule skips it so the calling handler keeps ownership of its own status update.
/// </summary>
public long? OriginReportId { get; init; }
```

Threaded from `ProfileScanHandler.BanAsync`/`KickAsync` (`alertId`) and from
`ExamFlowService.DenyExamFailureAsync`/`DenyAndBanExamFailureAsync`, which need a new `long?
originReportId` parameter — `ExamHandler` has the `examId` and currently drops it on the floor for
the denial paths.

Ordering is *not* changed: moderation still runs before the status update, so a failed ban still
leaves the report pending.

**6. Remove** `ProfileScanHandler.CleanupSiblingAlertsAsync` from the `BanAsync` and `KickAsync`
paths (the boss now owns it, more broadly). It stays for `AllowAsync` per the carve-out above.

---

## Bug 2 — the welcome message is left in the chat after a profile scan report is resolved

### Problem

`ExamFlowService` cleans up after itself: `ExecuteExamApprovalAsync` (`ExamFlowService.cs:749`) and
`ExecuteExamDenialAsync` (`:884`) both fetch the welcome response, delete
`welcomeResponse.WelcomeMessageId`, and update the response record.

`ProfileScanHandler.BanAsync` / `KickAsync` / `AllowAsync` do **neither**. They inject
`IWelcomeResponsesRepository` only to read the `Timeout` state in `AllowAsync`.

The message survives because the fallback cleaner is already gone by then. If the user clicked
Accept, `HandleAcceptAsync` cancelled the timeout job (`WelcomeService.cs:1036`) and edited the
message to *"⏳ Your profile is under admin review. Please wait..."* (`:1099`). The admin then bans
via the alert, and nothing deletes it. That hold text is what is stranded in the chat.

### Approach

**New worker** `Moderation/Actions/WelcomeCleanupHandler` (`IWelcomeCleanupHandler`):

```csharp
Task<int> DeleteStrandedWelcomeMessagesAsync(
    UserIdentity user,
    ChatIdentity? chat,
    Actor executor,
    CancellationToken cancellationToken = default);
```

Looks up the user's welcome response(s) — one chat, or all chats for a global ban — and deletes each
`WelcomeMessageId` through `IBotModerationMessageHandler.DeleteAsync` (audited delete, same as the
exam path). Returns the count deleted. Deleting an already-deleted message is a no-op at the
Telegram API level, so the operation is idempotent.

Needs a new repository method on `IWelcomeResponsesRepository`:

```csharp
Task<List<WelcomeResponse>> GetByUserAsync(long userId, CancellationToken cancellationToken = default);
```

**The worker does not touch `welcome_responses.response`.** The final state (`Denied` / `Timeout` /
`Left`) is a semantic each caller legitimately owns, and callers write it *after* calling the boss —
having the boss write `Denied` first would clobber `WelcomeTimeoutJob`'s more accurate `Timeout`.
Deleting the message is the whole fix; the state machine is already correct.

**Boss wiring:** `BanUserAsync` calls with `chat: null`, `KickUserFromChatAsync` with
`chat: intent.Chat`, both in `SafeExecuteAsync`.

### Duplication removal

Three call sites now delete a message the boss guarantees. Remove the redundant delete from each:

- `ExamFlowService.ExecuteExamDenialAsync` (`:884`) — kicks/bans, so the boss covers it. The
  `UpdateResponseAsync(… Denied …)` call **stays**.
- `WelcomeService.HandleDenyAsync` step 3 (`WelcomeService.cs:1136`) — kicks, so the boss covers it.
- `WelcomeTimeoutJob` "Delete welcome message" block after the kick
  (`WelcomeTimeoutJob.cs:134-152`) — the boss covers it.

**Not removed** (no ban/kick involved, so the boss never runs):
`ExamFlowService.ExecuteExamApprovalAsync`, `WelcomeService.HandleAcceptAsync`,
`WelcomeService.HandleDmAcceptAsync`, and the `WelcomeTimeoutJob` *already-handled* branch
(`:72-90`), which deletes without kicking.

---

## Bug 3 — the DM fallback posts into the group for a user who is banned

### Problem

`WelcomeService.SendRulesAsync` (`WelcomeService.cs:1325-1332`) passes `fallbackChatId: chat.Id` to
`IBotDmService.SendDmAsync`. When the user has never opened a DM with the bot, the DM 403s and
`BotDmService.SendFallbackToChatAsync` posts the full rules text **into the group**, mentioning the
user, with a 30-second auto-delete.

It is called from `HandleAcceptAsync` **step 3 — before any admission or ban check** — so it fires
regardless of the user's state. Combined with Bug 2, a stranded welcome message keeps a live Accept
button, so this can fire for a user who is already banned.

### Approach

**Remove the fallback entirely.** In `SendRulesAsync`, drop the `fallbackChatId` and
`autoDeleteSeconds` arguments:

```csharp
var result = await dmDeliveryService.SendDmAsync(
    user: UserIdentity.From(user),
    message: dmMessage,
    cancellationToken: cancellationToken);
```

`BotDmService.SendFallbackToChatAsync` stays — `FileScanJob` (`:308`) still uses it legitimately, and
`DmDeliveryResult.FallbackUsed` / the `welcome_responses.dm_fallback` column stay wired. From this
path `FallbackUsed` is now always `false`, which is honest.

Add a `LogWarning` when `result.Failed` so a user who cannot receive rules is visible in Seq rather
than silent.

### Accepted trade-off

A legitimate user who has DMs closed now clicks Accept, receives no rules anywhere, and is still
admitted (the admission gate does not depend on DM delivery). Previously they at least saw the rules
in the group for 30 seconds. This is the explicit decision — the fallback is legacy from before DM
mode and leaks a user-directed message into the group. If the silent case turns out to matter, the
follow-up is to surface the rules via `AnswerCallbackAsync(showAlert: true)` rather than to restore a
group post; that is out of scope here.

---

## File inventory

**Create**
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/IReportCleanupHandler.cs`
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/ReportCleanupHandler.cs`
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/IWelcomeCleanupHandler.cs`
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Actions/WelcomeCleanupHandler.cs`
- `TelegramGroupsAdmin.Data/Migrations/<stamp>_AddContentUserIdToEnrichedReportsView.cs`
- Unit tests: `ReportCleanupHandlerTests`, `WelcomeCleanupHandlerTests`

**Modify**
- `TelegramGroupsAdmin.Data/Models/EnrichedReportView.cs` — view SQL + `ContentUserId` property
- `TelegramGroupsAdmin.Core/Repositories/Mappings/EnrichedReportMappings.cs` — `SubjectUserId`
- `TelegramGroupsAdmin.Core/Repositories/IReportsRepository.cs` + `ReportsRepository.cs` — `GetPendingForUserAsync`
- `TelegramGroupsAdmin.Telegram/Repositories/IWelcomeResponsesRepository.cs` + impl — `GetByUserAsync`
- `TelegramGroupsAdmin.Telegram/Services/Moderation/Intents/ModerationIntent.cs` — `OriginReportId`
- `TelegramGroupsAdmin.Telegram/Services/Bot/BotModerationService.cs` — two new rules, two new deps
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs` — drop cleanup from ban/kick, set `OriginReportId`
- `TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs` + `IExamFlowService.cs` — `originReportId` param, drop teaser delete on denial
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs` — pass `examId` to denial
- `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` — remove DM chat fallback, drop `HandleDenyAsync` delete
- `TelegramGroupsAdmin.BackgroundJobs/Jobs/WelcomeTimeoutJob.cs` — drop post-kick delete
- `TelegramGroupsAdmin.Telegram/Extensions/ServiceCollectionExtensions.cs` — register two workers
- `TelegramGroupsAdmin.UnitTests/Telegram/Services/Moderation/BotModerationServiceTests.cs` — two new ctor mocks
- `TelegramGroupsAdmin.UnitTests/Services/ReportActions/ProfileScanHandlerTests.cs` — sibling-cleanup expectations

## Verification

- Unit: new worker tests + updated `BotModerationServiceTests` / `ProfileScanHandlerTests`.
- Integration: the view migration must be exercised — `GetPendingForUserAsync` returning a
  `ContentReport` for a banned user proves the new `content_user_id` column and join work against real
  PostgreSQL. This is the one part unit tests cannot cover.
- Per `tga_feedback_run_integration_suite_when_adding_di_deps`: `BotModerationService` gains two
  constructor dependencies, so the integration suite must run, not just unit tests.
