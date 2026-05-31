using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Unit tests for AdminMentionHandler.NotifyAdminsAsync.
///
/// Testing strategy:
/// - IChatAdminsRepository, IBotUserService, and IBotMessageService are substituted.
/// - Tests verify that the entity-based IBotMessageService overload (TelegramMessage) is used
///   and that no raw HTML mention strings appear in the message text.
/// </summary>
[TestFixture]
public class AdminMentionHandlerTests
{
    private const long BotId = 999_000_001L;
    private const long SenderId = 111_111_111L;
    private const long Admin1Id = 222_222_222L;
    private const long Admin2Id = 333_333_333L;
    private const long ChatId = -100_123_456_789L;
    private const int MessageId = 42;

#pragma warning disable NUnit1032
    private IChatAdminsRepository _chatAdminsRepository = null!;
    private IBotUserService _userService = null!;
    private IBotMessageService _messageService = null!;
#pragma warning restore NUnit1032

    private AdminMentionHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _chatAdminsRepository = Substitute.For<IChatAdminsRepository>();
        _userService = Substitute.For<IBotUserService>();
        _messageService = Substitute.For<IBotMessageService>();

        _sut = new AdminMentionHandler(
            NullLogger<AdminMentionHandler>.Instance,
            _chatAdminsRepository,
            _userService,
            _messageService);

        _userService
            .GetBotIdAsync(Arg.Any<CancellationToken>())
            .Returns(BotId);

        // Default: entity overload returns a stub Message
        _messageService
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Any<TelegramMessage>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 1 });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Message MakeMessage(long senderId = SenderId) => new()
    {
        Id = MessageId,
        Chat = new Chat { Id = ChatId, Type = ChatType.Supergroup },
        From = new User { Id = senderId, IsBot = false, FirstName = "Sender" },
        Text = "@admin help"
    };

    private static ChatAdmin MakeAdmin(long userId, string firstName, string? username = null) =>
        new()
        {
            Id = userId,
            ChatId = ChatId,
            User = new UserIdentity(userId, firstName, null, username),
            IsCreator = false,
            IsActive = true,
            PromotedAt = DateTimeOffset.UtcNow,
            LastVerifiedAt = DateTimeOffset.UtcNow
        };

    // ─────────────────────────────────────────────────────────────────────────
    // NotifyAdminsAsync — entity overload path
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task NotifyAdminsAsync_TwoAdmins_UsesEntityOverloadWithBoldHeaderAndTwoTextMentions()
    {
        // Arrange: two admins, neither the sender nor the bot
        var admin1 = MakeAdmin(Admin1Id, "Alice", username: "alice_admin");
        var admin2 = MakeAdmin(Admin2Id, "Bob", username: null);

        _chatAdminsRepository
            .GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([admin1, admin2]);

        var message = MakeMessage();

        // Act
        await _sut.NotifyAdminsAsync(message);

        // Assert: entity overload called once
        await _messageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: ChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    // Bold entity present (Admin Alert header)
                    m.Entities.Any(e => e.Type == MessageEntityType.Bold) &&
                    // Exactly 2 TextMention entities
                    m.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 2 &&
                    // Both correct user IDs
                    m.Entities.Where(e => e.Type == MessageEntityType.TextMention)
                              .All(e => e.User != null) &&
                    m.Entities.Where(e => e.Type == MessageEntityType.TextMention)
                              .Select(e => e.User!.Id)
                              .OrderBy(id => id)
                              .SequenceEqual(new[] { Admin1Id, Admin2Id }.OrderBy(id => id)) &&
                    // No raw HTML mention strings in the text
                    !m.Text.Contains("<a href")),
                replyParameters: Arg.Is<ReplyParameters?>(r => r != null && r.MessageId == MessageId),
                cancellationToken: Arg.Any<CancellationToken>());

        // Assert: string+ParseMode overload was NOT called
        await _messageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsAsync_TwoAdmins_MessageTextDoesNotContainRawHtmlMention()
    {
        // Arrange
        var admin1 = MakeAdmin(Admin1Id, "Alice", username: "alice_admin");
        var admin2 = MakeAdmin(Admin2Id, "Bob", username: null);

        _chatAdminsRepository
            .GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([admin1, admin2]);

        TelegramMessage? captured = null;
        await _messageService
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Do<TelegramMessage>(m => captured = m),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        // Act
        await _sut.NotifyAdminsAsync(MakeMessage());

        // Assert
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Text, Does.Not.Contain("<a href"));
    }

    [Test]
    public async Task NotifyAdminsAsync_SenderIsAdmin_SenderSkippedAndOtherAdminMentioned()
    {
        // Arrange: admin1 is the sender — only admin2 should be notified
        var admin1 = MakeAdmin(SenderId, "Sender");
        var admin2 = MakeAdmin(Admin2Id, "Bob");

        _chatAdminsRepository
            .GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([admin1, admin2]);

        // Act
        await _sut.NotifyAdminsAsync(MakeMessage(senderId: SenderId));

        // Assert: only 1 TextMention (admin2, not sender)
        await _messageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: ChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 1 &&
                    m.Entities.First(e => e.Type == MessageEntityType.TextMention).User!.Id == Admin2Id),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsAsync_OnlySenderAndBotAreAdmins_NoMessageSent()
    {
        // Arrange: only the sender and bot in the admin list — nothing to notify
        var senderAdmin = MakeAdmin(SenderId, "Sender");
        var botAdmin = MakeAdmin(BotId, "TestBot");

        _chatAdminsRepository
            .GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([senderAdmin, botAdmin]);

        // Act
        await _sut.NotifyAdminsAsync(MakeMessage());

        // Assert: no message sent at all
        await _messageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Any<TelegramMessage>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsAsync_EmptyAdminList_NoMessageSent()
    {
        // Arrange: no admins cached for the chat
        _chatAdminsRepository
            .GetChatAdminsAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        await _sut.NotifyAdminsAsync(MakeMessage());

        // Assert: no send at all
        await _messageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                message: Arg.Any<TelegramMessage>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }
}
