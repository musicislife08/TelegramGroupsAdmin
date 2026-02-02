using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for BotBanHandler.
/// Tests domain logic for banning, temp-banning, and unbanning users across managed chats.
/// PHASE8: Updated to mock ITelegramApiClient (wrapper interface) instead of ITelegramBotClient (extension methods unmockable).
/// </summary>
[TestFixture]
public class BanHandlerTests
{
    private IBotChatService _mockBotChatService = null!;
    private ITelegramBotClientFactory _mockBotClientFactory = null!;
    private ITelegramApiClient _mockApiClient = null!;
    private IJobScheduler _mockJobScheduler = null!;
    private ITelegramUserRepository _mockUserRepository = null!;
    private ILogger<BotBanHandler> _mockLogger = null!;
    private BotBanHandler _handler = null!;

    // Test chat IDs for cross-chat operations
    private static readonly long[] TestChatIds = [-100001, -100002, -100003, -100004, -100005];

    [SetUp]
    public void SetUp()
    {
        _mockBotChatService = Substitute.For<IBotChatService>();
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockApiClient = Substitute.For<ITelegramApiClient>();
        _mockBotClientFactory.GetApiClientAsync().Returns(_mockApiClient);
        _mockJobScheduler = Substitute.For<IJobScheduler>();
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockLogger = Substitute.For<ILogger<BotBanHandler>>();

        _handler = new BotBanHandler(
            _mockBotChatService,
            _mockBotClientFactory,
            _mockJobScheduler,
            _mockUserRepository,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        // NSubstitute mocks don't require disposal, but NUnit analyzer expects it
        (_mockBotClientFactory as IDisposable)?.Dispose();
    }

    #region BanAsync Tests

    [Test]
    public async Task BanAsync_SuccessfulBan_ReturnsSuccessWithChatsAffected()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        // Setup 5 healthy chats, all bans succeed
        _mockBotChatService.GetHealthyChatIds().Returns(TestChatIds.ToList().AsReadOnly());

        // Act
        var result = await _handler.BanAsync(userId, executor, "Spam violation");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public async Task BanAsync_PartialSuccess_ReturnsSuccessWithBothCounts()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "AdminUser");

        // Setup 5 healthy chats, but make 2 fail
        _mockBotChatService.GetHealthyChatIds().Returns(TestChatIds.ToList().AsReadOnly());

        // Make specific chats fail
        _mockApiClient.BanChatMemberAsync(TestChatIds[1], userId, Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Chat error 1"));
        _mockApiClient.BanChatMemberAsync(TestChatIds[3], userId, Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Chat error 2"));

        // Act
        var result = await _handler.BanAsync(userId, executor, "Repeated violations", 100L);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Partial success is still success");
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
            Assert.That(result.ChatsFailed, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task BanAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        // Make getting healthy chats fail
        _mockBotChatService.GetHealthyChatIds()
            .Returns(_ => throw new InvalidOperationException("Network error"));

        // Act
        var result = await _handler.BanAsync(userId, executor, "Test reason");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Network error"));
            Assert.That(result.ChatsAffected, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task BanAsync_NullReason_StillSucceeds()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002 }.AsReadOnly());

        // Act
        var result = await _handler.BanAsync(userId, executor, reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task BanAsync_SuccessfulBan_SetsBanStatusOnUser()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002, -100003 }.AsReadOnly());

        // Act
        await _handler.BanAsync(userId, executor, "Spam violation", 42L);

        // Assert - Verify ban status was set on user (source of truth)
        await _mockUserRepository.Received(1).SetBanStatusAsync(
            userId,
            isBanned: true,
            expiresAt: null, // Permanent ban
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region TempBanAsync Tests

    [Test]
    public async Task TempBanAsync_SuccessfulTempBan_ReturnsSuccessAndSchedulesJob()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(24);

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002, -100003 }.AsReadOnly());

        _mockJobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("job-123");

        // Act
        var result = await _handler.TempBanAsync(userId, executor, duration, "Timeout for spam");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
            Assert.That(result.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
        });

        // Verify job was scheduled
        await _mockJobScheduler.Received(1).ScheduleJobAsync(
            "TempbanExpiry",
            Arg.Is<TempbanExpiryJobPayload>(p => p.UserId == userId),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanAsync_SuccessfulTempBan_SetsBanStatusWithExpiry()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(2);
        var beforeCall = DateTimeOffset.UtcNow;

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002, -100003 }.AsReadOnly());

        _mockJobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("job-123");

        // Act
        await _handler.TempBanAsync(userId, executor, duration, "Timeout", 42L);

        // Assert - Verify ban status was set WITH expiry
        await _mockUserRepository.Received(1).SetBanStatusAsync(
            userId,
            isBanned: true,
            Arg.Is<DateTimeOffset?>(exp =>
                exp != null &&
                exp.Value > beforeCall.AddHours(1.9) &&
                exp.Value < beforeCall.AddHours(2.1)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanAsync_NullReason_UsesDefaultReason()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromMinutes(30);

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001 }.AsReadOnly());

        _mockJobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("job-456");

        // Act
        var result = await _handler.TempBanAsync(userId, executor, duration, reason: null);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify default reason was used
        await _mockJobScheduler.Received(1).ScheduleJobAsync(
            Arg.Any<string>(),
            Arg.Is<TempbanExpiryJobPayload>(p => p.Reason == "Temporary ban"),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBotChatService.GetHealthyChatIds()
            .Returns(_ => throw new Exception("Telegram API error"));

        // Act
        var result = await _handler.TempBanAsync(userId, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Telegram API error"));
        });
    }

    #endregion

    #region UnbanAsync Tests

    [Test]
    public async Task UnbanAsync_SuccessfulUnban_ClearsBanStatusAndUnbansOnTelegram()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002, -100003, -100004 }.AsReadOnly());

        // Act
        var result = await _handler.UnbanAsync(userId, executor, "False positive");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(4));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
        });

        // Verify ban status was cleared on user (source of truth)
        await _mockUserRepository.Received(1).SetBanStatusAsync(
            userId,
            isBanned: false,
            expiresAt: null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnbanAsync_PartialSuccess_StillReturnsSuccess()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(888, "Admin");

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001, -100002, -100003 }.AsReadOnly());

        // Make one chat fail
        _mockApiClient.UnbanChatMemberAsync(-100002, userId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Chat error"));

        // Act
        var result = await _handler.UnbanAsync(userId, executor, "Appeal approved");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(2));
            Assert.That(result.ChatsFailed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UnbanAsync_TelegramApiFails_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBotChatService.GetHealthyChatIds()
            .Returns(_ => throw new Exception("Telegram API error"));

        // Act
        var result = await _handler.UnbanAsync(userId, executor, "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Telegram API error"));
        });
    }

    [Test]
    public async Task UnbanAsync_ClearsBanStatusAfterTelegramUnban()
    {
        // Arrange - Verify the order: Telegram unban first, then clear DB status
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var callOrder = new List<string>();

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long> { -100001 }.AsReadOnly());

        _mockApiClient.UnbanChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add("TelegramUnban");
                return Task.CompletedTask;
            });

        _mockUserRepository.SetBanStatusAsync(
                userId,
                Arg.Any<bool>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add("SetBanStatus");
                return Task.CompletedTask;
            });

        // Act
        await _handler.UnbanAsync(userId, executor, "Test");

        // Assert - Telegram unban should happen before DB update
        Assert.That(callOrder, Is.EqualTo(new[] { "TelegramUnban", "SetBanStatus" }));
    }

    #endregion

    #region BanAsync (Single-Chat Overload) Tests

    [Test]
    public async Task BanAsync_SingleChat_SuccessfulBan_ReturnsSuccessWithOneChat()
    {
        // Arrange
        var user = new User { Id = 12345, FirstName = "Spammer" };
        var chat = new Chat { Id = -100123456789, Title = "Test Group" };
        var executor = Actor.AutoDetection;

        // Act
        var result = await _handler.BanAsync(user, chat, executor, "Lazy ban sync");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
        });

        // Verify Telegram API called
        await _mockApiClient.Received(1).BanChatMemberAsync(
            chat.Id, user.Id, Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        // Verify ban status set (idempotent)
        await _mockUserRepository.Received(1).SetBanStatusAsync(
            user.Id, isBanned: true, expiresAt: null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BanAsync_SingleChat_TelegramApiFails_ReturnsFailure()
    {
        // Arrange
        var user = new User { Id = 12345, FirstName = "Test" };
        var chat = new Chat { Id = -100123456789, Title = "Test Group" };
        var executor = Actor.AutoDetection;

        _mockApiClient.BanChatMemberAsync(chat.Id, user.Id, Arg.Any<DateTime?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("User is admin"));

        // Act
        var result = await _handler.BanAsync(user, chat, executor, "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("User is admin"));
        });
    }

    [Test]
    public async Task BanAsync_SingleChat_NullReason_StillSucceeds()
    {
        // Arrange
        var user = new User { Id = 12345, FirstName = "Test" };
        var chat = new Chat { Id = -100123456789, Title = "Test Group" };

        // Act
        var result = await _handler.BanAsync(user, chat, Actor.AutoDetection, reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    #endregion
}
