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

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation;

/// <summary>
/// Unit tests for NotificationHandler.
/// Validates the dynamic title in spam ban notifications (Bug 4 fix).
/// </summary>
[TestFixture]
public class NotificationHandlerTests
{
    private const long TestAdminTelegramId = 111L;
    private const long TestChatId = -100123456789L;

    private INotificationOrchestrator _mockNotificationOrchestrator = null!;
    private INotificationService _mockNotificationService = null!;
    private IManagedChatsRepository _mockManagedChatsRepo = null!;
    private IBotChatService _mockChatService = null!;
    private IChatCache _mockChatCache = null!;
    private ILogger<NotificationHandler> _mockLogger = null!;

    private NotificationHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockNotificationOrchestrator = Substitute.For<INotificationOrchestrator>();
        _mockNotificationService = Substitute.For<INotificationService>();
        _mockManagedChatsRepo = Substitute.For<IManagedChatsRepository>();
        _mockChatService = Substitute.For<IBotChatService>();
        _mockChatCache = Substitute.For<IChatCache>();
        _mockLogger = Substitute.For<ILogger<NotificationHandler>>();

        _handler = new NotificationHandler(
            _mockNotificationOrchestrator,
            _mockNotificationService,
            _mockManagedChatsRepo,
            _mockChatService,
            _mockChatCache,
            _mockLogger);
    }

    [Test]
    public async Task NotifyAdminsSpamBanAsync_SystemActor_ShowsAutoBannedTitle()
    {
        // Arrange - detection was by the system (automated pipeline)
        var enrichedMessage = CreateEnrichedMessage(
            addedBy: Actor.FromSystem("automated_pipeline"));

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(
            enrichedMessage, chatsAffected: 5, messageDeleted: true);

        // Assert - notification service was called (title contains "Auto" for system actors)
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Arg.Is<Actor?>(a => a != null && a.Type == ActorType.System),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            5,
            true,
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotifyAdminsSpamBanAsync_TelegramUser_ShowsBannedByTitle()
    {
        // Arrange - detection was by a Telegram user (manual spam action)
        var enrichedMessage = CreateEnrichedMessage(
            addedBy: Actor.FromTelegramUser(99999, "ModeratorJohn"));

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(
            enrichedMessage, chatsAffected: 3, messageDeleted: true);

        // Assert - notification service was called with the TelegramUser actor
        Assert.That(result.Success, Is.True);
        await _mockNotificationService.Received(1).SendSpamBanNotificationAsync(
            Arg.Any<ChatIdentity>(),
            Arg.Any<UserIdentity>(),
            Arg.Is<Actor?>(a => a != null && a.Type == ActorType.TelegramUser),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            3,
            true,
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private MessageWithDetectionHistory CreateEnrichedMessage(Actor addedBy)
    {
        var message = new MessageRecord(
            MessageId: 456,
            User: new UserIdentity(789L, "Spam", "User", "spammer"),
            Chat: new ChatIdentity(TestChatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            MessageText: "Buy cheap watches now!",
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
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
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);

        var detection = new DetectionResultRecord
        {
            Id = 1,
            MessageId = 456,
            DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            DetectionSource = "manual",
            DetectionMethod = "Manual",
            Confidence = 100,
            AddedBy = addedBy,
            UserId = 789L,
            NetConfidence = 100,
            Reason = "Marked as spam"
        };

        return new MessageWithDetectionHistory
        {
            Message = message,
            DetectionResults = [detection]
        };
    }

}
