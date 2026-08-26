# #526 DM-Only Ban Notice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the ban notification from posting a chat mention when the banned user has no DM channel, by giving `IUserMessagingService` a DM-only method and pointing both ban paths at it.

**Architecture:** The DM attempt currently embedded in `SendToUserAsync` is extracted into a private `TrySendDmAsync` helper. `SendToUserAsync` keeps its existing DM-then-chat-mention behaviour unchanged; a new `SendDmOnlyAsync` uses the same helper and returns `Failed` instead of falling back. `BanCommand` and `BanCallbackService` call the new method. The dead `SendToMultipleUsersAsync` is deleted in its own task.

**Tech Stack:** .NET 10.0, C# with nullable reference types, NUnit, NSubstitute 6, Telegram.Bot.

**Spec:** `docs/superpowers/specs/2026-08-25-bug-526-ban-notice-dm-only-design.md`

## Global Constraints

- Solution file is `TelegramGroupsAdmin.sln`. There is **no** `.slnx` file in this repo — `dotnet build TelegramGroupsAdmin.slnx` fails with `MSB1009: Project file does not exist`.
- Branch is `fix/526-ban-notice-dm-only`, already created off `develop`. Never commit to `master` or `develop`; the PR targets `develop`.
- Conventional commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`). Prefer new commits over amending. Use heredoc for multi-line messages.
- End every commit message with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.
- The build runs with warnings-as-errors in CI. An unused private field or constant **fails the build** — this matters in Task 4.
- NSubstitute 6 matcher lambdas are nullable-annotated: `Arg.Is<T>(x => x.Prop == y)` needs a null-forgiving `!` on the first dereference (`x!.Prop`). Never use `?.` instead — a null argument would silently compare `false` in `Returns()` configuration rather than throwing.
- Do not touch `LanguageWarningHandler.cs:105`, `WarnCommand.cs:116`, or `FileScanJob.cs:308`. Their fallback is correct and deliberate.

---

## File Structure

- Modify: `TelegramGroupsAdmin.Telegram/Services/IUserMessagingService.cs` — add `SendDmOnlyAsync`, remove `SendToMultipleUsersAsync`, update the interface summary
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs` — extract `TrySendDmAsync`, add `SendDmOnlyAsync`, delete `SendToMultipleUsersAsync`
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/BanCommand.cs:210-232` — DM-only call, collapse the delivery ternary, fix the stale class doc comment
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCallbackService.cs:141-152` — DM-only call, drop the null-forgiving
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs` — add three `SendDmOnlyAsync` tests, delete three `SendToMultipleUsersAsync` tests and the `TestUserId2` constant

Task order matters: Task 1 adds the method and its tests, Task 2 and Task 3 move the callers onto it, Task 4 removes the dead code last so the build stays green throughout.

---

## Task 1: `SendDmOnlyAsync` on `IUserMessagingService`

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/IUserMessagingService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs`
- Test: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs`

**Interfaces:**
- Consumes: `ITelegramUserRepository.GetByTelegramIdAsync(long, CancellationToken)`, `IBotDmService.SendDmAsync(UserIdentity user, TelegramMessage message, long? fallbackChatId, int? autoDeleteSeconds, CancellationToken)`, `MessageSendResult(long UserId, bool Success, MessageDeliveryMethod DeliveryMethod, string? ErrorMessage = null)`
- Produces: `IUserMessagingService.SendDmOnlyAsync(long userId, TelegramMessage message, CancellationToken cancellationToken = default)` returning `Task<MessageSendResult>` — consumed by Tasks 2 and 3

- [ ] **Step 1: Write the three failing tests**

Append these inside the `UserMessagingServiceTests` class, after the existing `SendToUserAsync_DmFails_FallsBackToEntityChatMention` test and before the closing brace. They rely on the existing `MakeUser` helper and the `SetUp` stub already in the file.

```csharp
    // ─────────────────────────────────────────────────────────────────────────
    // SendDmOnlyAsync — no chat-mention fallback (issue #526)
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task SendDmOnlyAsync_DmDisabled_SendsNothingToChat()
    {
        // Arrange: banned user never opened a DM with the bot
        var user = MakeUser(TestUserId1, firstName: "Grace", botDmEnabled: false);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        // Act
        var result = await _sut.SendDmOnlyAsync(TestUserId1, TelegramMessage.Plain("You were banned."));

        // Assert: DM never attempted, and nothing posted in any chat
        await _mockDmService
            .DidNotReceive()
            .SendDmAsync(
                user: Arg.Any<UserIdentity>(),
                message: Arg.Any<TelegramMessage>(),
                fallbackChatId: Arg.Any<long?>(),
                autoDeleteSeconds: Arg.Any<int?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        await _mockMessageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Any<TelegramMessage>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.DeliveryMethod, Is.EqualTo(MessageDeliveryMethod.Failed));
    }

    [Test]
    public async Task SendDmOnlyAsync_DmFails_DoesNotFallBackToChatMention()
    {
        // Arrange: DM enabled but the send fails (user blocked the bot since /start)
        var user = MakeUser(TestUserId1, firstName: "Heidi", botDmEnabled: true);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        _mockDmService
            .SendDmAsync(
                user: Arg.Any<UserIdentity>(),
                message: Arg.Any<TelegramMessage>(),
                fallbackChatId: Arg.Any<long?>(),
                autoDeleteSeconds: Arg.Any<int?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = false, Failed = true });

        // Act
        var result = await _sut.SendDmOnlyAsync(TestUserId1, TelegramMessage.Plain("You were banned."));

        // Assert: nothing posted in any chat
        await _mockMessageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Any<TelegramMessage>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.False);
        Assert.That(result.DeliveryMethod, Is.EqualTo(MessageDeliveryMethod.Failed));
    }

    [Test]
    public async Task SendDmOnlyAsync_DmSucceeds_ReportsPrivateDm()
    {
        // Arrange
        var user = MakeUser(TestUserId1, firstName: "Ivan", botDmEnabled: true);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        _mockDmService
            .SendDmAsync(
                user: Arg.Any<UserIdentity>(),
                message: Arg.Any<TelegramMessage>(),
                fallbackChatId: Arg.Any<long?>(),
                autoDeleteSeconds: Arg.Any<int?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new DmDeliveryResult { DmSent = true });

        // Act
        var result = await _sut.SendDmOnlyAsync(TestUserId1, TelegramMessage.Plain("You were banned."));

        // Assert
        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.DeliveryMethod, Is.EqualTo(MessageDeliveryMethod.PrivateDm));
    }
```

Why each fails today: `SendDmOnlyAsync` does not exist, so this does not compile.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserMessagingServiceTests"
```

Expected: **compile error** `CS1061: 'UserMessagingService' does not contain a definition for 'SendDmOnlyAsync'`. This is the correct failure — the feature is missing, not a typo. Do not proceed until you see exactly this.

- [ ] **Step 3: Add the interface method**

In `IUserMessagingService.cs`, replace the interface's `<summary>` block with:

```csharp
/// <summary>
/// Service for sending messages to users with DM preference handling.
/// Offers a DM-first strategy with a chat-mention fallback for users still in the chat,
/// and a DM-only strategy for users who are not (e.g. someone who was just banned).
/// </summary>
```

Add to `SendToUserAsync`'s summary the sentence `Only appropriate when the user is still in the chat and can read the mention.`, then add this member after `SendToUserAsync`:

```csharp
    /// <summary>
    /// Send a message to a user by DM only, with no chat-mention fallback.
    /// Use when the user cannot read a chat mention - a banned user is gone from the chat,
    /// so a mention would only leave noise behind.
    /// </summary>
    /// <param name="userId">Target user's Telegram ID</param>
    /// <param name="message">Pre-rendered message (text + entities) to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with <see cref="MessageDeliveryMethod.PrivateDm"/> if the DM landed, otherwise failure</returns>
    Task<MessageSendResult> SendDmOnlyAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Extract the DM attempt and implement the new method**

In `UserMessagingService.cs`, replace the whole body of `SendToUserAsync` (currently lines 41-74, from the `GetByTelegramIdAsync` call through the `SendChatMentionAsync` return) with:

```csharp
    {
        if (await TrySendDmAsync(userId, message, cancellationToken))
        {
            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
        }

        // Fallback: Send as chat mention
        return await SendChatMentionAsync(userId, chat, message, replyToMessageId, cancellationToken);
    }
```

Then add these two methods immediately after it:

```csharp
    public async Task<MessageSendResult> SendDmOnlyAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken = default)
    {
        if (await TrySendDmAsync(userId, message, cancellationToken))
        {
            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
        }

        // No fallback: the recipient can't read a chat mention, so nothing is sent
        return new MessageSendResult(
            userId,
            Success: false,
            MessageDeliveryMethod.Failed,
            ErrorMessage: "DM unavailable and no chat-mention fallback for this message");
    }

    /// <summary>
    /// Attempt a DM to the user, honouring their stored DM preference.
    /// Returns true only when the DM was actually delivered.
    /// </summary>
    private async Task<bool> TrySendDmAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken)
    {
        // Get user's DM preference (optimization: skip DM attempt if user blocked bot)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
        if (user?.BotDmEnabled is not true)
        {
            return false;
        }

        // Try DM via IBotDmService (no fallback - callers decide what happens next)
        var dmResult = await _dmService.SendDmAsync(
            user: UserIdentity.From(user),
            message: message,
            fallbackChatId: null,
            cancellationToken: cancellationToken);

        if (dmResult.DmSent)
        {
            _logger.LogInformation(
                "Sent DM to user {User}: {MessagePreview}",
                user.ToLogInfo(userId),
                message.Text.Length > 50 ? message.Text[..50] + "..." : message.Text);

            return true;
        }

        // DM failed (user blocked bot or error)
        _logger.LogDebug("DM to {User} failed", user.ToLogDebug(userId));
        return false;
    }
```

Also update the class `<summary>` on line 13, which currently reads `Service for sending messages to specific users with DM-first, mention-fallback strategy.` — change that line to `Service for sending messages to specific users.`

**Watch for one thing here.** The spec claims `if (user?.BotDmEnabled is not true)` narrows `user` to non-null afterward, which is what lets `UserIdentity.From(user)` replace the old `user != null ? UserIdentity.From(user) : UserIdentity.FromId(userId)` ternary. If Step 5's build instead reports `CS8604: Possible null reference argument for parameter 'user'`, the spec's claim is wrong. In that case restore the original ternary, and correct the "Extract the shared DM attempt" section of the spec in the same commit rather than silencing the warning with `!`.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~UserMessagingServiceTests"
```

Expected: PASS, all tests in the fixture, including the two pre-existing `SendToUserAsync` fallback tests. Those two must still pass — they prove `SendToUserAsync` behaviour was preserved by the extraction. Output must be free of warnings.

- [ ] **Step 6: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/IUserMessagingService.cs \
        TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs
git commit -F- <<'EOF'
feat(messaging): add SendDmOnlyAsync for recipients who left the chat

Extracts the DM attempt shared by both strategies into TrySendDmAsync.
SendToUserAsync behaviour is unchanged; the new method returns Failed
instead of posting a chat mention the recipient cannot read.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 2: `BanCommand` sends DM-only

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/BanCommand.cs:210-232`

**Interfaces:**
- Consumes: `IUserMessagingService.SendDmOnlyAsync` from Task 1
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Replace the notification call**

Find this block (starting at the comment on line 210):

```csharp
            // Notify user of ban via DM (preferred) or chat mention (fallback)
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var banNotification = BanNotificationMessage.Build(
                chatName, ModerationConstants.DefaultBanReason, result.ChatsAffected);

            var messageResult = await _messagingService.SendToUserAsync(
                userId: targetIdentity.Id,
                chat: message.Chat,
                message: banNotification,
                replyToMessageId: null, // Don't reply to trigger message for bans
                cancellationToken: cancellationToken);

            var deliveryMethod = messageResult.DeliveryMethod == MessageDeliveryMethod.PrivateDm
                ? "DM"
                : "chat mention";

            _logger.LogInformation(
                "{TargetUser} banned by {Executor} from {ChatsAffected} chats. " +
                "Reason: {Reason}. User notified via {DeliveryMethod}. Trust removed: {TrustRemoved}",
                targetIdentity.ToLogInfo(),
                message.From.ToLogInfo(),
                result.ChatsAffected, ModerationConstants.DefaultBanReason, deliveryMethod, result.TrustRemoved);
```

Replace it with:

```csharp
            // Notify user of ban via DM only - they are out of the chat, so a mention is just noise
            var chatName = message.Chat.Title ?? message.Chat.Username ?? "this chat";
            var banNotification = BanNotificationMessage.Build(
                chatName, ModerationConstants.DefaultBanReason, result.ChatsAffected);

            var messageResult = await _messagingService.SendDmOnlyAsync(
                userId: targetIdentity.Id,
                message: banNotification,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "{TargetUser} banned by {Executor} from {ChatsAffected} chats. " +
                "Reason: {Reason}. Ban DM delivered: {DmDelivered}. Trust removed: {TrustRemoved}",
                targetIdentity.ToLogInfo(),
                message.From.ToLogInfo(),
                result.ChatsAffected, ModerationConstants.DefaultBanReason, messageResult.Success, result.TrustRemoved);
```

- [ ] **Step 2: Fix the stale class doc comment**

The class `<summary>` near line 19 still claims the old behaviour:

```csharp
/// Notifies user via DM if available, falls back to chat mention
```

Change that line to:

```csharp
/// Notifies user via DM only - a banned user cannot read a chat mention
```

- [ ] **Step 3: Build to verify it compiles clean**

```bash
dotnet build TelegramGroupsAdmin.sln
```

Expected: `Build succeeded`, zero warnings. If `MessageDeliveryMethod` is now an unused using or the identifier no longer resolves anywhere in this file, that is expected only if no other reference remains — check with `grep -n "MessageDeliveryMethod" TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/BanCommand.cs` and remove nothing unless the grep comes back empty (the type lives in the `TelegramGroupsAdmin.Telegram.Services` namespace, which is not separately imported here, so there is no using directive to remove).

- [ ] **Step 4: Run the ban command tests**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCommand"
```

Expected: PASS. If a test asserts on the old `"User notified via {DeliveryMethod}"` log message or stubs `SendToUserAsync`, update it to match the new call — the behaviour change is intended and the test is encoding the old contract.

- [ ] **Step 5: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BotCommands/Commands/BanCommand.cs
git commit -F- <<'EOF'
fix(moderation): /ban notifies by DM only, never by chat mention

A banned user cannot read a mention in the chat they were just removed
from, so the fallback only left noise behind. Restores the silence the
command's own "Silent mode" comment already claimed.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 3: `BanCallbackService` sends DM-only

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/BanCallbackService.cs:141-152`

**Interfaces:**
- Consumes: `IUserMessagingService.SendDmOnlyAsync` from Task 1
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Replace the notification call**

Find this block:

```csharp
                // Send ban notification to user (resolve from scope since IUserMessagingService is Scoped)
                var messagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();
                var chatName = callbackQuery.Message?.Chat.Title ?? "this chat";
                var banNotification = BanNotificationMessage.Build(
                    chatName, ModerationConstants.DefaultBanReason, result.ChatsAffected);

                await messagingService.SendToUserAsync(
                    userId: targetUserId,
                    chat: callbackQuery.Message!.Chat,
                    message: banNotification,
                    replyToMessageId: null,
                    cancellationToken: cancellationToken);
```

Replace it with:

```csharp
                // Send ban notification by DM only - the user is out of the chat, so a mention is just noise
                // (resolve from scope since IUserMessagingService is Scoped)
                var messagingService = scope.ServiceProvider.GetRequiredService<IUserMessagingService>();
                var chatName = callbackQuery.Message?.Chat.Title ?? "this chat";
                var banNotification = BanNotificationMessage.Build(
                    chatName, ModerationConstants.DefaultBanReason, result.ChatsAffected);

                await messagingService.SendDmOnlyAsync(
                    userId: targetUserId,
                    message: banNotification,
                    cancellationToken: cancellationToken);
```

Note the `callbackQuery.Message!.Chat` null-forgiving is gone — it existed only to satisfy the `chat` parameter. `chatName` keeps its `?.` and its `?? "this chat"` default, so a null `Message` is still handled.

- [ ] **Step 2: Build to verify it compiles clean**

```bash
dotnet build TelegramGroupsAdmin.sln
```

Expected: `Build succeeded`, zero warnings.

- [ ] **Step 3: Run the ban callback tests**

```bash
dotnet test TelegramGroupsAdmin.UnitTests --filter "FullyQualifiedName~BanCallback"
```

Expected: PASS. As in Task 2, a test stubbing `SendToUserAsync` for this path is encoding the old contract and should be updated to `SendDmOnlyAsync`.

- [ ] **Step 4: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/BanCallbackService.cs
git commit -F- <<'EOF'
fix(moderation): ban selection button notifies by DM only

Same fallback removal as /ban, for the fuzzy-search selection path.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 4: Remove the dead `SendToMultipleUsersAsync`

Done last so the build stays green through Tasks 1-3. This task is behaviour-neutral: if it changes any observable behaviour, something is wrong and the removal is not safe.

**Files:**
- Modify: `TelegramGroupsAdmin.Telegram/Services/IUserMessagingService.cs`
- Modify: `TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs`
- Modify: `TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: nothing

- [ ] **Step 1: Re-confirm it is still orphaned**

```bash
grep -rn "SendToMultipleUsersAsync" --include=*.cs . | grep -v /obj/ | grep -v /bin/ | grep -v "/.claude/worktrees/"
```

Expected: hits only in `IUserMessagingService.cs`, `UserMessagingService.cs`, and `UserMessagingServiceTests.cs`. If a production caller appears, **stop** — the premise has changed and the removal needs re-deciding, not forcing through.

- [ ] **Step 2: Delete the three tests**

In `UserMessagingServiceTests.cs`, delete the section header comment `// SendToMultipleUsersAsync — batched chat-mention path` (with its two `─────` rule lines) and all three tests beneath it:
- `SendToMultipleUsersAsync_AllUsersDmDisabled_SendsEntityOverloadWithOneTextMentionPerUser`
- `SendToMultipleUsersAsync_SingleUserDmDisabled_SendsEntityOverloadWithOneTextMention`
- `SendToMultipleUsersAsync_DmFails_FallenBackUserGetsTextMentionEntity`

Stop at the `// SendToUserAsync — single-user chat-mention path via SendChatMentionAsync` header, which stays.

- [ ] **Step 3: Delete the now-unused test constant**

Those three tests were the only users of `TestUserId2`. Delete this line:

```csharp
    private const long TestUserId2 = 444_555_666L;
```

Leaving it fails the build under warnings-as-errors (`CS0414`, unused private field). Keep `TestUserId1` and `TestChatId` — both are still used by the remaining tests, as is the `MakeChat` helper.

- [ ] **Step 4: Delete the interface member**

In `IUserMessagingService.cs`, remove the `SendToMultipleUsersAsync` declaration together with its `<summary>` block (`Send a notification to multiple users (e.g., all admins in a chat). / Each user gets DM if available, otherwise fallback to single chat mention.`).

- [ ] **Step 5: Delete the implementation**

In `UserMessagingService.cs`, remove the entire `SendToMultipleUsersAsync` method — roughly 100 lines, from its signature through the `return results;` and closing brace, ending just before the `/// Send a message in the chat with user mention (fallback when DM unavailable)` comment that belongs to `SendChatMentionAsync`.

- [ ] **Step 6: Build and run the full unit suite**

```bash
dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests
```

Expected: `Build succeeded` with zero warnings, and the whole unit suite green — not just `UserMessagingServiceTests`. A red test anywhere else means something did call this after all.

- [ ] **Step 7: Commit**

```bash
git add TelegramGroupsAdmin.Telegram/Services/IUserMessagingService.cs \
        TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs \
        TelegramGroupsAdmin.UnitTests/Telegram/Services/UserMessagingServiceTests.cs
git commit -F- <<'EOF'
refactor(messaging): drop the orphaned SendToMultipleUsersAsync

Its last caller went away in fe876d6c when the DM review workflow was
reworked; admin notification is now handled by AdminMentionHandler and
INotificationService. Removes the three tests that were its only
remaining references.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

## Task 5: Full verification and PR

**Files:** none modified

- [ ] **Step 1: Build and test the whole solution**

```bash
dotnet build TelegramGroupsAdmin.sln && dotnet test TelegramGroupsAdmin.UnitTests
```

Expected: `Build succeeded`, zero warnings, all unit tests pass. Paste the actual summary line into the PR body rather than asserting success from memory.

- [ ] **Step 2: Confirm the warning paths were left alone**

```bash
git diff develop...HEAD --stat
```

Expected: exactly seven files — the spec, the plan, `IUserMessagingService.cs`, `UserMessagingService.cs`, `BanCommand.cs`, `BanCallbackService.cs`, `UserMessagingServiceTests.cs`. `LanguageWarningHandler.cs`, `WarnCommand.cs`, and `FileScanJob.cs` must **not** appear. If they do, the change overreached — revert those hunks.

- [ ] **Step 3: Confirm the fallback still exists for warnings**

```bash
grep -n "SendChatMentionAsync" TelegramGroupsAdmin.Telegram/Services/UserMessagingService.cs
```

Expected: two hits — the call inside `SendToUserAsync` and the method definition. If zero, the fallback was deleted and the warning paths are silently broken.

- [ ] **Step 4: Open the PR against develop**

```bash
git push -u origin fix/526-ban-notice-dm-only
gh pr create --base develop --title "fix(moderation): ban notice sends by DM only" --body "$(cat <<'EOF'
Closes #526

Ban notifications no longer fall back to a chat mention. A banned user
cannot read a mention in the chat they were just removed from, so the
fallback delivered nothing and left noise behind.

## Changes

- `IUserMessagingService.SendDmOnlyAsync` — DM attempt with no fallback,
  sharing the extracted `TrySendDmAsync` helper with `SendToUserAsync`
- `BanCommand` and `BanCallbackService` moved onto it
- Removed the orphaned `SendToMultipleUsersAsync` (last caller deleted in
  `fe876d6c`) and its three tests

## Not changed

The warning paths (`LanguageWarningHandler`, `WarnCommand`) and
`FileScanJob` keep their chat-mention fallback — those recipients are
still in the chat and can read it.

## Trade-off

A ban DM that cannot be delivered is now a log line only, invisible to
the admin who ran `/ban`.

Spec: `docs/superpowers/specs/2026-08-25-bug-526-ban-notice-dm-only-design.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Rollback

Each task is one commit, so `git revert <sha>` undoes any single step. Reverting Task 2 and Task 3 alone restores the old ban behaviour while keeping `SendDmOnlyAsync` in place unused — acceptable as a temporary state if the ban silence turns out to be unwanted in production.
