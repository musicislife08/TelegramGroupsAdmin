# Admin Notification Fixes — Button Expiration & Clickable Mentions

**Date:** 2026-04-23
**Scope:** Two related bugs in admin-facing notifications.

## Problem Statement

### Bug A — DM action buttons expire within minutes

Admins receive action buttons (Ban, Warn, Dismiss, Allow, Kick, etc.) in DM
notifications for reports. In multi-admin deployments these buttons frequently
show `"Button expired - please use web UI"` within seconds or minutes of the
report arriving, even though the underlying report is still pending and
actionable.

**Root cause:** Each admin receiving a DM for a report gets their own
`report_callback_contexts` row (so short callback IDs stay under Telegram's
64-byte limit). When admin A clicks any action button, the action handlers
call `IReportCallbackContextRepository.DeleteByReportIdAsync(reportId)` which
wipes the callback contexts for **every admin**, not just admin A. The
remaining admins' button clicks then fail to resolve their context and the
service returns `"Button expired"` as a blanket fallback.

Call sites of the problematic eager delete:

- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ContentReportHandler.cs:284`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs:64`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs:116`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ProfileScanHandler.cs:173`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs:50`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs:87`
- `TelegramGroupsAdmin.Telegram/Services/ReportActions/ExamHandler.cs:124`
- `TelegramGroupsAdmin.Telegram/Services/ReportCallbackService.cs:86` (post-action delete of admin A's own context)

A secondary contributor: `DataCleanupJob.CleanupCallbackContextsAsync` deletes
contexts older than 7 days (`CallbackContextRetention = "7d"`) regardless of
whether the associated report is still pending. Any report that sits pending
for more than 7 days loses its buttons while still live.

### Bug B — Target user names aren't clickable in Telegram DMs

Notification DMs render the target user's display name as plain text even
though `NotificationRenderer.ToTelegramHtml` wraps it in
`<a href="tg://user?id=X">Name</a>`. Per the
[Telegram Bot API](https://core.telegram.org/bots/api#formatting-options):

> `tg://user?id=<user_id>` links will only work if the user has contacted the
> bot in private in the past or has sent a callback query via inline button
> and doesn't have Forwarded Messages privacy enabled for the bot.

Newly-banned spam users never satisfy that condition, so their names render
as unstyled, non-clickable plain text — exactly the case where admins need to
inspect profiles most often.

The docs confirm `text_mention` `MessageEntity` (with a full `User` object
embedded) is the supported way to mention users that don't meet the prior-
interaction requirement. Required fields are `id` only; additional fields
(first/last/username) improve client-side display.

`text_mention` requires the message be sent via the `entities` parameter,
which is mutually exclusive with `parse_mode` — per the Bot API:

> When provided, the parseMode is ignored.

### Bug C — Reporter name not clickable on the moderation report page

`TelegramGroupsAdmin/Components/Reports/ModerationReportCard.razor:67` renders
the reporter as plain text via `GetReporterDisplay()`. When a user submits a
report via the `/report` command, `Report.ReportedByUserId` is a real
Telegram user ID — we can wrap the name in the existing `<UserDetailLink>`
component (which opens `UserDetailDialog`) just like we already do for the
reported user on the same card.

Other report cards (ProfileScanAlertCard, ImpersonationAlertCard,
ExamReviewCard) all wrap user displays in `<UserDetailLink>` — no change
needed.

## Non-Goals

- No change to web-user actor display names (emails will continue to render
  as mailto auto-links on Telegram clients; accepted limitation).
- No change to the `NotificationBell` / `NotificationItem` web UI.
- No change to web-user-only reviewers on report cards (the `UserDetailLink`
  component is Telegram-user-ID-based; web-user linking is a separate
  concern).
- No change to email rendering (`NotificationRenderer.ToEmailHtml`) — emails
  are fine as-is.
- No change to web push plain text (`NotificationRenderer.ToPlainText`).

## Design

### Bug A — Stop eager deletion; rely on already-handled pattern

**Remove eager `DeleteByReportIdAsync` / `DeleteAsync` calls** from all four
action handlers and from `ReportCallbackService.HandleCallbackAsync`. The
existing `ReportStatusHelper.CheckAlreadyHandled` pattern already handles the
concurrency race: when admin B tries to update a report whose status has
already changed, `TryUpdateStatusAsync` fails, the `onRaceLost` callback
fetches the current state, and `CheckAlreadyHandled` returns a
`ReviewActionResult(false, "Already handled by {admin} ({action}) at
{time}")`. That message is strictly better than `"Button expired"`.

**Change the cleanup strategy**: callback contexts should live as long as
their underlying report does. Rewrite
`DataCleanupJob.CleanupCallbackContextsAsync` to delete only orphaned rows
(contexts whose `report_id` no longer exists in the `reports` table). Since:

- Pending reports are never cleaned up (`DeleteOldReportsAsync` filters
  `Status != Pending`).
- Resolved reports are cleaned after 30 days (`ReportRetention`).
- Orphan callback contexts will be cleaned on the next cleanup run after their
  report is deleted.

...the buttons stay live for the whole lifetime of the report, matching user
intent ("if the report is still active in the DB, the buttons should work").

**Drop the age-based `CallbackContextRetention` setting.** It's now
structurally unused and will confuse operators if left in place.
`DataCleanupSettings.CallbackContextRetention`, the `DefaultShortRetention`
reference in the job, and any config test assertions all get removed.

**Delete method cleanup:** `DeleteExpiredAsync(TimeSpan)` on
`IReportCallbackContextRepository` is now dead code — remove it and its
implementation. Add a new `DeleteOrphanedAsync()` method that performs the
anti-join delete.

#### Pseudocode for the new cleanup

```csharp
public async Task<int> DeleteOrphanedAsync(CancellationToken ct)
{
    await using var db = await _contextFactory.CreateDbContextAsync(ct);
    return await db.ReportCallbackContexts
        .Where(rcc => !db.Reports.Any(r => r.Id == rcc.ReportId))
        .ExecuteDeleteAsync(ct);
}
```

EF Core translates this to a `DELETE ... WHERE NOT EXISTS (...)` that Postgres
handles efficiently with the existing `ix_report_callback_contexts_report_id`
index.

#### Behavioral changes admins will see

| Scenario | Before | After |
|---|---|---|
| Admin A acts, admin B clicks later | `"Button expired - please use web UI"` | `"Already handled by Kass (ban) at 10:24 UTC"` |
| Report still pending after 7 days | `"Button expired"` | Buttons still work |
| Report deleted (e.g., aged-out resolved) | N/A | `"Button expired - please use web UI"` (correct fallback) |

### Bug B — Entity-based DM rendering

Move DM notifications from `ParseMode.Html` to Telegram's `entities`
parameter. This enables proper `text_mention` entities for spam users and
eliminates a whole class of HTML-parse-error issues.

#### Renderer output shape

Rename `NotificationRenderer.ToTelegramHtml` to
`NotificationRenderer.ToTelegramMessage` and change its return type from
`string` to a new record:

```csharp
public sealed record TelegramMessage(
    string Text,
    IReadOnlyList<MessageEntity> Entities);
```

Entity types emitted:

- `MessageEntityType.Bold` for:
  - The subject line (first line of the message)
  - Every field label (the `"User:"`, `"Chat:"`, etc. prefix in `FieldList`)
  - Every section header (the `"Analysis"`, `"Message"`, `"Action Taken"`
    blocks)
- `MessageEntityType.TextMention` with a full `User` object for `Field`
  instances where `TelegramUserId.HasValue` is true.
- No entity for system actors ("Auto-Detection", "Bot Protection", etc.) and
  web-user display strings — they stay as plain text (email auto-linking is
  out of scope, as agreed).

#### Offset tracking

Entities carry `Offset` and `Length` measured in UTF-16 code units (per
Telegram spec). The renderer threads a `currentOffset` counter as it appends
to `StringBuilder`, computing `string.GetUtf16CodeUnitLength()` helpers. For
surrogate-pair-free ASCII/BMP text (the dominant case) this is just
`string.Length`; we'll add a small utility for correctness on the edge cases
that include emoji or non-BMP characters (display names may contain these).

#### Embedded `User` object

Per the tdlib documentation, only `User.Id` is required for `text_mention`.
To improve display fallback on clients, we also populate `FirstName`,
`LastName`, `Username`, and `IsBot = false`. `UserIdentity` already carries
these fields, so we route linked mentions through a new `UserIdentity`
overload and drop the existing nullable `telegramUserId` parameter on the
string overload — a field is either plain text or a linked user mention,
never a string with an optional ID hanging off it.

Final signatures — two overloads, both skip-on-null:

```csharp
public NotificationPayloadBuilder WithField(string label, string? value);
public NotificationPayloadBuilder WithField(string label, UserIdentity? user);
```

`WithFieldIf(bool, ...)` is dropped — callers use null to signal skip. For the
rare bool-guarded case (`messageDeleted` in spam-ban notifications), a
ternary-to-null is cleaner:

```csharp
// Before
.WithField("User", user.DisplayName, telegramUserId: user.Id)
.WithField("Chat", chat.ChatName ?? chat.Id.ToString())
.WithFieldIf(detectionReason != null, "Reason", detectionReason)
.WithFieldIf(messageDeleted, "Message deleted", $"ID: {messageId}")

// After
.WithField("User", user)
.WithField("Chat", chat.ChatName ?? chat.Id.ToString())
.WithField("Reason", detectionReason)                    // skips if null
.WithField("Message deleted",
    messageDeleted ? $"ID: {messageId}" : null)          // ternary-to-null
```

The `Field` record on `NotificationPayload` loses `TelegramUserId: long?` in
favor of `User: UserIdentity?` — the renderer reads the latter when deciding
whether to emit a `TextMention` entity.

#### Send-path plumbing

Threading entities through:

1. `TelegramMessage` flows from `NotificationRenderer` out of
   `NotificationService.SendTypedTelegramDmAsync` and
   `SendTelegramDmDirectAsync`.
2. `IBotDmService.SendDmWithQueueAsync`, `SendDmWithMediaAsync`, and
   `SendDmWithMediaAndKeyboardAsync` need overloads that accept
   `IReadOnlyList<MessageEntity>? entities`. Since these methods currently
   also accept `ParseMode parseMode = MarkdownV2`, we add a new overload
   taking `TelegramMessage` (text + entities) and have the new overload bypass
   `parseMode` entirely.
3. `IBotMessageHandler.SendAsync` / `SendPhotoAsync` / `SendVideoAsync` get
   an `entities` parameter that maps to the Telegram SDK's
   `messageEntities` / `captionEntities` parameters.
4. User-facing DM code paths (warning DMs, temp-ban DMs, critical-violation
   DMs routed through `INotificationOrchestrator`) are untouched. Only admin
   notifications routed through `NotificationService` → `IBotDmService`
   switch to entity-based sending. The new overload is additive; existing
   `string + parseMode` overloads stay.

#### Tests for the renderer

Unit tests in `TelegramGroupsAdmin.UnitTests` verifying:

- Subject line emits a `Bold` entity covering the entire first line.
- Field label emits a `Bold` entity covering only the label portion
  (including the colon).
- A `FieldList` entry with a `TelegramUserId` emits a `TextMention` entity
  over just the value span, carrying a `User` object with the provided ID and
  name fields.
- A `FieldList` entry without `TelegramUserId` emits no entity (plain text).
- Section headers emit `Bold` entities.
- Offset arithmetic is correct when display names contain emoji / non-BMP
  characters (one test case with `"👤 User"`).

### Bug C — Blazor reporter link

`TelegramGroupsAdmin/Components/Reports/ModerationReportCard.razor`:

```razor
@* Before *@
<MudText Typo="Typo.caption" Color="Color.Secondary">
    <b>Reported by:</b> @GetReporterDisplay()
</MudText>

@* After *@
<MudText Typo="Typo.caption" Color="Color.Secondary">
    <b>Reported by:</b> @ReporterFragment
</MudText>
```

`ReporterFragment` is a computed `RenderFragment` that returns:

- System report (`ReportedByUserId == null && WebUserId == null`): plain
  `ReportedByUserName ?? "System"`.
- Telegram-user report (`ReportedByUserId != null`):
  `<UserDetailLink UserId="@Report.ReportedByUserId.Value">@ReportedByUserName</UserDetailLink>`
  followed by plain text `" (ID: {id})"` so the ID stays visible.
- Web-user report (`WebUserId != null`, no Telegram link possible): plain
  `"{name} (Web User)"`.

No changes to `ProfileScanAlertCard`, `ImpersonationAlertCard`, or
`ExamReviewCard` — their user displays already use `<UserDetailLink>`.

## Data Model Changes

- `IReportCallbackContextRepository` loses `DeleteExpiredAsync(TimeSpan)`,
  gains `DeleteOrphanedAsync()`.
- `DataCleanupSettings.CallbackContextRetention` removed.
  `DefaultShortRetention` constant usage reduced (still used by
  `WebNotificationRetention`).
- No migrations. No schema changes. The existing `report_callback_contexts`
  table and indexes are unchanged.

## Error Handling

- Entity sends that fail Telegram validation (e.g., overlapping entities, out-
  of-bounds offsets) will surface as `ApiRequestException` from the handler.
  The existing catch blocks in `BotDmService` already log these; the new
  offset-computation tests should catch bugs pre-prod.
- Orphaned-context cleanup runs in the existing `DataCleanupJob` scope —
  failures already log, retry next run.
- Multi-admin race — covered by the pre-existing
  `ReportStatusHelper.CheckAlreadyHandled` logic, no new handling needed.

## Testing

### Unit tests
- `NotificationRendererTests` — entity offset/length correctness, bold
  coverage, text-mention emission, no-entity for fields without
  `TelegramUserId`.
- `ContentReportHandlerTests` / `ProfileScanHandlerTests` /
  `ExamHandlerTests` — confirm no `DeleteByReportIdAsync` call after
  successful action (spy on repo mock).
- `ReportCallbackServiceTests` — confirm no `DeleteAsync` call on success.

### Integration tests
- Postgres integration test on `ReportCallbackContextRepository` verifying
  `DeleteOrphanedAsync` removes contexts with missing reports, keeps contexts
  whose reports exist.
- Scenario test: admin A acts → admin B's callback still resolves to the
  resolved report and gets the "Already handled" message (not "Button
  expired").

### Manual verification
- Trigger a real spam detection in dev; confirm DM admin-facing mention is
  clickable and opens the banned user's profile.
- Trigger a `/report` against a message; confirm the reporter is clickable
  on the moderation report card in the web UI.

## Rollout

Single PR, single commit sequence following the repo's conventional-commits
rules. Feature branch off `develop`, PR to `develop`.

No config changes required. Existing deployments that have overridden
`CallbackContextRetention` in their `background_jobs_config` JSONB will
simply see the value ignored after the migration — no corrupt state.

## Out of Scope (deferred)

- Generalizing `UserDetailLink` to accept web-user IDs (would enable
  clickable "Reviewed by" on all report cards). Separate design if desired.
- Entity-based rendering for user-facing DMs (warnings, temp bans) — keep
  existing Markdown/HTML rendering; those messages don't need text_mention
  because they're sent to the user themselves.
- Any change to email (`ToEmailHtml`) or web push (`ToPlainText`) rendering.
