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
/// Unit tests for UserMessagingService.
///
/// Testing strategy:
/// - ITelegramUserRepository, IBotDmService, and IBotMessageService are substituted.
/// - Telegram.Bot concrete types (Chat, Message) are created via direct initialization.
/// - Tests verify that chat-mention paths use the entity-based IBotMessageService overload
///   (TelegramMessage) and NOT the string+ParseMode overload.
/// </summary>
[TestFixture]
public class UserMessagingServiceTests
{
    private const long TestUserId1 = 111_222_333L;
    private const long TestChatId = -100_987_654_321L;

#pragma warning disable NUnit1032
    private ITelegramUserRepository _mockUserRepo = null!;
    private IBotDmService _mockDmService = null!;
    private IBotMessageService _mockMessageService = null!;
#pragma warning restore NUnit1032

    private UserMessagingService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _mockUserRepo = Substitute.For<ITelegramUserRepository>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockMessageService = Substitute.For<IBotMessageService>();

        _sut = new UserMessagingService(
            _mockUserRepo,
            _mockDmService,
            _mockMessageService,
            NullLogger<UserMessagingService>.Instance);

        // Default: SendAndSaveMessageAsync (entity overload) returns a stub Message
        _mockMessageService
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

    private static Chat MakeChat(long id = TestChatId) =>
        new() { Id = id, Type = ChatType.Supergroup };

    private static TelegramUser MakeUser(
        long telegramUserId,
        string firstName = "Alice",
        string? lastName = null,
        string? username = null,
        bool botDmEnabled = false) =>
        new(
            TelegramUserId: telegramUserId,
            Username: username,
            FirstName: firstName,
            LastName: lastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: false,
            IsTrusted: false,
            IsBanned: false,
            KickCount: 0,
            BotDmEnabled: botDmEnabled,
            FirstSeenAt: DateTimeOffset.UtcNow,
            LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    // ─────────────────────────────────────────────────────────────────────────
    // SendToUserAsync — single-user chat-mention path via SendChatMentionAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task SendToUserAsync_DmDisabled_UsesEntityOverloadWithOneTextMention()
    {
        // Arrange: DM disabled → goes straight to chat mention
        var user = MakeUser(TestUserId1, firstName: "Eve", botDmEnabled: false);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        var chat = MakeChat();

        // Act
        var result = await _sut.SendToUserAsync(TestUserId1, chat, TelegramMessage.Plain("Check this."));

        // Assert: entity overload called
        await _mockMessageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: TestChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m!.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 1),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        // Assert: string+ParseMode overload NOT called
        await _mockMessageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        using var _ = Assert.EnterMultipleScope();
        Assert.That(result.Success, Is.True);
        Assert.That(result.DeliveryMethod, Is.EqualTo(MessageDeliveryMethod.ChatMention));
    }

    [Test]
    public async Task SendToUserAsync_DmFails_FallsBackToEntityChatMention()
    {
        // Arrange: DM enabled but fails → falls back to chat mention
        var user = MakeUser(TestUserId1, firstName: "Frank", botDmEnabled: true);
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

        var chat = MakeChat();

        // Act
        var result = await _sut.SendToUserAsync(TestUserId1, chat, TelegramMessage.Plain("Fallback text."));

        // Assert: entity overload used on fallback
        await _mockMessageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: TestChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m!.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 1),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        Assert.That(result.DeliveryMethod, Is.EqualTo(MessageDeliveryMethod.ChatMention));
    }

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
}
