using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Unit tests for NotificationHandler - DM and admin notifications for moderation actions.
///
/// Architecture:
/// - NotificationHandler is a domain expert for all moderation-related notifications
/// - Handles user DM notifications (warnings, temp bans, critical violations)
/// - Handles admin notifications (bans, spam bans)
///
/// Test Coverage (8 tests):
/// - NotifyUserCriticalViolationAsync: User notification for security policy violations
/// - NotifyAdminsSpamBanAsync: Rich admin notification with message context
///
/// Mocking Strategy:
/// - NSubstitute for all dependencies
/// - Verify notification orchestrator/service calls
/// - Verify correct message formatting and delivery
/// </summary>
[TestFixture]
public class NotificationHandlerTests
{
    private INotificationOrchestrator _mockNotificationOrchestrator = null!;
    private INotificationService _mockNotificationService = null!;
    private IManagedChatsRepository _mockManagedChatsRepository = null!;
    private IBotChatService _mockChatService = null!;
    private IChatCache _mockChatCache = null!;
    private ILogger<NotificationHandler> _mockLogger = null!;
    private NotificationHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _mockNotificationOrchestrator = Substitute.For<INotificationOrchestrator>();
        _mockNotificationService = Substitute.For<INotificationService>();
        _mockManagedChatsRepository = Substitute.For<IManagedChatsRepository>();
        _mockChatService = Substitute.For<IBotChatService>();
        _mockChatCache = Substitute.For<IChatCache>();
        _mockLogger = Substitute.For<ILogger<NotificationHandler>>();

        _handler = new NotificationHandler(
            _mockNotificationOrchestrator,
            _mockNotificationService,
            _mockManagedChatsRepository,
            _mockChatService,
            _mockChatCache,
            _mockLogger);
    }

    /// <summary>
    /// Creates a test ChatAdmin with specified properties.
    /// </summary>
    private static ChatAdmin CreateTestChatAdmin(long chatId, long telegramId)
    {
        return new ChatAdmin
        {
            Id = 1,
            ChatId = chatId,
            User = new UserIdentity(telegramId, "Admin", "User", "test_admin"),
            IsCreator = false,
            PromotedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastVerifiedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Creates a test TelegramUserMappingRecord with specified properties.
    /// </summary>
    #region NotifyUserCriticalViolationAsync Tests

    [Test]
    public async Task NotifyUserCriticalViolationAsync_SendsDmToUser()
    {
        // Arrange
        const long userId = 12345;
        var violations = new List<string> { "Blocked URL detected" };

        _mockNotificationOrchestrator.SendTelegramDmAsync(
                userId,
                Arg.Any<Notification>(),
                Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true));

        // Act
        var result = await _handler.NotifyUserCriticalViolationAsync(UserIdentity.FromId(userId), violations);

        // Assert
        Assert.That(result.Success, Is.True);
        await _mockNotificationOrchestrator.Received(1).SendTelegramDmAsync(
            userId,
            Arg.Is<Notification>(n => n.Type == "critical_violation"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyUserCriticalViolationAsync_IncludesAllViolations()
    {
        // Arrange
        const long userId = 12345;
        var violations = new List<string>
        {
            "Blocked URL detected",
            "Malware signature found",
            "Phishing link blocked"
        };

        _mockNotificationOrchestrator.SendTelegramDmAsync(
                userId,
                Arg.Any<Notification>(),
                Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true));

        // Act
        var result = await _handler.NotifyUserCriticalViolationAsync(UserIdentity.FromId(userId), violations);

        // Assert
        Assert.That(result.Success, Is.True);
        await _mockNotificationOrchestrator.Received(1).SendTelegramDmAsync(
            userId,
            Arg.Is<Notification>(n =>
                n.Message.Contains("Blocked URL detected") &&
                n.Message.Contains("Malware signature found") &&
                n.Message.Contains("Phishing link blocked")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyUserCriticalViolationAsync_DeliveryFails_ReturnsFailedResult()
    {
        // Arrange
        const long userId = 12345;
        var violations = new List<string> { "Blocked URL detected" };

        _mockNotificationOrchestrator.SendTelegramDmAsync(
                userId,
                Arg.Any<Notification>(),
                Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(false, "User has blocked the bot"));

        // Act
        var result = await _handler.NotifyUserCriticalViolationAsync(UserIdentity.FromId(userId), violations);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("blocked"));
        }
    }

    #endregion

    #region NotifyAdminsSpamBanAsync Tests

    [Test]
    public async Task NotifyAdminsSpamBanAsync_DelegatesToNotificationService()
    {
        // Arrange
        var enrichedMessage = CreateTestEnrichedMessage(chatId: 1001, messageId: 2002, userId: 3003);

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(enrichedMessage, chatsAffected: 3, messageDeleted: true);

        // Assert — handler delegates to typed notification service
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Is<ChatIdentity>(c => c.Id == 1001),
            Arg.Is<UserIdentity>(u => u.Id == 3003),
            Arg.Any<Actor?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            3,
            true,
            2002,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsSpamBanAsync_PassesMessagePreview()
    {
        // Arrange
        var enrichedMessage = CreateTestEnrichedMessage(
            chatId: 1001,
            messageId: 2002,
            userId: 3003,
            messageText: "This is spam content that should appear in preview");

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(enrichedMessage, chatsAffected: 1, messageDeleted: true);

        // Assert — message preview is passed to notification service
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Is<string?>(preview => preview != null && preview.Contains("spam content")),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsSpamBanAsync_PassesDetectionDetails()
    {
        // Arrange
        var detection = new DetectionResultRecord
        {
            Id = 1,
            MessageId = 2002,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectionSource = "auto",
            DetectionMethod = "OpenAI",
            IsSpam = true,
            Confidence = 95,
            NetConfidence = 85,
            Reason = "High confidence spam detection",
            AddedBy = Actor.AutoDetection
        };
        var enrichedMessage = CreateTestEnrichedMessage(
            chatId: 1001,
            messageId: 2002,
            userId: 3003,
            latestDetection: detection);

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(enrichedMessage, chatsAffected: 2, messageDeleted: true);

        // Assert — detection confidence values are passed through
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor?>(),
            85, // netConfidence = Math.Abs(85)
            95, // confidence
            Arg.Is<string?>(r => r != null && r.Contains("High confidence")),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsSpamBanAsync_PassesPhotoPath_WhenAvailable()
    {
        // Arrange
        var enrichedMessage = CreateTestEnrichedMessage(
            chatId: 1001,
            messageId: 2002,
            userId: 3003,
            photoLocalPath: "/data/media/photos/spam_photo.jpg");

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(enrichedMessage, chatsAffected: 1, messageDeleted: true);

        // Assert — photo path is passed to notification service
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Arg.Any<Actor?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            "/data/media/photos/spam_photo.jpg",
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    /// <summary>
    /// Creates a test MessageWithDetectionHistory for spam ban notification tests.
    /// </summary>
    private static MessageWithDetectionHistory CreateTestEnrichedMessage(
        long chatId,
        int messageId,
        long userId,
        string? messageText = "Test spam message",
        string? photoLocalPath = null,
        DetectionResultRecord? latestDetection = null)
    {
        var message = new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(userId, "Spam", "User", "spam_user"),
            Chat: new ChatIdentity(chatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: messageText,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: photoLocalPath,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );

        return new MessageWithDetectionHistory
        {
            Message = message,
            DetectionResults = latestDetection != null ? new List<DetectionResultRecord> { latestDetection } : new List<DetectionResultRecord>()
        };
    }
}
