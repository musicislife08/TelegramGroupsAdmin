# Telegram Entity-Based Message Rendering Standardization

**Date:** 2026-05-30
**Status:** Design — approved for planning
**Closes:** #468 (folded into the migration, not a standalone fix)
**Branch:** `feat/entity-based-message-rendering`

## Problem

The bot has **four different ways** to render and send user-facing text, and DMs alone account for three of them:

| Path | Convention | Mention reliability |
|---|---|---|
| Chat sends (`IBotMessageService`) | `parseMode` only, **no entities overload** | `@username` only; no-username = bare name, not clickable |
| DM legacy (`IBotDmService.SendDmWithQueueAsync` / media / keyboard) | default `ParseMode.MarkdownV2` | escaped text, `@username` |
| DM channel (`TelegramDmChannel`) | `ParseMode.Html` | HTML `<a href=tg://user?id>` if the caller built it |
| DM modern (`IBotDmService.SendDmWithEntitiesAsync`) | entity-based, no parse mode | `text_mention` with embedded `User` — most reliable |

This divergence makes the DM/send surface hard to trace and edit, and it is the direct cause of bug **#468**: `ReportCommand` interpolates a raw username into a `ParseMode.Markdown` template, so a username containing `_` (e.g. `rodriguez_sofi`) produces an unbalanced entity and Telegram rejects the send with API 400.

`#468` is one symptom. The codebase audit surfaced two more latent bugs of the same class:

1. **`IBotDmService` defaults to `ParseMode.MarkdownV2`, but `TelegramTextUtilities.EscapeMarkdownV2` has zero production callers.** Several DM paths ship unescaped user text today — one underscore from the same 400.
2. **`AdminMentionHandler` hand-builds `<a href="tg://user?id=…">@{username}</a>` with the username not HTML-encoded** — a latent parse/injection bug.

`TelegramDisplayName.FormatMention` — nominally the single source of truth for mentions — returns a bare `@username` / display-name string. It produces no clickable link for users without a username anywhere except the already-modernized notification path.

## Goal

Standardize **every user-facing bot send (chat + DM)** on a single rendering model: **entity-based composition** (`text` + `MessageEntity[]`, no parse mode). This:

- Reduces the four conventions to one — a single place to trace and edit how the bot composes messages.
- Makes user mentions reliably clickable for everyone, with or without a username, via `text_mention` entities carrying the real `User` id.
- **Structurally eliminates the entire #468 bug class.** With entities there is no parser and nothing to escape, so "username contains a special character" can never break a send again.

This is one milestone delivered as one PR to `develop`. Because the whole PR *is* the parse-mode work, it is standalone by construction rather than bundled with unrelated changes.

## Why entity-based over HTML-everywhere

HTML-everywhere is less plumbing but keeps escaping as a permanent manual hazard — forgetting one `Encode()` call is exactly how #468 happened — and gives slightly weaker no-username links (`tg://user?id=` resolution vs an embedded `User`). Entity-based composition removes the escaping footgun entirely and is already the proven, in-production convention for the notification subsystem (`NotificationService` → `NotificationRenderer.ToTelegramMessage` → `SendDmWithEntitiesAsync`). The cost — UTF-16 offset bookkeeping — is hidden behind a builder.

Telegram.Bot 22.9.x ships no fluent entity builder (only the `MessageEntity` DTO and `entities:` parameters). A third-party `Telegram.Bot.Extensions.MessageBuilder` exists, but per project convention (*libraries for hard/security things, custom for everything else*) and because we already have a working hand-rolled equivalent inside `NotificationRenderer`, we build a small in-house builder rather than take a dependency on a wrapper of a fast-moving API surface.

## Core components

### `TelegramMessage` → move to Core

The `(string Text, IReadOnlyList<MessageEntity> Entities)` record is currently `internal` in `TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs`. Move it to **`TelegramGroupsAdmin.Core.Utilities`** (Core already references `Telegram.Bot`, so `MessageEntity` is available) so the main-app renderer, the `.Telegram` command layer, and the bot services all share one type.

### `TelegramMessageBuilder` (new, `Core.Utilities`)

A fluent builder that produces a `TelegramMessage`, tracking UTF-16 offsets internally (the offset/append logic lifted from `NotificationRenderer.AppendBold` / `AppendUserMention`, which already compute it correctly — `StringBuilder.Length` counts UTF-16 code units, matching Telegram's offset rule).

Surface (illustrative):

```csharp
var msg = new TelegramMessageBuilder()
    .Text("✅ Message reported for admin review (Report #")
    .Text(reportId.ToString())
    .Text(")\nReported user: ")
    .Mention(reportedUser)
    .LineBreak().LineBreak()
    .Italic("Admins will be notified shortly.")
    .Build();   // -> TelegramMessage(text, entities)
```

Methods: `Text`, `Bold`, `Italic`, `Code`, `Pre`, `Link(text, url)`, `Mention(user)`, `LineBreak`, `Build()`. Add others (`Underline`, `Strikethrough`, `Blockquote`, `Spoiler`) only when a call site needs them — YAGNI.

A convenience `TelegramMessage.Plain(string text)` (or `TelegramMessageBuilder` overload) covers the common no-formatting case without ceremony.

### Mention semantics

`.Mention(user)` **always** emits a `TextMention` entity carrying the real `User` id, with display text = `TelegramDisplayName.Format(user)` (name, no `@`). Uniform and reliably clickable for everyone, username or not. This intentionally drops the copy-pasteable literal `@username`; a future web-UI `@username` popup-tagging feature is separate and out of scope.

The `User` object embedded in the entity follows the existing `AppendUserMention` shape: `Id`, `IsBot = false`, `FirstName` (non-null), `LastName`, `Username`.

### Chat-send entity overloads

`IBotMessageService` gains entity overloads mirroring the DM service:

- `SendAndSaveMessageAsync(long chatId, TelegramMessage message, ReplyParameters?, InlineKeyboardMarkup?, CancellationToken)`
- `EditAndUpdateMessageAsync(long chatId, int messageId, TelegramMessage message, InlineKeyboardMarkup?, CancellationToken)`
- `SendAndSaveAnimationAsync(long chatId, InputFile, TelegramMessage caption, CancellationToken)`

The underlying `IBotMessageHandler` / `ITelegramApiClient` already accept `entities:` / `captionEntities:`, so this is wiring, not new capability. The message-history save path stores `message.Text` (the plain text), unchanged from today.

### `CommandResult` reshape

`CommandResult` is defined in `CommandRouter.cs` as `record CommandResult(string? Response, bool DeleteCommandMessage, int? DeleteResponseAfterSeconds = null, ParseMode? ParseMode = null)`. Replace the `string? Response` + `ParseMode?` fields with a `TelegramMessage`:

```csharp
public record CommandResult(
    TelegramMessage Message,
    bool DeleteCommandMessage,
    int? DeleteResponseAfterSeconds = null);
```

The 14 command classes build their `Message` via the builder (`TelegramMessage.Plain(...)` for the many static-text commands) — ~82 `CommandResult` construction sites in total, though only two pass a non-default parse mode today (`MyStatusCommand` and `StartCommand`, both `ParseMode.Html`); the rest rely on the `Markdown` default. The two central senders in `MessageProcessingService` (the private-chat command path and the group command path) always use the entity overload; command authors no longer choose a parse mode.

## Data flow (target)

```
Command / Service
   │  builds via TelegramMessageBuilder
   ▼
TelegramMessage (text + entities)
   │
   ├─ chat  → IBotMessageService.SendAndSaveMessageAsync(chatId, message)
   └─ DM    → IBotDmService.SendDmWithEntitiesAsync(user, type, text, entities)
                 │
                 ▼
            IBotMessageHandler / ITelegramApiClient  (entities:, no parse_mode)
                 ▼
              Telegram
```

## Migration scope

Every site from the audit converts to the builder + entity overloads. Grouped by area:

- **Commands (14 command classes, ~82 construction sites):** reshape `CommandResult` construction to `TelegramMessage`. `ReportCommand` success path and duplicate-report branch become builder-composed — **this is where #468 dies**.
- **`MessageProcessingService`:** both command-response senders use the entity overload; drop the `?? ParseMode.Markdown` fallback.
- **`UserMessagingService`:** batched and single chat-mention sends build mentions via `.Mention()`; drop `ParseMode.Markdown`.
- **`BanCelebrationService`:** GIF captions (chat) and the DM caption build via the builder; remove the `EscapeMarkdownV2` DM call (now redundant).
- **`AdminMentionHandler`:** replace hand-built HTML `<a href>` mentions with `.Mention()` entities (fixes the unencoded-username bug).
- **`WelcomeService`:** ~12 sends/edits including the bypass announcement (currently HTML) move to the builder; drop `TelegramHtmlEncoder` usage here.
- **`ExamFlowService`:** intro / question / pass / fail / timeout DM sends move to entity-based DM (`SendDmWithEntitiesAsync`).
- **`NotificationHandler`:** DM moderation messages (warnings, temp bans, critical violations) move from HTML to entities; drop `TelegramHtmlEncoder` usage here.
- **`TelegramDmChannel`:** route through the entity DM path instead of `ParseMode.Html`.
- **DM service callers** currently relying on `MarkdownV2` defaults move to `SendDmWithEntitiesAsync` / the media+entities variant.
- **`NotificationRenderer.ToTelegramMessage`:** refactor to use `TelegramMessageBuilder` internally (proves the extraction; existing tests must stay green).

## Dead-code removal (same PR, no back-compat, no `[Obsolete]`)

Removed once provably unused, in the final sweep:

- `ParseMode?` parameters on `IBotMessageService` methods and `CommandResult`.
- `MarkdownV2` defaults and the parse-mode DM overloads on `IBotDmService` that no longer have callers.
- `TelegramTextUtilities.EscapeMarkdownV2` and its tests (zero production callers after migration).
- `TelegramDisplayName.FormatMention` (superseded by `.Mention()`); keep `TelegramDisplayName.Format` (still used for display text and logging).
- `TelegramHtmlEncoder` (`Core/Utilities/TelegramHtmlEncoder.cs`) and `TelegramHtmlEncoderTests` — its only remaining caller is the email renderer, so its null-guarded `WebUtility.HtmlEncode` folds into a **private** `EncodeHtml` helper inside `NotificationRenderer`:

  ```csharp
  private static string EncodeHtml(string? value) =>
      string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
  ```

  Ordering: this deletion lands only after the `WelcomeService` and `NotificationHandler` migrations remove their `Encode` calls, so no live caller breaks mid-PR.

## Out of scope

- **Web UI bot-message editor** (`WebBotMessagingService`): admin-authored raw text stays as-is. A future `@username` popup-tagging feature is tracked separately.
- **#158** (configurable rich-text in moderation reasons): likely stale (no mechanism for admin-authored custom replies); untouched here.
- **Email and plain-text render channels** (`ToEmailHtml`, `ToPlainText`): unchanged; email keeps HTML encoding via the new private helper.

## Testing

- **`TelegramMessageBuilder` unit tests:** offset correctness including a surrogate-pair/emoji case (length counted as UTF-16 code units), entity nesting, `.Mention()` with and without a username, `Plain` produces no entities.
- **`NotificationRenderer`:** refactor onto the builder and keep existing `NotificationRendererTests` green — proves the extraction preserved behavior.
- **#468 acceptance:** an underscore-bearing username through the `/report` happy path and the duplicate-report path, asserting no exception and a correct `TextMention` entity (offset/length over the display name, embedded `User.Id`).
- **Send-path tests:** at least one chat-mention path and one ban-celebration/user-messaging path assert entity output rather than a parse-mode string.
- **Regression guard:** a test (or CI grep) asserting zero app-level `ParseMode.Markdown` / `ParseMode.MarkdownV2` sends remain outside statically-controlled text.
- Run the integration suite after the `IBotMessageService` / `CommandResult` signature changes (DI-touching changes warrant it).

## Implementation sequencing (within the single PR)

Reviewable commit order, each building cleanly:

1. **Foundation:** move `TelegramMessage` to Core; add `TelegramMessageBuilder` + tests; refactor `NotificationRenderer.ToTelegramMessage` onto it (tests stay green).
2. **Bot-layer overloads:** add entity overloads to `IBotMessageService` (+ impl, handler wiring).
3. **Commands + #468:** reshape `CommandResult`; migrate all 14 command classes and the two `MessageProcessingService` senders; `ReportCommand` underscore fix + acceptance test.
4. **Chat services:** `UserMessagingService`, `BanCelebrationService`, `AdminMentionHandler`.
5. **DM + welcome/exam:** `NotificationHandler`, `TelegramDmChannel`, `WelcomeService`, `ExamFlowService` onto entity DM sends.
6. **Dead-code sweep:** delete unused parse-mode params/overloads, `EscapeMarkdownV2`, `FormatMention`, `TelegramHtmlEncoder` (fold into `NotificationRenderer`); add the regression guard.

## Risk notes

- **Blast radius:** ~50 production files. Per-file changes are mechanical and uniform (same builder pattern), which is why one PR is appropriate; the commit sequencing above keeps each step reviewable.
- **UTF-16 offsets** are the one genuine footgun; contained in the builder and covered by a surrogate-pair test.
- **Message-history text** is unchanged (we still persist plain `Text`), so audit/dedup/hashing behavior is unaffected.
