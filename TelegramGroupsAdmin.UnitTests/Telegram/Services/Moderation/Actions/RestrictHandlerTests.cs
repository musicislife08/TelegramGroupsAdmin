using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for RestrictHandler.
/// Tests both single-chat and global restriction modes.
/// </summary>
[TestFixture]
public class RestrictHandlerTests
{
    private ITelegramBotClientFactory _mockBotClientFactory = null!;
    private ICrossChatExecutor _mockCrossChatExecutor = null!;
    private ITelegramOperations _mockOperations = null!;
    private ILogger<RestrictHandler> _mockLogger = null!;
    private RestrictHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockCrossChatExecutor = Substitute.For<ICrossChatExecutor>();
        _mockOperations = Substitute.For<ITelegramOperations>();
        _mockLogger = Substitute.For<ILogger<RestrictHandler>>();

        // Default setup: bot client factory returns mock operations
        _mockBotClientFactory.GetOperationsAsync().Returns(_mockOperations);

        _handler = new RestrictHandler(
            _mockBotClientFactory,
            _mockCrossChatExecutor,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockBotClientFactory.Dispose();
    }

    #region Single Chat Restriction Tests (chatId > 0)

    [Test]
    public async Task RestrictAsync_SingleChat_CallsTelegramDirectly()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(1);

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, duration, "Test mute");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
            Assert.That(result.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
        });

        // Verify single-chat API call was made
        await _mockOperations.Received(1).RestrictChatMemberAsync(
            chatId,
            userId,
            Arg.Is<ChatPermissions>(p => p.CanSendMessages == false),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());

        // Verify cross-chat executor was NOT called
        await _mockCrossChatExecutor.DidNotReceive().ExecuteAcrossChatsAsync(
            Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictAsync_SingleChat_WithSpecificDuration_SetsCorrectExpiry()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromTelegramUser(999, "AdminUser");
        var duration = TimeSpan.FromMinutes(30);
        var expectedExpiryLower = DateTimeOffset.UtcNow.AddMinutes(29);
        var expectedExpiryUpper = DateTimeOffset.UtcNow.AddMinutes(31);

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, duration, "Timeout");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ExpiresAt, Is.GreaterThan(expectedExpiryLower));
            Assert.That(result.ExpiresAt, Is.LessThan(expectedExpiryUpper));
        });
    }

    [Test]
    public async Task RestrictAsync_SingleChat_ApiError_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("test");

        _mockOperations.RestrictChatMemberAsync(
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<ChatPermissions>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("User not found in chat"));

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("User not found in chat"));
            Assert.That(result.ChatsAffected, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task RestrictAsync_SingleChat_NullReason_StillSucceeds()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("test");

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromMinutes(15), reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region Global Restriction Tests (chatId = 0)

    [Test]
    public async Task RestrictAsync_Global_UsesCrossChatExecutor()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 0; // Global sentinel
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(2);

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Restrict",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 5, FailCount: 0, SkippedCount: 1));

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, duration, "Global mute");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
        });

        // Verify cross-chat executor was called
        await _mockCrossChatExecutor.Received(1).ExecuteAcrossChatsAsync(
            Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
            "Restrict",
            Arg.Any<CancellationToken>());

        // Verify direct bot client was NOT called (except through cross-chat executor)
        await _mockBotClientFactory.DidNotReceive().GetOperationsAsync();
    }

    [Test]
    public async Task RestrictAsync_Global_PartialSuccess_ReturnsSuccessWithBothCounts()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 0;
        var executor = Actor.FromTelegramUser(888, "Admin");

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Restrict",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 3, FailCount: 2, SkippedCount: 1));

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromHours(1), "Spam");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Partial success is still success");
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
            Assert.That(result.ChatsFailed, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RestrictAsync_Global_ExceptionThrown_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 0;
        var executor = Actor.FromSystem("test");

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Restrict",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No managed chats available"));

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("No managed chats available"));
        });
    }

    [Test]
    public async Task RestrictAsync_Global_AllChatsSkipped_ReturnsZeroCounts()
    {
        // Arrange - No healthy chats available
        const long userId = 12345L;
        const long chatId = 0;
        var executor = Actor.FromSystem("test");

        _mockCrossChatExecutor.ExecuteAcrossChatsAsync(
                Arg.Any<Func<ITelegramOperations, long, CancellationToken, Task>>(),
                "Restrict",
                Arg.Any<CancellationToken>())
            .Returns(new CrossChatResult(SuccessCount: 0, FailCount: 0, SkippedCount: 5));

        // Act
        var result = await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "No failures = still success, even with zero affected");
            Assert.That(result.ChatsAffected, Is.EqualTo(0));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
        });
    }

    #endregion

    #region Permission Verification Tests

    [Test]
    public async Task RestrictAsync_AppliesFullMutePermissions()
    {
        // Arrange
        const long userId = 12345L;
        const long chatId = 67890L;
        var executor = Actor.FromSystem("test");

        ChatPermissions? capturedPermissions = null;
        _mockOperations.RestrictChatMemberAsync(
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Do<ChatPermissions>(p => capturedPermissions = p),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await _handler.RestrictAsync(userId, chatId, executor, TimeSpan.FromHours(1), "Test");

        // Assert - All permissions should be false (full mute)
        Assert.That(capturedPermissions, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(capturedPermissions!.CanSendMessages, Is.False);
            Assert.That(capturedPermissions.CanSendAudios, Is.False);
            Assert.That(capturedPermissions.CanSendDocuments, Is.False);
            Assert.That(capturedPermissions.CanSendPhotos, Is.False);
            Assert.That(capturedPermissions.CanSendVideos, Is.False);
            Assert.That(capturedPermissions.CanSendVideoNotes, Is.False);
            Assert.That(capturedPermissions.CanSendVoiceNotes, Is.False);
            Assert.That(capturedPermissions.CanSendPolls, Is.False);
            Assert.That(capturedPermissions.CanSendOtherMessages, Is.False);
            Assert.That(capturedPermissions.CanAddWebPagePreviews, Is.False);
            Assert.That(capturedPermissions.CanChangeInfo, Is.False);
            Assert.That(capturedPermissions.CanInviteUsers, Is.False);
            Assert.That(capturedPermissions.CanPinMessages, Is.False);
            Assert.That(capturedPermissions.CanManageTopics, Is.False);
        });
    }

    #endregion
}
