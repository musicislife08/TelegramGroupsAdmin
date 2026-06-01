using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Metrics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot;

/// <summary>
/// Unit tests for BotMessageService entity-based overloads.
/// Verifies that TelegramMessage overloads forward entities and null parse_mode
/// to the underlying handler, keeping all existing string overloads intact.
/// </summary>
[TestFixture]
public class BotMessageServiceEntityTests
{
    private IBotMessageHandler _handler = null!;
    private IBotUserService _userService = null!;
    private IBotChatHandler _chatHandler = null!;
    private IMessageHistoryRepository _messageRepo = null!;
    private IMessageEditService _editService = null!;
    private ITelegramUserRepository _userRepo = null!;
    private ApiMetrics _apiMetrics = null!;
    private BotMessageService _service = null!;

    private static readonly User BotUser = new()
    {
        Id = 1L,
        IsBot = true,
        FirstName = "TestBot",
        Username = "test_bot"
    };

    [SetUp]
    public void SetUp()
    {
        _handler = Substitute.For<IBotMessageHandler>();
        _userService = Substitute.For<IBotUserService>();
        _chatHandler = Substitute.For<IBotChatHandler>();
        _messageRepo = Substitute.For<IMessageHistoryRepository>();
        _editService = Substitute.For<IMessageEditService>();
        _userRepo = Substitute.For<ITelegramUserRepository>();
        _apiMetrics = new ApiMetrics();

        _userService.GetMeAsync(Arg.Any<CancellationToken>()).Returns(BotUser);

        _service = new BotMessageService(
            _handler,
            _userService,
            _chatHandler,
            _messageRepo,
            _editService,
            _userRepo,
            _apiMetrics,
            NullLogger<BotMessageService>.Instance);
    }

    #region SendAndSaveMessageAsync — TelegramMessage overload

    [Test]
    public async Task SendAndSaveMessageAsync_with_TelegramMessage_forwards_entities_and_no_parse_mode()
    {
        // Arrange
        var sentMessage = new Message { Id = 1, Chat = new Chat { Id = 42 } };
        _handler.SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(sentMessage);

        var msg = new TelegramMessageBuilder().Bold("hi").Build();

        // Act
        await _service.SendAndSaveMessageAsync(42, msg);

        // Assert — entities forwarded, parse_mode null
        await _handler.Received(1).SendAsync(
            chatId: 42,
            text: "hi",
            parseMode: null,
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            entities: Arg.Is<IReadOnlyList<MessageEntity>?>(e =>
                e != null && e.Count == 1 && e[0].Type == MessageEntityType.Bold),
            ct: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAndSaveMessageAsync_with_TelegramMessage_saves_to_history()
    {
        // Arrange
        var sentMessage = new Message { Id = 99, Chat = new Chat { Id = 42 } };
        _handler.SendAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(sentMessage);

        var msg = new TelegramMessageBuilder().Text("hello").Build();

        // Act
        var result = await _service.SendAndSaveMessageAsync(42, msg);

        // Assert — message saved, returned value is the sent message
        await _messageRepo.Received(1).InsertMessageAsync(
            Arg.Any<MessageRecord>(),
            Arg.Any<CancellationToken>());
        Assert.That(result.Id, Is.EqualTo(99));
    }

    #endregion

    #region EditAndUpdateMessageAsync — TelegramMessage overload

    [Test]
    public async Task EditAndUpdateMessageAsync_with_TelegramMessage_forwards_entities_and_no_parse_mode()
    {
        // Arrange
        const int messageId = 7;
        const long chatId = 42;
        var oldRecord = new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(1, null, "Bot", null),
            Chat: new ChatIdentity(chatId, null),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: "old text",
            PhotoFileId: null, PhotoFileSize: null, Urls: null, EditDate: null, ContentHash: null,
            PhotoLocalPath: null, PhotoThumbnailPath: null, ChatIconPath: null, UserPhotoPath: null,
            DeletedAt: null, DeletionSource: null, ReplyToMessageId: null, ReplyToUser: null,
            ReplyToText: null, MediaType: null, MediaFileId: null, MediaFileSize: null,
            MediaFileName: null, MediaMimeType: null, MediaLocalPath: null, MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.UserAdmin);

        _messageRepo.GetMessageAsync(messageId, chatId, Arg.Any<CancellationToken>())
            .Returns(oldRecord);

        var editedMessage = new Message
        {
            Id = messageId,
            Chat = new Chat { Id = chatId },
            EditDate = DateTime.UtcNow
        };
        _handler.EditTextAsync(
                chatId: Arg.Any<long>(),
                messageId: Arg.Any<int>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                entities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(editedMessage);

        var msg = new TelegramMessageBuilder().Bold("new text").Build();

        // Act
        await _service.EditAndUpdateMessageAsync(chatId, messageId, msg);

        // Assert — entities forwarded, parse_mode null
        await _handler.Received(1).EditTextAsync(
            chatId: chatId,
            messageId: messageId,
            text: "new text",
            parseMode: null,
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            entities: Arg.Is<IReadOnlyList<MessageEntity>?>(e =>
                e != null && e.Count == 1 && e[0].Type == MessageEntityType.Bold),
            ct: Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendAndSaveAnimationAsync — TelegramMessage caption overload

    [Test]
    public async Task SendAndSaveAnimationAsync_with_TelegramMessage_caption_forwards_entities_and_no_parse_mode()
    {
        // Arrange — animation overload forwards caption_entities to the handler
        var animation = InputFile.FromFileId("anim_file_id");
        var sentMessage = new Message
        {
            Id = 5,
            Chat = new Chat { Id = 42 },
            Animation = new Animation
            {
                FileId = "anim_file_id",
                FileUniqueId = "unique",
                Width = 100,
                Height = 100,
                Duration = 3
            }
        };
        _handler.SendAnimationAsync(
                chatId: Arg.Any<long>(),
                animation: Arg.Any<InputFile>(),
                caption: Arg.Any<string?>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
                captionEntities: Arg.Any<IReadOnlyList<MessageEntity>?>(),
                ct: Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(sentMessage);

        var captionMsg = new TelegramMessageBuilder().Bold("caption text").Build();

        // Act
        var result = await _service.SendAndSaveAnimationAsync(42, animation, captionMsg);

        // Assert — caption text + entities forwarded, parse_mode null (entities and parse_mode are mutually exclusive)
        await _handler.Received(1).SendAnimationAsync(
            chatId: 42,
            animation: Arg.Any<InputFile>(),
            caption: "caption text",
            parseMode: null,
            replyParameters: Arg.Any<ReplyParameters?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            captionEntities: Arg.Is<IReadOnlyList<MessageEntity>?>(e =>
                e != null && e.Count == 1 && e[0].Type == MessageEntityType.Bold),
            ct: Arg.Any<CancellationToken>());
        Assert.That(result.Id, Is.EqualTo(5));
    }

    #endregion
}
