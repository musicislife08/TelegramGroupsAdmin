# #526 — Ban notice falls back to a chat mention when the user has DMs disabled

Closes #526

## Problem

`UserMessagingService.SendToUserAsync` implements a DM-first, chat-mention-fallback strategy.
When the target's `BotDmEnabled` is false, or the DM attempt fails, it calls the private
`SendChatMentionAsync` (`UserMessagingService.cs:180`), which posts `@user: <notice>` into the
chat.

That fallback is correct for warnings and wrong for bans. Its four production callers split
cleanly on whether the recipient can still read the chat:

| Caller | User still in chat | Mention useful |
|---|---|---|
| `LanguageWarningHandler.cs:105` | yes | yes |
| `WarnCommand.cs:116` | yes | yes |
| `BanCommand.cs:215` | no, just banned | no |
| `BanCallbackService.cs:147` | no, just banned | no |

A banned user cannot read the mention, so it delivers nothing and leaves noise in the chat.

It also contradicts the ban command's own stated design. `BanCommand.cs:234` returns
`TelegramMessage.Empty` under the comment `// Silent mode: No chat feedback, command message
simply disappears`. The mention fallback is currently the only thing that makes a manual ban
visible in chat, so removing it restores the silence the command already claims to implement.

### Scope confirmation

Verified against the tree rather than assumed:

- `BanNotificationMessage.Build` has exactly two callers — `BanCommand.cs:212` and
  `BanCallbackService.cs:144`. No auto-ban path builds one, so spam auto-bans already notify
  nobody and are unaffected.
- Commit `3a0557ce` (PR #528) fixed only `WelcomeService.SendRulesAsync`; its message records
  the deliberate decision to leave `BotDmService`'s chat fallback in place "for FileScanJob,
  which uses it legitimately." The ban paths were never in that PR's scope, so this is
  untouched work rather than a regression.
- `FileScanJob.cs:308` keeps its fallback. The malware notice goes to a user whose file was
  removed, not to someone who was kicked out — they are still present to read it.

## Approach: a separate DM-only method

Add `IUserMessagingService.SendDmOnlyAsync(userId, message, ct)` and point the two ban paths at
it. Extract the existing DM attempt — the `BotDmEnabled` lookup plus `SendDmAsync` plus its
success logging — into a private helper shared with `SendToUserAsync`, whose behaviour is
unchanged.

A separate method rather than a `bool useFallback` parameter on `SendToUserAsync`. The two
strategies differ in their required arguments, not just their behaviour: DM-only needs neither
`chat` nor `replyToMessageId`, and a flag would leave both as live parameters that callers must
pass and readers must ignore. A distinct signature makes the DM-only path unable to express a
chat target at all, which is the property we want to enforce.

Rejected: deleting `SendChatMentionAsync` outright. The two warning paths depend on it and
their users are still in the chat, so removing it would silently drop warnings for anyone who
never opened a DM with the bot.

## Implementation

### 1. `IUserMessagingService` — add the DM-only contract

```csharp
/// <summary>
/// Send a message to a user by DM only, with no chat-mention fallback.
/// Use when the user cannot read a chat mention - a banned user is gone from the chat,
/// so a mention would only leave noise behind.
/// </summary>
Task<MessageSendResult> SendDmOnlyAsync(
    long userId,
    TelegramMessage message,
    CancellationToken cancellationToken = default);
```

The interface doc comment is updated: the service no longer offers one blanket strategy, it
offers DM-first-with-fallback for users still in the chat and DM-only for users who are not.

### 2. `UserMessagingService` — extract the shared DM attempt

Add a private `TrySendDmAsync(userId, message, ct)` returning `bool`, holding the current
`GetByTelegramIdAsync` → `BotDmEnabled` check → `SendDmAsync(fallbackChatId: null)` →
success-log sequence lifted verbatim from `SendToUserAsync`.

Use `if (user?.BotDmEnabled is not true) return false;` for the guard. The `is not true`
pattern narrows `user` to non-null for the remainder of the method, which lets the
`UserIdentity.From(user)` call drop the existing
`user != null ? UserIdentity.From(user) : UserIdentity.FromId(userId)` ternary — that branch was
already unreachable, since a null user cannot have `BotDmEnabled` true.

`SendToUserAsync` becomes the helper plus its existing fallback call; behaviour is byte-for-byte
the same. `SendDmOnlyAsync` returns `PrivateDm` on success and `Failed` with an explanatory
`ErrorMessage` otherwise, sending nothing else.

### 3. `BanCommand.cs:215` — DM-only

Swap `SendToUserAsync` for `SendDmOnlyAsync`, dropping the `chat` and `replyToMessageId`
arguments. The `deliveryMethod` ternary at line 222 collapses since `ChatMention` is now
unreachable here; the log line reports delivery as a bool instead:

```
"... Reason: {Reason}. Ban DM delivered: {DmDelivered}. Trust removed: {TrustRemoved}"
```

### 4. `BanCallbackService.cs:147` — DM-only

Same swap. The `callbackQuery.Message!.Chat` null-forgiving becomes unnecessary and is removed.
`chatName` continues to read `callbackQuery.Message?.Chat.Title ?? "this chat"`.

### 5. Remove the dead `SendToMultipleUsersAsync`

Roughly 100 lines of batching logic with no production callers, in the same file this change
touches. Confirmed orphaned rather than forward-looking scaffolding: it had a real caller in
`SpamActionService` (`39f981e0`), which moved to `SendChatNotificationJob` (`8c30a637`), and
`fe876d6c` deleted the last one when that review workflow was reworked. Nothing has called it
since.

The admin-notification job it once served is now handled by two purpose-built paths that do not
use it — `AdminMentionHandler.NotifyAdminsAsync` for in-chat `@admin` mentions, and
`NotificationHandler.NotifyAdminsBanAsync` → `INotificationService` for typed notifications.
Note the two are not equivalent: `SendToMultipleUsersAsync` tried a DM per admin and batched
only the failures into one mention, whereas `AdminMentionHandler` always posts a single in-chat
mention. Restoring DM-first admin notification would be a deliberate feature, not a revival of
this method.

Removed with its three tests at `UserMessagingServiceTests.cs:97`, `:145`, and `:170`, and the
`TestUserId2` constant they were the only users of — left behind it would break the build under
`-warnaserror`.

## Accepted trade-off

When the ban DM cannot be delivered, nothing becomes visible to the admin who ran `/ban`; the
outcome is a log line only. That is the point of the change, but it means an admin cannot tell
from the chat whether the banned user was actually notified without checking Seq. Accepted
deliberately.

## Testing

Unit, `UserMessagingServiceTests`:

- `SendDmOnlyAsync` with `BotDmEnabled = false` attempts no DM and calls `IBotMessageService`
  not at all.
- `SendDmOnlyAsync` where the DM attempt fails still calls `IBotMessageService` not at all, and
  reports `Failed`.
- `SendDmOnlyAsync` where the DM lands reports `PrivateDm`.

The two existing `SendToUserAsync` fallback tests stay unchanged. They are the regression guard
proving the warning paths keep their chat mention.
