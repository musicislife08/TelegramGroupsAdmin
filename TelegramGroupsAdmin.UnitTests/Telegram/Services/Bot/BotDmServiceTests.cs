using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

/// <summary>
/// Unit tests for BotDmService entity-forwarding overloads.
/// Verifies that when a TelegramMessage is passed to SendDmAsync or SendDmWithKeyboardAsync,
/// the entities array (and null parse_mode) reach the underlying IBotMessageHandler.SendAsync call.
/// The seam is IBotMessageHandler — mocked with NSubstitute, captured via Arg.Do.
/// </summary>
[TestFixture]
public class BotDmServiceTests
{
    private IBotMessageHandler _messageHandler = null!;
    private ITelegramUserRepository _userRepository = null!;
    private IPendingNotificationsRepository _pendingNotificationsRepository = null!;
    private IManagedChatsRepository _managedChatsRepository = null!;
    private IJobScheduler _jobScheduler = null!;
    private BotDmService _service = null!;

    private static readonly UserIdentity TestUser = new(99001L, "Alice", null, "alice_tg");

    [SetUp]
    public void SetUp()
    {
        _messageHandler = Substitute.For<IBotMessageHandler>();
        _userRepository = Substitute.For<ITelegramUserRepository>();
        _pendingNotificationsRepository = Substitute.For<IPendingNotificationsRepository>();
        _managedChatsRepository = Substitute.For<IManagedChatsRepository>();
        _jobScheduler = Substitute.For<IJobScheduler>();

        // Default: SendAsync succeeds with a minimal Message
        _messageHandler
            .SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 1, Chat = new Chat { Id = TestUser.Id } });

        _service = new BotDmService(
            _messageHandler,
            _userRepository,
            _pendingNotificationsRepository,
            _managedChatsRepository,
            _jobScheduler,
            NullLogger<BotDmService>.Instance);
    }

    #region SendDmAsync — TelegramMessage overload

    [Test]
    public async Task SendDmAsync_WithTelegramMessage_ForwardsEntitiesAndNullParseMode()
    {
        // Arrange
        var message = new TelegramMessageBuilder().Bold("Hello").Text(" world").Build();
        IReadOnlyList<MessageEntity>? capturedEntities = null;
        ParseMode? capturedParseMode = ParseMode.Html; // intentionally wrong default — assert it gets null

        _messageHandler
            .SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Do<ParseMode?>(pm => capturedParseMode = pm),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Do<IReadOnlyList<MessageEntity>?>(e => capturedEntities = e),
                ct: Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 1, Chat = new Chat { Id = TestUser.Id } });

        // Act
        var result = await _service.SendDmAsync(TestUser, message);

        // Assert — DM succeeded, entities were forwarded, parse_mode was null
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True, "DM should have been sent");
            Assert.That(capturedParseMode, Is.Null, "parse_mode must be null when sending entity-based message");
            Assert.That(capturedEntities, Is.Not.Null, "Entities must be forwarded to the handler");
            Assert.That(capturedEntities!.Count, Is.EqualTo(1), "Expected exactly one entity (Bold)");
            Assert.That(capturedEntities[0].Type, Is.EqualTo(MessageEntityType.Bold),
                "The forwarded entity should be Bold");
        }
    }

    [Test]
    public async Task SendDmAsync_WithTelegramMessage_SendsToUserChatId()
    {
        // Arrange
        var message = new TelegramMessageBuilder().Bold("test").Build();

        // Act
        await _service.SendDmAsync(TestUser, message);

        // Assert — the underlying SendAsync was called with the user's id as the chat id
        await _messageHandler.Received(1).SendAsync(
            chatId: TestUser.Id,
            text: Arg.Any<string>(),
            parseMode: Arg.Any<ParseMode?>(),
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
            ct: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendDmAsync_WithTelegramMessage_EnablesBotDmOnSuccess()
    {
        // Arrange
        var message = TelegramMessage.Plain("simple");

        // Act
        await _service.SendDmAsync(TestUser, message);

        // Assert — bot_dm_enabled flag is flipped on success
        await _userRepository.Received(1).EnableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendDmWithKeyboardAsync — TelegramMessage overload

    [Test]
    public async Task SendDmWithKeyboardAsync_WithTelegramMessage_ForwardsEntitiesAndNullParseMode()
    {
        // Arrange
        var message = new TelegramMessageBuilder().Bold("Question?").Build();
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Yes", "yes") }
        });
        IReadOnlyList<MessageEntity>? capturedEntities = null;
        ParseMode? capturedParseMode = ParseMode.Html; // intentionally wrong default

        _messageHandler
            .SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Do<ParseMode?>(pm => capturedParseMode = pm),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Do<IReadOnlyList<MessageEntity>?>(e => capturedEntities = e),
                ct: Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 2, Chat = new Chat { Id = TestUser.Id } });

        // Act
        var result = await _service.SendDmWithKeyboardAsync(TestUser, message, keyboard);

        // Assert — DM succeeded, entities were forwarded, parse_mode was null
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True, "DM with keyboard should have been sent");
            Assert.That(capturedParseMode, Is.Null, "parse_mode must be null when sending entity-based message");
            Assert.That(capturedEntities, Is.Not.Null, "Entities must be forwarded to the handler");
            Assert.That(capturedEntities!.Count, Is.EqualTo(1), "Expected exactly one entity (Bold)");
            Assert.That(capturedEntities[0].Type, Is.EqualTo(MessageEntityType.Bold),
                "The forwarded entity should be Bold");
        }
    }

    [Test]
    public async Task SendDmWithKeyboardAsync_WithTelegramMessage_ForwardsKeyboardToHandler()
    {
        // Arrange
        var message = new TelegramMessageBuilder().Text("Pick one:").Build();
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Option A", "a") }
        });
        InlineKeyboardMarkup? capturedMarkup = null;

        _messageHandler
            .SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Do<InlineKeyboardMarkup?>(m => capturedMarkup = m),
                entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 3, Chat = new Chat { Id = TestUser.Id } });

        // Act
        await _service.SendDmWithKeyboardAsync(TestUser, message, keyboard);

        // Assert — keyboard is forwarded as-is
        Assert.That(capturedMarkup, Is.SameAs(keyboard), "The same keyboard instance must be forwarded");
    }

    [Test]
    public async Task SendDmWithKeyboardAsync_WithTelegramMessage_EnablesBotDmOnSuccess()
    {
        // Arrange
        var message = TelegramMessage.Plain("exam question");
        var keyboard = new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>());

        // Act
        await _service.SendDmWithKeyboardAsync(TestUser, message, keyboard);

        // Assert
        await _userRepository.Received(1).EnableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendDmWithAnimationEntitiesAsync

    private void SetupAnimationReturns(string returnedFileId) =>
        _messageHandler
            .SendAnimationAsync(
                Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
            .Returns(new Message
            {
                Id = 7,
                Chat = new Chat { Id = TestUser.Id },
                Animation = new Animation { FileId = returnedFileId, FileUniqueId = "u1" }
            });

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_CachedFileId_SendsByFileIdAndReportsReturnedId()
    {
        SetupAnimationReturns("cached-id");
        var caption = new TelegramMessageBuilder().Bold("Workshop Alumni").LineBreak().Text("banned!").Build();

        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, caption, "cached-id", "/nope.gif");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.True);
            Assert.That(result.Blocked, Is.False);
            Assert.That(result.AnimationFileId, Is.EqualTo("cached-id"));
        }
        await _messageHandler.Received(1).SendAnimationAsync(
            TestUser.Id,
            Arg.Is<InputFile>(f => f is InputFileId && ((InputFileId)f).Id == "cached-id"),
            caption.Text, Arg.Any<ParseMode?>(), Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
            caption.Entities, Arg.Any<CancellationToken>());
        await _userRepository.Received(1).EnableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_Forbidden_ReportsBlockedDisablesDmAndDoesNotQueue()
    {
        _messageHandler
            .SendAnimationAsync(
                Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
            .Returns<Message>(_ => throw new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), "cached-id", null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Blocked, Is.True);
            Assert.That(result.Failed, Is.True);
        }
        await _userRepository.Received(1).DisableBotDmAsync(TestUser.Id, Arg.Any<CancellationToken>());
        await _pendingNotificationsRepository.DidNotReceiveWithAnyArgs().AddPendingNotificationAsync(default, default!, default!, default);
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_NonForbiddenApiError_FailsWithoutBlocking()
    {
        _messageHandler
            .SendAnimationAsync(
                Arg.Any<long>(), Arg.Any<InputFile>(), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
            .Returns<Message>(_ => throw new ApiRequestException("Bad Request: message caption is too long", 400));

        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), "cached-id", null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Blocked, Is.False);
        }
        await _userRepository.DidNotReceiveWithAnyArgs().DisableBotDmAsync(default);
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_StaleFileId_RetriesWithUploadAndReportsNewId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anim_{Guid.NewGuid():N}.gif");
        await File.WriteAllBytesAsync(path, [0x47, 0x49, 0x46]);
        try
        {
            _messageHandler
                .SendAnimationAsync(
                    Arg.Any<long>(), Arg.Is<InputFile>(f => f is InputFileId), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                    Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                    Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
                .Returns<Message>(_ => throw new ApiRequestException("Bad Request: wrong file identifier/HTTP URL specified", 400));
            _messageHandler
                .SendAnimationAsync(
                    Arg.Any<long>(), Arg.Is<InputFile>(f => f is InputFileStream), Arg.Any<string?>(), Arg.Any<ParseMode?>(),
                    Arg.Any<ReplyParameters?>(), Arg.Any<InlineKeyboardMarkup?>(),
                    Arg.Any<IReadOnlyList<MessageEntity>?>(), Arg.Any<CancellationToken>())
                .Returns(new Message
                {
                    Id = 8,
                    Chat = new Chat { Id = TestUser.Id },
                    Animation = new Animation { FileId = "fresh-id", FileUniqueId = "u2" }
                });

            var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), "stale-id", path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.DmSent, Is.True);
                Assert.That(result.AnimationFileId, Is.EqualTo("fresh-id"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task SendDmWithAnimationEntitiesAsync_NoFileIdAndMissingFile_FailsWithoutCallingTelegram()
    {
        var result = await _service.SendDmWithAnimationEntitiesAsync(TestUser, TelegramMessage.Plain("x"), null, "/does/not/exist.gif");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DmSent, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Blocked, Is.False);
        }
        await _messageHandler.DidNotReceiveWithAnyArgs().SendAnimationAsync(default, default!);
    }

    #endregion
}
