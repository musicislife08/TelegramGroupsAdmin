using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for WarnHandler.
/// Tests domain logic for issuing warnings (writes to JSONB warnings column on telegram_users).
/// REFACTOR-5: Updated to use ITelegramUserRepository.AddWarningAsync instead of IWarningsRepository.
/// </summary>
[TestFixture]
public class WarnHandlerTests
{
    private const long TestChatId = 123456789L;
    private ITelegramUserRepository _mockUserRepository = null!;
    private ILogger<WarnHandler> _mockLogger = null!;
    private WarnHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockLogger = Substitute.For<ILogger<WarnHandler>>();

        _handler = new WarnHandler(_mockUserRepository, _mockLogger);
    }

    #region WarnAsync Tests

    [Test]
    public async Task WarnAsync_FirstWarning_ReturnsSuccessWithCountOf1()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        // AddWarningAsync returns the active warning count after insertion
        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Spam detected", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(1));
            Assert.That(result.ErrorMessage, Is.Null);
        });

        // Verify warning was added via user repository
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w => w.Reason == "Spam detected"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_MultipleWarnings_ReturnsAccumulatedCount()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        // User already has 2 warnings, this will be the 3rd
        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(3);

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Repeated violation", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task WarnAsync_WithChatAndMessageContext_IncludesContextInWarning()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = -100123456789L;
        const long messageId = 42L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(
            userId, executor, "Spam", chatId: chatId, messageId: messageId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify context was included
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w =>
                w.ChatId == chatId &&
                w.MessageId == messageId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_NullReason_StillSucceeds()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(userId, executor, reason: null, TestChatId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify warning was inserted with null reason
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w => w.Reason == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_AddWarningFails_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database insert failed"));

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Test", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Database insert failed"));
            Assert.That(result.WarningCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task WarnAsync_UserNotFound_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Cannot add warning for unknown user 12345"));

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Test", TestChatId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("unknown user"));
        });
    }

    [Test]
    public async Task WarnAsync_WebUserExecutor_SetsCorrectActorType()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromWebUser("web-user-123", "admin@example.com");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Warned from web UI", TestChatId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify executor type was set correctly
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w =>
                w.ActorType == "web_user" &&
                w.ActorId == "web-user-123"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_TelegramUserExecutor_SetsCorrectActorType()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "ModeratorBot");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Warned by admin", TestChatId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify executor type was set correctly
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w =>
                w.ActorType == "telegram_user" &&
                w.ActorId == "999"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_SystemExecutor_SetsCorrectActorType()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("SpamDetection");

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var result = await _handler.WarnAsync(userId, executor, "Auto-detected spam", TestChatId);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify executor type was set correctly
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w =>
                w.ActorType == "system" &&
                w.ActorId == "SpamDetection"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WarnAsync_SetsDefaultExpiryOf90Days()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var beforeCall = DateTimeOffset.UtcNow;

        _mockUserRepository.AddWarningAsync(userId, Arg.Any<WarningEntry>(), Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        await _handler.WarnAsync(userId, executor, "Test", TestChatId);

        // Assert - ExpiresAt should be ~90 days from now
        await _mockUserRepository.Received(1).AddWarningAsync(
            userId,
            Arg.Is<WarningEntry>(w =>
                w.ExpiresAt != null &&
                w.ExpiresAt.Value > beforeCall.AddDays(89) &&
                w.ExpiresAt.Value < beforeCall.AddDays(91)),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
