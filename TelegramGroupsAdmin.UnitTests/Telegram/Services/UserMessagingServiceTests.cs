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
    private const long TestUserId2 = 444_555_666L;
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
    // SendToMultipleUsersAsync — batched chat-mention path
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task SendToMultipleUsersAsync_AllUsersDmDisabled_SendsEntityOverloadWithOneTextMentionPerUser()
    {
        // Arrange: two users with DM disabled → both go to chat-mention batch
        var user1 = MakeUser(TestUserId1, firstName: "Alice", botDmEnabled: false);
        var user2 = MakeUser(TestUserId2, firstName: "Bob", botDmEnabled: false);

        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user1);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId2, Arg.Any<CancellationToken>())
            .Returns(user2);

        var chat = MakeChat();

        // Act
        var results = await _sut.SendToMultipleUsersAsync(
            [TestUserId1, TestUserId2],
            chat,
            TelegramMessage.Plain("Please check this."));

        // Assert: entity overload called once, with exactly 2 TextMention entities
        await _mockMessageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: TestChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m!.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 2),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        // Assert: string+ParseMode overload was NOT called
        await _mockMessageService
            .DidNotReceive()
            .SendAndSaveMessageAsync(
                chatId: Arg.Any<long>(),
                text: Arg.Any<string>(),
                parseMode: Arg.Any<ParseMode?>(),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());

        // Assert: both users reported as ChatMention success
        using var _ = Assert.EnterMultipleScope();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.Success && r.DeliveryMethod == MessageDeliveryMethod.ChatMention), Is.True);
    }

    [Test]
    public async Task SendToMultipleUsersAsync_SingleUserDmDisabled_SendsEntityOverloadWithOneTextMention()
    {
        // Arrange: one user, DM disabled
        var user = MakeUser(TestUserId1, firstName: "Carol", botDmEnabled: false);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        var chat = MakeChat();

        // Act
        await _sut.SendToMultipleUsersAsync([TestUserId1], chat, TelegramMessage.Plain("Hello!"));

        // Assert: exactly 1 TextMention entity in the sent message
        await _mockMessageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: TestChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m!.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 1),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendToMultipleUsersAsync_DmFails_FallenBackUserGetsTextMentionEntity()
    {
        // Arrange: user has DM enabled but DM fails → falls back to chat-mention batch
        var user = MakeUser(TestUserId1, firstName: "Dave", botDmEnabled: true);
        _mockUserRepo
            .GetByTelegramIdAsync(TestUserId1, Arg.Any<CancellationToken>())
            .Returns(user);

        // DM service reports failure
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
        await _sut.SendToMultipleUsersAsync([TestUserId1], chat, TelegramMessage.Plain("Fallback mention."));

        // Assert: entity overload was used for the fallback path
        await _mockMessageService
            .Received(1)
            .SendAndSaveMessageAsync(
                chatId: TestChatId,
                message: Arg.Is<TelegramMessage>(m =>
                    m!.Entities.Count(e => e.Type == MessageEntityType.TextMention) == 1),
                replyParameters: Arg.Any<ReplyParameters?>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

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
}
