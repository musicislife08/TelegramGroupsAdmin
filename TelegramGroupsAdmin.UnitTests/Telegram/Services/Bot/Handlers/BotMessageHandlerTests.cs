using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Bot.Handlers;

/// <summary>
/// Unit tests for BotMessageHandler pass-throughs to ITelegramApiClient.
/// The seam is ITelegramApiClient (from ITelegramBotClientFactory); TelegramApiClient itself is a
/// one-line forward into Telegram.Bot and is not tested here.
/// </summary>
[TestFixture]
public class BotMessageHandlerTests
{
    private ITelegramApiClient _apiClient = null!;
    private BotMessageHandler _handler = null!;

    private static readonly MessageEntity[] MentionEntities =
    [
        new() { Type = MessageEntityType.TextMention, Offset = 0, Length = 5, User = new User { Id = 42, FirstName = "Alice" } }
    ];

    [SetUp]
    public void SetUp()
    {
        _apiClient = Substitute.For<ITelegramApiClient>();
        var factory = Substitute.For<ITelegramBotClientFactory>();
        factory.GetApiClientAsync().Returns(_apiClient);
        _handler = new BotMessageHandler(factory);
    }

    [Test]
    public async Task EditCaptionAsync_ForwardsCaptionEntitiesToApiClient()
    {
        await _handler.EditCaptionAsync(100, 7, "Alice reported", captionEntities: MentionEntities);

        await _apiClient.Received(1).EditMessageCaptionAsync(
            100,
            7,
            caption: "Alice reported",
            parseMode: Arg.Any<ParseMode?>(),
            replyMarkup: Arg.Any<InlineKeyboardMarkup?>(),
            captionEntities: MentionEntities,
            ct: Arg.Any<CancellationToken>());
    }
}
