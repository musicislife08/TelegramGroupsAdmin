using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for TrustHandler.
/// Tests domain logic for trusting and untrusting users (setting is_trusted flag).
/// </summary>
[TestFixture]
public class TrustHandlerTests
{
    private ITelegramUserRepository _mockUserRepository = null!;
    private ILogger<TrustHandler> _mockLogger = null!;
    private TrustHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockLogger = Substitute.For<ILogger<TrustHandler>>();

        _handler = new TrustHandler(_mockUserRepository, _mockLogger);
    }

    #region TrustAsync Tests

    [Test]
    public async Task TrustAsync_SuccessfulTrust_ReturnsSuccess()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        // Act
        var result = await _handler.TrustAsync(UserIdentity.FromId(userId), executor, "Verified user");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        });

        // Verify repository was called with correct parameters
        await _mockUserRepository.Received(1).UpdateTrustStatusAsync(
            userId,
            isTrusted: true,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrustAsync_NullReason_StillSucceeds()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(999, "Admin");

        // Act
        var result = await _handler.TrustAsync(UserIdentity.FromId(userId), executor, reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task TrustAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.UpdateTrustStatusAsync(
                userId,
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _handler.TrustAsync(UserIdentity.FromId(userId), executor, "Test reason");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Database connection failed"));
        });
    }

    [Test]
    public async Task TrustAsync_DifferentExecutorTypes_AllSucceed()
    {
        // Arrange
        const long userId = 12345L;
        var executors = new[]
        {
            Actor.FromSystem("AutoTrust"),
            Actor.FromTelegramUser(999, "TgAdmin"),
            Actor.FromWebUser("web-user-id", "admin@example.com")
        };

        // Act & Assert
        foreach (var executor in executors)
        {
            var result = await _handler.TrustAsync(UserIdentity.FromId(userId), executor, "Test");
            Assert.That(result.Success, Is.True, $"Failed for executor type: {executor.Type}");
        }
    }

    #endregion

    #region UntrustAsync Tests

    [Test]
    public async Task UntrustAsync_SuccessfulUntrust_ReturnsSuccess()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        // Act
        var result = await _handler.UntrustAsync(UserIdentity.FromId(userId), executor, "Trust revoked due to ban");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        });

        // Verify repository was called with isTrusted: false
        await _mockUserRepository.Received(1).UpdateTrustStatusAsync(
            userId,
            isTrusted: false,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UntrustAsync_NullReason_StillSucceeds()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("AutoBan");

        // Act
        var result = await _handler.UntrustAsync(UserIdentity.FromId(userId), executor, reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task UntrustAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockUserRepository.UpdateTrustStatusAsync(
                userId,
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("User not found"));

        // Act
        var result = await _handler.UntrustAsync(UserIdentity.FromId(userId), executor, "Test reason");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("User not found"));
        });
    }

    [Test]
    public async Task UntrustAsync_CalledByAutoBan_SucceedsWithSystemExecutor()
    {
        // Arrange - Auto-ban scenario uses Actor.AutoBan
        const long userId = 12345L;
        var executor = Actor.AutoBan;

        // Act
        var result = await _handler.UntrustAsync(UserIdentity.FromId(userId), executor, "Exceeded warning threshold");

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify the repository was called
        await _mockUserRepository.Received(1).UpdateTrustStatusAsync(
            userId,
            isTrusted: false,
            Arg.Any<CancellationToken>());
    }

    #endregion
}
