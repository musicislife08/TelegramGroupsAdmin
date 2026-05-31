# Telegram Entity-Based Message Rendering Standardization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standardize every user-facing bot send (chat + DM) on one entity-based rendering model, eliminating the four competing parse-mode conventions and the #468 Markdown-parse bug class.

**Architecture:** A new `TelegramMessageBuilder` in `Core.Utilities` produces a `TelegramMessage(text, entities)`. Chat sends gain entity overloads mirroring the existing DM entity methods. `CommandResult` carries a `TelegramMessage`. All call sites compose via the builder; mentions become uniform `text_mention` entities carrying the real `User`. Dead parse-mode/escaping helpers are deleted.

**Tech Stack:** .NET 10, C#, Telegram.Bot 22.9.x, NUnit, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-05-30-telegram-entity-rendering-standardization-design.md`

**Branch:** `feat/entity-based-message-rendering` (already created).

---

## Conventions for every task

- Build before committing: `dotnet build TelegramGroupsAdmin.slnx` (expected: `Build succeeded`).
- Run a single test: `dotnet test --filter "FullyQualifiedName~<TestClass>.<TestName>"`.
- Tests run in Debug locally. Commit messages use conventional prefixes and end with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- Never commit to `master`/`develop`; all work is on `feat/entity-based-message-rendering`.

---

## File Structure

**Created:**
- `TelegramGroupsAdmin.Core/Utilities/TelegramMessage.cs` — the `(Text, Entities)` record + `Plain` factory (moved from main app).
- `TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs` — fluent builder, UTF-16 offset tracking.
- `TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramMessageBuilderTests.cs` — builder tests.

**Modified (high-traffic):**
- `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs` — use builder for Telegram; private `EncodeHtml` for email.
- `TelegramGroupsAdmin.Telegram/Services/Bot/IBotMessageService.cs` + `BotMessageService.cs` — entity overloads.
- `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs` — `CommandResult` reshape.
- All 14 command classes; `MessageProcessingService.cs`; `UserMessagingService.cs`; `BanCelebrationService.cs`; `AdminMentionHandler.cs`; `WelcomeService.cs`; `ExamFlowService.cs`; `NotificationHandler.cs`; `TelegramDmChannel.cs`.

**Deleted (final sweep):**
- `TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs` + its test.
- `TelegramGroupsAdmin.Core/Utilities/TelegramTextUtilities.EscapeMarkdownV2` + its test.
- `TelegramDisplayName.FormatMention` overloads.
- `ParseMode?` params/overloads that become unused.

---

# Phase 1 — Foundation

## Task 1: Move `TelegramMessage` to Core and add `Plain`

**Files:**
- Create: `TelegramGroupsAdmin.Core/Utilities/TelegramMessage.cs`
- Delete: `TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs`
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs` (namespace import), `TelegramGroupsAdmin/Services/NotificationService.cs` (namespace import), and any other referencer of the old type.

- [ ] **Step 1: Create the Core type**

```csharp
// TelegramGroupsAdmin.Core/Utilities/TelegramMessage.cs
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Rendered Telegram message — plain text plus explicit entities.
/// Sent with no parse_mode; Telegram renders exactly what the entities specify.
/// </summary>
public sealed record TelegramMessage(
    string Text,
    IReadOnlyList<MessageEntity> Entities)
{
    /// <summary>A message with no formatting and no entities.</summary>
    public static TelegramMessage Plain(string text) =>
        new(text, []);
}
```

- [ ] **Step 2: Delete the old type**

```bash
git rm TelegramGroupsAdmin/Services/Notifications/TelegramMessage.cs
```

- [ ] **Step 3: Fix references**

Find every referencer and update the `using`:

```bash
grep -rln "TelegramMessage" --include=*.cs TelegramGroupsAdmin/ | grep -v /obj/
```

In `NotificationRenderer.cs` and `NotificationService.cs`, the type is used unqualified inside `namespace TelegramGroupsAdmin.Services.Notifications`. Add `using TelegramGroupsAdmin.Core.Utilities;` (already present in `NotificationRenderer.cs`) and remove the now-deleted local type. No code body changes.

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin.slnx`
Expected: `Build succeeded` (the `internal`→`public` widening and namespace move resolve cleanly).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -F- <<'EOF'
refactor(core): move TelegramMessage record to Core.Utilities with Plain factory

Shared by the notification renderer, command layer, and bot services for
entity-based rendering. Adds Plain(text) for the no-formatting case.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

## Task 2: `TelegramMessageBuilder` — failing tests first

**Files:**
- Test: `TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramMessageBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.UnitTests.Core.Utilities;

[TestFixture]
public class TelegramMessageBuilderTests
{
    [Test]
    public void Text_only_produces_no_entities()
    {
        var msg = new TelegramMessageBuilder().Text("hello world").Build();

        Assert.That(msg.Text, Is.EqualTo("hello world"));
        Assert.That(msg.Entities, Is.Empty);
    }

    [Test]
    public void Bold_records_offset_and_length_over_appended_text()
    {
        var msg = new TelegramMessageBuilder()
            .Text("a ")
            .Bold("banned")
            .Build();

        Assert.That(msg.Text, Is.EqualTo("a banned"));
        Assert.That(msg.Entities, Has.Count.EqualTo(1));
        var e = msg.Entities[0];
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.Bold));
        Assert.That(e.Offset, Is.EqualTo(2));
        Assert.That(e.Length, Is.EqualTo(6));
    }

    [Test]
    public void Mention_emits_text_mention_with_embedded_user_and_display_name()
    {
        var user = new UserIdentity(12345, "Sofi", "R", "rodriguez_sofi");
        var msg = new TelegramMessageBuilder()
            .Text("Reported user: ")
            .Mention(user)
            .Build();

        Assert.That(msg.Text, Is.EqualTo("Reported user: Sofi R"));
        Assert.That(msg.Entities, Has.Count.EqualTo(1));
        var e = msg.Entities[0];
        Assert.That(e.Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(e.Offset, Is.EqualTo("Reported user: ".Length));
        Assert.That(e.Length, Is.EqualTo("Sofi R".Length));
        Assert.That(e.User!.Id, Is.EqualTo(12345));
    }

    [Test]
    public void Mention_without_username_still_clickable_via_display_name()
    {
        var user = new UserIdentity(999, "NoUser", null, null);
        var msg = new TelegramMessageBuilder().Mention(user).Build();

        Assert.That(msg.Text, Is.EqualTo("NoUser"));
        Assert.That(msg.Entities[0].Type, Is.EqualTo(MessageEntityType.TextMention));
        Assert.That(msg.Entities[0].User!.Id, Is.EqualTo(999));
    }

    [Test]
    public void Offsets_count_utf16_code_units_not_runes()
    {
        // "👍" is a non-BMP char: 2 UTF-16 code units. Telegram offsets are UTF-16.
        var msg = new TelegramMessageBuilder()
            .Text("👍 ")
            .Bold("x")
            .Build();

        Assert.That(msg.Text, Is.EqualTo("👍 x"));
        // "👍" = 2 units, " " = 1 unit, so bold starts at offset 3.
        Assert.That(msg.Entities[0].Offset, Is.EqualTo(3));
        Assert.That(msg.Entities[0].Length, Is.EqualTo(1));
    }

    [Test]
    public void LineBreak_appends_newline()
    {
        var msg = new TelegramMessageBuilder().Text("a").LineBreak().Text("b").Build();
        Assert.That(msg.Text, Is.EqualTo("a\nb"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TelegramMessageBuilderTests"`
Expected: FAIL — `TelegramMessageBuilder` does not exist (compile error).

## Task 3: `TelegramMessageBuilder` — implementation

**Files:**
- Create: `TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs`

- [ ] **Step 1: Implement the builder**

```csharp
// TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs
using System.Text;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Builds a <see cref="TelegramMessage"/> (text + entities) with UTF-16 offset tracking.
/// Entities are Telegram's parse-mode-free formatting model: each records a type over an
/// offset/length range of the text. Offsets are UTF-16 code units (StringBuilder.Length),
/// which matches Telegram's offset rule, so non-BMP characters (emoji) count as length 2.
/// </summary>
public sealed class TelegramMessageBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly List<MessageEntity> _entities = [];

    public TelegramMessageBuilder Text(string text)
    {
        _sb.Append(text);
        return this;
    }

    public TelegramMessageBuilder LineBreak()
    {
        _sb.Append('\n');
        return this;
    }

    public TelegramMessageBuilder Bold(string text) => Styled(text, MessageEntityType.Bold);
    public TelegramMessageBuilder Italic(string text) => Styled(text, MessageEntityType.Italic);
    public TelegramMessageBuilder Code(string text) => Styled(text, MessageEntityType.Code);
    public TelegramMessageBuilder Pre(string text) => Styled(text, MessageEntityType.Pre);

    public TelegramMessageBuilder Link(string text, string url)
    {
        var offset = _sb.Length;
        _sb.Append(text);
        _entities.Add(new MessageEntity
        {
            Type = MessageEntityType.TextLink,
            Offset = offset,
            Length = text.Length,
            Url = url
        });
        return this;
    }

    /// <summary>
    /// Append a clickable mention of <paramref name="user"/>. Always emits a TextMention
    /// entity carrying the real User id, so it is clickable even for users without a username.
    /// Display text is the user's name (no @).
    /// </summary>
    public TelegramMessageBuilder Mention(UserIdentity user)
    {
        var displayText = TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, user.Id);
        var offset = _sb.Length;
        _sb.Append(displayText);
        _entities.Add(new MessageEntity
        {
            Type = MessageEntityType.TextMention,
            Offset = offset,
            Length = displayText.Length,
            User = new User
            {
                Id = user.Id,
                IsBot = false,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName,
                Username = user.Username
            }
        });
        return this;
    }

    public TelegramMessage Build() => new(_sb.ToString(), _entities);

    private TelegramMessageBuilder Styled(string text, MessageEntityType type)
    {
        var offset = _sb.Length;
        _sb.Append(text);
        _entities.Add(new MessageEntity { Type = type, Offset = offset, Length = text.Length });
        return this;
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TelegramMessageBuilderTests"`
Expected: PASS (6 tests).

- [ ] **Step 3: Commit**

```bash
git add TelegramGroupsAdmin.Core/Utilities/TelegramMessageBuilder.cs TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramMessageBuilderTests.cs
git commit -F- <<'EOF'
feat(core): add TelegramMessageBuilder for entity-based message composition

Fluent builder producing TelegramMessage(text, entities) with UTF-16 offset
tracking. Mention() emits text_mention entities with the embedded User so
mentions are clickable with or without a username.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

## Task 4: Refactor `NotificationRenderer` onto the builder + private `EncodeHtml`

**Files:**
- Modify: `TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs` (existing — must stay green)

- [ ] **Step 1: Reimplement `ToTelegramMessage` using the builder**

Replace the private `AppendBold`/`AppendUserMention`/`RenderBlocksTelegram` trio so the Telegram path delegates to `TelegramMessageBuilder`. Keep block-walking structure; swap the manual offset code:

```csharp
public static TelegramMessage ToTelegramMessage(NotificationPayload payload)
{
    var builder = new TelegramMessageBuilder();
    builder.Bold(payload.Subject).LineBreak().LineBreak();
    RenderBlocksTelegram(builder, payload.Blocks);
    var msg = builder.Build();
    return new TelegramMessage(msg.Text.TrimEnd(), msg.Entities);
}

private static void RenderBlocksTelegram(TelegramMessageBuilder builder, IReadOnlyList<ContentBlock> blocks)
{
    foreach (var block in blocks)
    {
        switch (block)
        {
            case TextBlock text:
                builder.Text(text.Text).LineBreak();
                break;
            case FieldList fieldList:
                foreach (var field in fieldList.Fields)
                {
                    builder.Bold($"{field.Label}:").Text(" ");
                    if (field.User is { } u)
                        builder.Mention(u);
                    else
                        builder.Text(field.Value);
                    builder.LineBreak();
                }
                break;
            case SectionBlock section:
                builder.LineBreak().Bold(section.Header).LineBreak();
                RenderBlocksTelegram(builder, section.Content);
                break;
        }
    }
}
```

Note: `field.User` is a `UserIdentity`, matching `Mention(UserIdentity)`. Previously `AppendUserMention` used `field.Value` as display text; the builder now derives display text from the user. If `NotificationRendererTests` asserts the old `field.Value` display text for mentions, update those assertions to the `TelegramDisplayName.Format(user)` output — the embedded `User.Id` and entity type/offsets are the behavior that matters.

- [ ] **Step 2: Replace `TelegramHtmlEncoder.Encode` calls with a private helper**

Add at the bottom of the class and replace the four `TelegramHtmlEncoder.Encode(...)` calls in `ToEmailHtml`/`RenderBlocksEmail`:

```csharp
private static string EncodeHtml(string? value) =>
    string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
```

Replace `using TelegramGroupsAdmin.Core.Utilities;`-provided `TelegramHtmlEncoder.Encode(x)` with `EncodeHtml(x)` at lines for subject, text block, field label/value, and section header. (Do NOT delete `TelegramHtmlEncoder.cs` yet — other callers still exist; that happens in Phase 6.)

- [ ] **Step 3: Run renderer tests**

Run: `dotnet test --filter "FullyQualifiedName~NotificationRendererTests"`
Expected: PASS (after the display-text assertion update from Step 1, if any).

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin/Services/Notifications/NotificationRenderer.cs TelegramGroupsAdmin.UnitTests/Services/Notifications/NotificationRendererTests.cs
git commit -F- <<'EOF'
refactor(notifications): render Telegram messages via TelegramMessageBuilder

Replaces the renderer's hand-rolled offset bookkeeping with the shared
builder, proving the extraction. Email HTML encoding moves to a private
EncodeHtml helper (TelegramHtmlEncoder deletion deferred to the dead-code sweep).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

# Phase 2 — Chat-send entity overloads

## Task 5: Add entity overloads to `IBotMessageService` + impl

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/IBotMessageService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/BotMessageService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/BotMessageServiceTests.cs` (create if absent)

- [ ] **Step 1: Write a failing test that the entity overload forwards entities to the handler**

```csharp
using NSubstitute;
using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

[TestFixture]
public class BotMessageServiceEntityTests
{
    [Test]
    public async Task SendAndSaveMessageAsync_with_TelegramMessage_forwards_entities_and_no_parse_mode()
    {
        var handler = Substitute.For<IBotMessageHandler>();
        handler.SendAsync(default, default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new Message { Id = 1, Chat = new Chat { Id = 42 } });
        // ... construct BotMessageService with substituted deps (userService.GetMeAsync stubbed,
        //     userRepo/messageRepo as no-op substitutes, ApiMetrics real or substituted, logger NullLogger).

        var msg = new TelegramMessageBuilder().Bold("hi").Build();
        await service.SendAndSaveMessageAsync(42, msg);

        await handler.Received(1).SendAsync(
            42, "hi",
            parseMode: null,
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            entities: Arg.Is<IReadOnlyList<MessageEntity>>(e => e.Count == 1 && e[0].Type == MessageEntityType.Bold),
            ct: Arg.Any<CancellationToken>());
    }
}
```

(Mirror the existing `BotMessageService` test setup if one exists; otherwise stub the six constructor deps. Use `NullLogger<BotMessageService>.Instance`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BotMessageServiceEntityTests"`
Expected: FAIL — overload does not exist.

- [ ] **Step 3: Add the interface overloads**

In `IBotMessageService.cs`, add alongside the existing methods:

```csharp
/// <summary>Send an entity-based message (no parse_mode) AND save to messages table.</summary>
Task<Message> SendAndSaveMessageAsync(
    long chatId,
    TelegramMessage message,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    CancellationToken cancellationToken = default);

/// <summary>Edit a message with entity-based content (no parse_mode) AND save edit history.</summary>
Task<Message> EditAndUpdateMessageAsync(
    long chatId,
    int messageId,
    TelegramMessage message,
    InlineKeyboardMarkup? replyMarkup = null,
    CancellationToken cancellationToken = default);

/// <summary>Send an animation with an entity-based caption AND save to history.</summary>
Task<Message> SendAndSaveAnimationAsync(
    long chatId,
    InputFile animation,
    TelegramMessage caption,
    CancellationToken cancellationToken = default);
```

Add `using TelegramGroupsAdmin.Core.Utilities;` to the interface file.

- [ ] **Step 4: Implement in `BotMessageService.cs`**

The text-message overload forwards entities to `messageHandler.SendAsync(..., entities: message.Entities, ...)` and saves `message.Text` to history (reuse the existing save block — extract the existing `SendAndSaveMessageAsync` body into a private `SaveSentMessageAsync(sentMessage, chatId, text, replyParameters, ct)` helper so both overloads share it). Example for the send overload:

```csharp
public async Task<Message> SendAndSaveMessageAsync(
    long chatId,
    TelegramMessage message,
    ReplyParameters? replyParameters = null,
    InlineKeyboardMarkup? replyMarkup = null,
    CancellationToken cancellationToken = default)
{
    var sentMessage = await messageHandler.SendAsync(
        chatId: chatId,
        text: message.Text,
        parseMode: null,
        replyParameters: replyParameters,
        replyMarkup: replyMarkup,
        entities: message.Entities,
        ct: cancellationToken);
    apiMetrics.RecordTelegramApiCall("send_message", success: true);
    await SaveSentMessageAsync(sentMessage, chatId, message.Text, replyParameters, cancellationToken);
    return sentMessage;
}
```

For the animation caption overload: the handler's `SendAnimationAsync` does NOT accept caption entities (Telegram limitation — see `IBotMessageHandler.SendAnimationAsync`). Therefore the entity caption for animations must degrade: pass `caption.Text` with `parseMode: null`. Document this inline:

```csharp
// Telegram's sendAnimation has no caption_entities in this client surface; send plain caption text.
return await SendAndSaveAnimationAsync(chatId, animation, caption.Text, parseMode: null, cancellationToken);
```

`EditAndUpdateMessageAsync` entity overload: the handler's `EditTextAsync` has no `entities` parameter. Add an `entities` parameter to `IBotMessageHandler.EditTextAsync` / `BotMessageHandler.EditTextAsync` / `ITelegramApiClient.EditMessageTextAsync` / `TelegramApiClient.EditMessageTextAsync` (forward to `EditMessageText(..., entities: ...)`), mirroring how `SendAsync` already threads `entities`. Then the service overload forwards `message.Entities`.

- [ ] **Step 5: Build + run test**

Run: `dotnet build TelegramGroupsAdmin.slnx` then `dotnet test --filter "FullyQualifiedName~BotMessageServiceEntityTests"`
Expected: `Build succeeded`; test PASS.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/Bot/ TelegramGroupsAdmin.UnitTests/Telegram/Services/Bot/
git commit -F- <<'EOF'
feat(bot): entity-based overloads for chat sends on IBotMessageService

Adds TelegramMessage overloads for SendAndSaveMessageAsync / EditAndUpdate /
animation caption, mirroring the existing DM entity methods. Threads entities
through EditText on the handler/api-client surface.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

# Phase 3 — Commands + #468

## Task 6: Reshape `CommandResult` to carry `TelegramMessage`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/CommandRouter.cs:16`
- Modify: all 14 command classes; `MessageProcessingService.cs`

This is the breaking-signature task: it will not build until every construction site and both senders are migrated, so do it as one commit.

- [ ] **Step 1: Change the record**

```csharp
// CommandRouter.cs
public record CommandResult(
    TelegramMessage Message,
    bool DeleteCommandMessage,
    int? DeleteResponseAfterSeconds = null);
```

Add `using TelegramGroupsAdmin.Core.Utilities;`. Remove the `using Telegram.Bot.Types.Enums;` if `ParseMode` was its only use here.

- [ ] **Step 2: Migrate every `new CommandResult(...)` site — transform rule**

For each of the ~82 construction sites across the 14 command classes (list below), apply this rule:

- **Plain static text** (the overwhelming majority): wrap with `TelegramMessage.Plain(...)`.
  - Before: `new CommandResult("❌ Could not identify users.", DeleteCommandMessage, DeleteResponseAfterSeconds)`
  - After: `new CommandResult(TelegramMessage.Plain("❌ Could not identify users."), DeleteCommandMessage, DeleteResponseAfterSeconds)`
- **Text that interpolated a username/mention or used `ParseMode.Html`/Markdown**: build via `TelegramMessageBuilder` (see Task 7 for `ReportCommand`, and `MyStatusCommand`/`StartCommand` which currently pass `ParseMode.Html`).

Command classes to migrate (`TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/`):
`TrustCommand`, `StartCommand`, `MyStatusCommand`, `MuteCommand`, `BanCommand`, `InviteCommand`, `DeleteCommand`, `WarnCommand`, `HelpCommand`, `UnbanCommand`, `TempBanCommand`, `SpamCommand`, `LinkCommand`, `ReportCommand`.

Add `using TelegramGroupsAdmin.Core.Utilities;` to each command file that now references `TelegramMessage`.

- [ ] **Step 3: Migrate the two senders in `MessageProcessingService.cs`**

Both command-response sends (private-chat path ~line 86–90, group path ~line 367–372) change from:

```csharp
await botMessageService.SendAndSaveMessageAsync(
    message.Chat.Id, commandResult.Response,
    parseMode: commandResult.ParseMode ?? ParseMode.Markdown,
    ...);
```

to the entity overload:

```csharp
await botMessageService.SendAndSaveMessageAsync(
    message.Chat.Id, commandResult.Message,
    replyParameters: ...,   // keep the existing replyParameters arg on the group path
    cancellationToken: cancellationToken);
```

Update the empty-response guard: `commandResult.Response != null && !string.IsNullOrWhiteSpace(...)` becomes `!string.IsNullOrWhiteSpace(commandResult.Message.Text)`.

- [ ] **Step 4: Build**

Run: `dotnet build TelegramGroupsAdmin.slnx`
Expected: `Build succeeded` (fix any missed construction site the compiler flags).

- [ ] **Step 5: Run command + message-processing tests**

Run: `dotnet test --filter "FullyQualifiedName~Command|FullyQualifiedName~MessageProcessing"`
Expected: PASS (update any test asserting `.Response`/`.ParseMode` to `.Message.Text`).

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/ TelegramGroupsAdmin.UnitTests/
git commit -F- <<'EOF'
refactor(commands): CommandResult carries TelegramMessage (entities), drop ParseMode

All command responses compose via the builder/Plain; central senders use the
entity overload. Command authors no longer pick a parse mode.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

## Task 7: Fix #468 in `ReportCommand` + acceptance test

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/ReportCommand.cs:56-67,103-108`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/ReportCommandTests.cs` (create/extend)

- [ ] **Step 1: Write the failing acceptance test**

```csharp
[Test]
public async Task Report_with_underscore_username_produces_entity_message_no_markdown()
{
    // Arrange a reported user whose username contains '_', reply present, no existing report.
    // ... set up substitutes so CreateReportAsync returns ReportId = 7 ...

    var result = await command.ExecuteAsync(message, [], userPermissionLevel: 0);

    // The success response must be entity-based with a text_mention for the reported user,
    // and contain NO raw markdown underscores that would 400 under Markdown parse.
    Assert.That(result.Message.Text, Does.Contain("Report #7"));
    Assert.That(result.Message.Entities, Has.Some.Matches<MessageEntity>(
        e => e.Type == MessageEntityType.TextMention));
    Assert.That(result.Message.Text, Does.Not.Contain("_Admins"));  // no markdown italic trailer
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ReportCommandTests"`
Expected: FAIL (current code returns a Markdown string with `_Admins..._`).

- [ ] **Step 3: Rewrite both responses via the builder**

Success path (lines 103-108):

```csharp
return new CommandResult(
    new TelegramMessageBuilder()
        .Text($"✅ Message reported for admin review (Report #{result.ReportId})")
        .LineBreak()
        .Text("Reported user: ")
        .Mention(UserIdentity.From(reportedUser))
        .LineBreak().LineBreak()
        .Italic("Admins will be notified shortly.")
        .Build(),
    DeleteCommandMessage,
    DeleteResponseAfterSeconds);
```

Duplicate-report branch (lines 56-67): rebuild the same way, using `.Italic("Admins will review the report shortly.")` for the trailer and `.Text(...)` for the static lines. The previous reporter name is plain text (no `User` object available there) — use `.Text(existingReporterName)`; it is now safe because there is no parser.

Add `using TelegramGroupsAdmin.Core.Models;` (for `UserIdentity`) and `using TelegramGroupsAdmin.Core.Utilities;` if not present. Confirm `UserIdentity.From(User)` exists; if the available factory differs, construct `new UserIdentity(reportedUser.Id, reportedUser.FirstName, reportedUser.LastName, reportedUser.Username)`.

- [ ] **Step 4: Run the acceptance test**

Run: `dotnet test --filter "FullyQualifiedName~ReportCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/ReportCommand.cs TelegramGroupsAdmin.UnitTests/Telegram/Services/BotCommands/Commands/ReportCommandTests.cs
git commit -F- <<'EOF'
fix(commands): /report builds entity message, killing the Markdown-parse 400

Reported username becomes a text_mention; the italic trailer is an Italic
entity. Underscore-bearing usernames can no longer break the send.

Fixes #468

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

# Phase 4 — Chat services

## Task 8: `UserMessagingService` → builder mentions

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs:112-139,189-199`
- Test: extend `UserMessagingServiceTests` if present.

- [ ] **Step 1: Write/extend a failing test** asserting the batched chat-mention send calls the entity overload with a `TextMention` per failed-DM user (mirror the Task 5 NSubstitute style on the injected `IBotMessageService`).

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Replace the two `SendAndSaveMessageAsync(..., parseMode: ParseMode.Markdown, ...)` calls.

Batched path (was building `mentions` string + `chatMessage`): build a single message:

```csharp
var builder = new TelegramMessageBuilder();
for (var i = 0; i < failedDmUsers.Count; i++)
{
    if (i > 0) builder.Text(", ");
    builder.Mention(failedDmUsers[i].User);   // store UserIdentity in the failed list instead of a pre-formatted string
}
builder.Text(":").LineBreak().LineBreak().Text(messageText);

await _messageService.SendAndSaveMessageAsync(
    chatId: chat.Id,
    message: builder.Build(),
    replyParameters: replyToMessageId.HasValue ? new ReplyParameters { MessageId = replyToMessageId.Value } : null,
    cancellationToken: cancellationToken);
```

Change `failedDmUsers` from `List<(long UserId, string Mention)>` to `List<(long UserId, UserIdentity User)>` and build `UserIdentity` where it was building the mention string (lines 112, 119). Single-mention path (line 189-199): `new TelegramMessageBuilder().Mention(userIdentity).Text(": ").Text(messageText).Build()`.

- [ ] **Step 4: Run — expect PASS. Build the solution.**

- [ ] **Step 5: Commit** (`refactor(messaging): UserMessagingService chat mentions via entity builder`).

## Task 9: `BanCelebrationService` → builder captions

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCelebrationService.cs:122-130,239-297,324-327`

- [ ] **Step 1: Test** — assert `SendGifToChatAsync` calls the entity-caption animation overload; assert the DM path no longer calls `EscapeMarkdownV2`.

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Implement.** The chat caption is `ReplacePlaceholders(...)` output (may contain a masked/real display name as plain text — no `User` object). Wrap as plain:

```csharp
var sentMessage = await SendGifToChatAsync(chat, gif, TelegramMessage.Plain(chatCaption), cancellationToken);
```

Change `SendGifToChatAsync` to accept `TelegramMessage caption` and call `messageService.SendAndSaveAnimationAsync(chat.Id, inputFile, caption, cancellationToken)` (the new overload — which sends plain caption text for animations, see Task 5). DM path (line 326-327): drop `TelegramTextUtilities.EscapeMarkdownV2(...)`; pass the placeholder-replaced text straight to the DM service (which moves to entity/plain in Phase 5). For now keep `dmCaption` as plain text without escaping.

- [ ] **Step 4: Run — PASS. Build.**

- [ ] **Step 5: Commit** (`refactor(ban-celebration): entity/plain captions, drop MarkdownV2 escaping`).

## Task 10: `AdminMentionHandler` → builder mentions

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/AdminMentionHandler.cs:67-107`

- [ ] **Step 1: Test** — assert `NotifyAdminsAsync` sends an entity message containing a `Bold` "Admin Alert" and one `TextMention` per notified admin, with no raw `<a href` text.

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Implement.** Replace the `mentionsList` string building + `notificationText` + `ParseMode.Html` send:

```csharp
var builder = new TelegramMessageBuilder().Bold("🔔 Admin Alert").LineBreak();
var notified = 0;
foreach (var admin in admins)
{
    if (admin.User.Id == message.From?.Id || admin.User.Id == botId) continue;
    if (notified > 0) builder.Text(" ");
    builder.Mention(new UserIdentity(admin.User.Id, admin.User.FirstName, admin.User.LastName, admin.User.Username));
    notified++;
}
if (notified == 0) { /* existing early-return log */ return; }
builder.Text(" you've been mentioned in this conversation.");

await _messageService.SendAndSaveMessageAsync(
    chatId: message.Chat.Id,
    message: builder.Build(),
    replyParameters: new ReplyParameters { MessageId = message.MessageId },
    cancellationToken: cancellationToken);
```

Confirm the admin model's `User` exposes `FirstName`/`LastName` (it's a Telegram `User`); adjust the `UserIdentity` construction to the actual property names. Remove the now-unused `using Telegram.Bot.Types.Enums;` if `ParseMode` was its only use.

- [ ] **Step 4: Run — PASS (fixes the unencoded-username latent bug). Build.**

- [ ] **Step 5: Commit** (`fix(admin-mention): entity text_mentions, drop hand-built unencoded HTML`).

---

# Phase 5 — DM + welcome/exam

## Task 11: `NotificationHandler` DM messages → entities

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Moderation/Handlers/NotificationHandler.cs`

- [ ] **Step 1: Read the file fully** and identify the warning / temp-ban / critical-violation message builders that currently produce HTML strings via `TelegramHtmlEncoder.Encode` and are sent through `TelegramDmChannel`/`IBotDmService`.

- [ ] **Step 2: Write a test** asserting one representative builder returns a `TelegramMessage` whose `reason` text appears as plain text (no `&lt;`/HTML entities) and any user reference is a `TextMention`.

- [ ] **Step 3: Run — FAIL.**

- [ ] **Step 4: Implement.** Convert each message builder to return `TelegramMessage` via the builder; replace `EscapeHtml(reason)` with `.Text(reason)` (no escaping needed). Where it sends, route through `IBotDmService.SendDmWithEntitiesAsync(user, type, msg.Text, msg.Entities, ct)`. Remove the local `EscapeHtml`/`TelegramHtmlEncoder.Encode` usage and the `// TODO: ... rich formatting` comment.

- [ ] **Step 5: Run — PASS. Build.**

- [ ] **Step 6: Commit** (`refactor(moderation): NotificationHandler DMs use entities, drop HTML escaping`).

## Task 12: `TelegramDmChannel` → entity DM path

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Notifications/TelegramDmChannel.cs`

- [ ] **Step 1: Read the file**; it currently calls `SendDmWithQueueAsync(..., ParseMode.Html)` with a pre-built `notification.Message` string.

- [ ] **Step 2: Decide the contract.** If `INotificationChannel`/`notification` now carries a `TelegramMessage` (preferred — the notification system already renders entities upstream in `NotificationService`), route to `SendDmWithEntitiesAsync`. If it still carries a string, this channel becomes plain-text: `SendDmWithEntitiesAsync(user, type, text, [])`. Choose the entity path if the upstream `Notification` model exposes entities; otherwise plain.

- [ ] **Step 3: Test + implement + run + commit** (`refactor(notifications): TelegramDmChannel uses entity DM send`).

## Task 13: `WelcomeService` sends → builder

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/WelcomeService.cs` (sends/edits at ~195-197, 423, 515, 616, 688-697, 825-827, 1102, 1212, 1248-1250, 1300, 1318-1324)

This is the largest single file. Apply the transform rule per site:

- [ ] **Step 1: Read each send/edit site.** For each, classify: static text → `TelegramMessage.Plain`; contains a `FormatMention(user)` interpolation → `.Mention(user)`; the HTML bypass announcement (688-697) → rebuild with `.Mention(user)` + `.Text(...)` and drop `TelegramHtmlEncoder.Encode`.

- [ ] **Step 2: For the bypass announcement, write a test** asserting it returns an entity message with a `TextMention` and the chat name as plain text (no HTML entities).

- [ ] **Step 3: Run — FAIL. Implement each site. Build after each cluster.**

- [ ] **Step 4: Migrate chat sends to `SendAndSaveMessageAsync(chatId, message)` / `EditAndUpdateMessageAsync(chatId, messageId, message)`; migrate DM sends to `SendDmWithEntitiesAsync`.** Replace `FormatMention` usages (195, 423, 1102, 1248, 1318) with `.Mention(user)` inside the builder.

- [ ] **Step 5: Run welcome tests — PASS. Build.**

- [ ] **Step 6: Commit** (`refactor(welcome): all sends compose via entity builder`).

## Task 14: `ExamFlowService` DM sends → entities

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/ExamFlowService.cs` (DM sends at ~175, 198, 574, 612, 626, 772, 819, 948; `FormatMention` at 194, 595, 622)

- [ ] **Step 1: Read each DM site.** Most are static templates (`SendDmAsync(user, "...")`) → build `TelegramMessage.Plain` and send via `SendDmWithEntitiesAsync(user, type, text, [])`. The intro/failure sites interpolate `FormatMention` → rebuild with `.Mention(user)`.

- [ ] **Step 2: Test the intro builder** returns a `TextMention` for the user.

- [ ] **Step 3: Run — FAIL. Implement. Build.**

- [ ] **Step 4: For keyboard DM sends** (`SendDmWithKeyboardAsync`), add or use an entity+keyboard DM method. `IBotDmService` already has `SendDmWithMediaAndKeyboardEntitiesAsync`; if a text+keyboard+entities (no media) variant is missing, add `SendDmWithKeyboardEntitiesAsync(user, text, entities, keyboard, ct)` to the interface/impl (thin wrapper over the handler `SendAsync` with `entities` + `replyMarkup`). Implement it next to the existing entity methods in `BotDmService`.

- [ ] **Step 5: Run exam tests — PASS. Build.**

- [ ] **Step 6: Commit** (`refactor(exam): exam DMs use entity sends`).

---

# Phase 6 — Dead-code sweep + regression guard

## Task 15: Delete now-unused parse-mode/escaping code

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/IBotMessageService.cs` + `BotMessageService.cs` (remove `ParseMode?` string overloads if unused)
- Modify: `TelegramGroupsAdmin.Telegram/Services/Bot/IBotDmService.cs` + `BotDmService.cs` (remove `MarkdownV2` parse-mode overloads if unused)
- Modify: `TelegramGroupsAdmin.Core/Utilities/TelegramDisplayName.cs` (remove `FormatMention` overloads)
- Delete: `TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs` + `TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramHtmlEncoderTests.cs`
- Delete: `TelegramGroupsAdmin.Core/Utilities/TelegramTextUtilities.EscapeMarkdownV2` + its tests in `TelegramTextUtilitiesTests.cs`

- [ ] **Step 1: Verify each is unused before deleting.** Run for each symbol:

```bash
grep -rn "FormatMention\|EscapeMarkdownV2\|TelegramHtmlEncoder" --include=*.cs | grep -v /obj/ | grep -vi test
```

Expected after Phases 1–5: zero production hits (email renderer now uses the private `EncodeHtml`). If any remain, migrate that site first.

- [ ] **Step 2: Delete the dead members/files.**

```bash
git rm TelegramGroupsAdmin.Core/Utilities/TelegramHtmlEncoder.cs \
       TelegramGroupsAdmin.UnitTests/Core/Utilities/TelegramHtmlEncoderTests.cs
```

Remove `FormatMention` (both overloads + private impl) from `TelegramDisplayName.cs`, keeping `Format`. Remove `EscapeMarkdownV2` from `TelegramTextUtilities.cs` and its tests. Remove the string+`ParseMode?` overloads on `IBotMessageService`/`BotMessageService` and the `MarkdownV2` overloads on `IBotDmService`/`BotDmService` only if Step 1 confirmed zero callers.

- [ ] **Step 3: Build.**

Run: `dotnet build TelegramGroupsAdmin.slnx`
Expected: `Build succeeded`. Compiler errors here mean a caller was missed — migrate it, don't restore the dead code.

- [ ] **Step 4: Commit** (`refactor: remove dead parse-mode/escaping helpers after entity migration`).

## Task 16: Regression guard + full suite

**Files:**
- Test: `TelegramGroupsAdmin.UnitTests/Architecture/ParseModeUsageGuardTests.cs` (create)

- [ ] **Step 1: Write a guard test** asserting no production source under `TelegramGroupsAdmin.Telegram/Services` references `ParseMode.Markdown` or `ParseMode.MarkdownV2`:

```csharp
[Test]
public void No_app_level_markdown_parse_mode_sends_remain()
{
    var root = TestPaths.RepoRoot("TelegramGroupsAdmin.Telegram/Services");
    var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/obj/"))
        .Where(f => File.ReadAllText(f).Contains("ParseMode.Markdown"))
        .ToList();
    Assert.That(offenders, Is.Empty,
        $"Markdown parse-mode sends remain:\n{string.Join("\n", offenders)}");
}
```

(Use the existing repo-root helper pattern if one exists; otherwise resolve from `AppContext.BaseDirectory` up to the solution.)

- [ ] **Step 2: Run — expect PASS** (all migrated). If it fails, migrate the offender.

- [ ] **Step 3: Run the full unit + integration suites.**

Run: `dotnet test TelegramGroupsAdmin.slnx` (integration suite ~50–60s; run in background with file output if needed).
Expected: all green.

- [ ] **Step 4: Commit** (`test: guard against app-level Markdown parse-mode sends`).

---

## Self-review checklist (completed during authoring)

- **Spec coverage:** builder (T2-3), TelegramMessage→Core (T1), NotificationRenderer refactor (T4), chat entity overloads (T5), CommandResult reshape (T6), #468 (T7), UserMessaging/BanCelebration/AdminMention (T8-10), NotificationHandler/DmChannel/Welcome/Exam (T11-14), dead-code sweep incl. TelegramHtmlEncoder→email-local + EscapeMarkdownV2 + FormatMention (T15), regression guard + surrogate-pair test (T16, T2). All spec sections mapped.
- **Out-of-scope honored:** `WebBotMessagingService`, #158, and email/plain-text channels are untouched (email keeps `EncodeHtml`).
- **Type consistency:** `TelegramMessage` (Text/Entities/Plain), `TelegramMessageBuilder` (Text/Bold/Italic/Code/Pre/Link/Mention/LineBreak/Build), `Mention(UserIdentity)`, `SendAndSaveMessageAsync(long, TelegramMessage, ...)`, `SendDmWithEntitiesAsync(user, type, text, entities, ct)` used consistently across tasks.
- **Open item flagged for the executor:** confirm `UserIdentity.From(User)` exists (Task 7) and the admin model's user property names (Task 10) at implementation time; both have explicit fallbacks in-task.
