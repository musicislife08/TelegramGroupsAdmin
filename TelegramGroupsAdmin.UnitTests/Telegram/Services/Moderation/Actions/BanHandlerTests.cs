using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for BanHandler.
/// Tests domain logic for banning, temp-banning, and unbanning users across managed chats.
/// REFACTOR-5: Updated to use ITelegramUserRepository.SetBanStatusAsync instead of IUserActionsRepository.
/// </summary>
[TestFixture]
public class BanHandlerTests
{
    private ICrossChatExecutor _mockCrossChatExecutor = null!;
    private IJobScheduler _mockJobScheduler = null!;
    private ITelegramUserRepository _mockUserRepository = null!;
    private ILogger<BanHandler> _mockLogger = null!;
    private BanHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockCrossChatExecutor = Substitute.For<ICrossChatExecutor>();
        _mockJobScheduler = Substitute.For<IJobScheduler>();
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockLogger = Substitute.For<ILogger<BanHandler>>();

        _handler = new BanHandler(
            _mockCrossChatExecutor,
            _mockJobScheduler,
            _mockUserRepository,
            _mockLogger);
    }

    #region BanAsync Tests

    [Test]
    public async Task BanAsync_SuccessfulBan_ReturnsSuccessWithChatsAffected()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Ban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 5, FailCount: 0, SkippedCount: 0));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Ban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 3, FailCount: 2, SkippedCount: 1));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Ban",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network error"));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Ban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 2, FailCount: 0, SkippedCount: 0));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Ban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 3, FailCount: 0, SkippedCount: 0));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "TempBan",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 3, FailCount: 0, SkippedCount: 0));

        _mockJobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "TempBan",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 3, FailCount: 0, SkippedCount: 0));

        _mockJobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "TempBan",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 1, FailCount: 0, SkippedCount: 0));

        _mockJobScheduler.ScheduleJobAsync(
                Arg.Any<string>(),
                Arg.Any<TempbanExpiryJobPayload>(),
                Arg.Any<int>(),
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
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TempBanAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "TempBan",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Telegram API error"));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Unban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 4, FailCount: 0, SkippedCount: 0));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Unban",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 2, FailCount: 1, SkippedCount: 0));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Unban",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Telegram API error"));

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

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Unban",
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add("TelegramUnban");
                return Task.FromResult(new CrossChatResult(SuccessCount: 1, FailCount: 0, SkippedCount: 0));
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
}
