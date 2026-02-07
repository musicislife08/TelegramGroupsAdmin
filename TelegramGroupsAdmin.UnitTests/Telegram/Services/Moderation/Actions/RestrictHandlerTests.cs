using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Actions;

/// <summary>
/// Unit tests for BotRestrictHandler.
/// Tests both single-chat and global restriction modes.
/// Uses ITelegramApiClient for mockable API calls and Parallel.ForEachAsync for cross-chat operations.
/// </summary>
[TestFixture]
public class RestrictHandlerTests
{
    private IBotChatService _mockBotChatService = null!;
    private ITelegramBotClientFactory _mockBotClientFactory = null!;
    private ITelegramApiClient _mockApiClient = null!;
    private ILogger<BotRestrictHandler> _mockLogger = null!;
    private BotRestrictHandler _handler = null!;

    // Test chat IDs for cross-chat operations
    private static readonly long[] TestChatIds = [-100001, -100002, -100003, -100004, -100005];

    [SetUp]
    public void SetUp()
    {
        _mockBotChatService = Substitute.For<IBotChatService>();
        _mockBotClientFactory = Substitute.For<ITelegramBotClientFactory>();
        _mockApiClient = Substitute.For<ITelegramApiClient>();
        _mockBotClientFactory.GetApiClientAsync().Returns(_mockApiClient);
        _mockLogger = Substitute.For<ILogger<BotRestrictHandler>>();

        _handler = new BotRestrictHandler(
            _mockBotChatService,
            _mockBotClientFactory,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        (_mockBotClientFactory as IDisposable)?.Dispose();
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
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), ChatIdentity.FromId(chatId), executor, duration, "Test mute");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(1));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
            Assert.That(result.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
        });

        // Verify single-chat API call was made
        await _mockApiClient.Received(1).RestrictChatMemberAsync(
            chatId,
            userId,
            Arg.Is<ChatPermissions>(p => p.CanSendMessages == false),
            Arg.Any<DateTime?>(),
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
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), ChatIdentity.FromId(chatId), executor, duration, "Timeout");

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

        _mockApiClient.RestrictChatMemberAsync(
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<ChatPermissions>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("User not found in chat"));

        // Act
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), ChatIdentity.FromId(chatId), executor, TimeSpan.FromHours(1), "Test");

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
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), ChatIdentity.FromId(chatId), executor, TimeSpan.FromMinutes(15), reason: null);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region Global Restriction Tests (chatId = 0)

    [Test]
    public async Task RestrictAsync_Global_CallsApiForEachHealthyChat()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");
        var duration = TimeSpan.FromHours(2);

        // Setup 5 healthy chats
        _mockBotChatService.GetHealthyChatIds().Returns(TestChatIds.ToList().AsReadOnly());

        // Act
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), chat: null, executor, duration, "Global mute");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ChatsAffected, Is.EqualTo(5));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
        });

        // Verify API was called once per chat
        await _mockApiClient.Received(5).RestrictChatMemberAsync(
            Arg.Any<long>(),
            userId,
            Arg.Any<ChatPermissions>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RestrictAsync_Global_PartialSuccess_ReturnsSuccessWithBothCounts()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromTelegramUser(888, "Admin");

        // Setup 5 healthy chats, but make 2 fail
        _mockBotChatService.GetHealthyChatIds().Returns(TestChatIds.ToList().AsReadOnly());

        _mockApiClient.RestrictChatMemberAsync(TestChatIds[1], userId, Arg.Any<ChatPermissions>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Chat error 1"));
        _mockApiClient.RestrictChatMemberAsync(TestChatIds[3], userId, Arg.Any<ChatPermissions>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Chat error 2"));

        // Act
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), chat: null, executor, TimeSpan.FromHours(1), "Spam");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "Partial success is still success");
            Assert.That(result.ChatsAffected, Is.EqualTo(3));
            Assert.That(result.ChatsFailed, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RestrictAsync_Global_ExceptionBeforeForEach_ReturnsFailure()
    {
        // Arrange
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        // Make getting healthy chats fail
        _mockBotChatService.GetHealthyChatIds()
            .Returns(_ => throw new InvalidOperationException("No managed chats available"));

        // Act
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), chat: null, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("No managed chats available"));
        });
    }

    [Test]
    public async Task RestrictAsync_Global_NoHealthyChats_ReturnsZeroCounts()
    {
        // Arrange - No healthy chats available
        const long userId = 12345L;
        var executor = Actor.FromSystem("test");

        _mockBotChatService.GetHealthyChatIds().Returns(new List<long>().AsReadOnly());

        // Act
        var result = await _handler.RestrictAsync(UserIdentity.FromId(userId), chat: null, executor, TimeSpan.FromHours(1), "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "No failures = still success, even with zero affected");
            Assert.That(result.ChatsAffected, Is.EqualTo(0));
            Assert.That(result.ChatsFailed, Is.EqualTo(0));
        });

        // Verify API was never called (no chats to process)
        await _mockApiClient.DidNotReceive().RestrictChatMemberAsync(
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<ChatPermissions>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
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
        _mockApiClient.RestrictChatMemberAsync(
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Do<ChatPermissions>(p => capturedPermissions = p),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await _handler.RestrictAsync(UserIdentity.FromId(userId), ChatIdentity.FromId(chatId), executor, TimeSpan.FromHours(1), "Test");

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
