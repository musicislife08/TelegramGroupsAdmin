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
    private IChatAdminsRepository _mockChatAdminsRepo = null!;
    private ITelegramUserMappingRepository _mockUserMappingRepo = null!;
    private IBotDmService _mockDmService = null!;
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
        _mockChatAdminsRepo = Substitute.For<IChatAdminsRepository>();
        _mockUserMappingRepo = Substitute.For<ITelegramUserMappingRepository>();
        _mockDmService = Substitute.For<IBotDmService>();
        _mockChatService = Substitute.For<IBotChatService>();
        _mockChatCache = Substitute.For<IChatCache>();
        _mockLogger = Substitute.For<ILogger<NotificationHandler>>();

        _handler = new NotificationHandler(
            _mockNotificationOrchestrator,
            _mockNotificationService,
            _mockManagedChatsRepo,
            _mockChatAdminsRepo,
            _mockUserMappingRepo,
            _mockDmService,
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
        SetupAdminWithMapping();

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(
            enrichedMessage, chatsAffected: 5, messageDeleted: true);

        // Assert - title should be "Spam Auto-Banned" (auto-ban title)
        Assert.That(result.Success, Is.True);
        await _mockDmService.Received(1).SendDmWithMediaAsync(
            TestAdminTelegramId,
            "spam_banned",
            Arg.Is<string>(s => s.Contains("Spam Auto\\-Banned")),
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
        SetupAdminWithMapping();

        // Act
        var result = await _handler.NotifyAdminsSpamBanAsync(
            enrichedMessage, chatsAffected: 3, messageDeleted: true);

        // Assert - title should include "Banned by <moderator>"
        Assert.That(result.Success, Is.True);
        await _mockDmService.Received(1).SendDmWithMediaAsync(
            TestAdminTelegramId,
            "spam_banned",
            Arg.Is<string>(s => s.Contains("Spam Banned by") && s.Contains("ModeratorJohn")),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private MessageWithDetectionHistory CreateEnrichedMessage(Actor addedBy)
    {
        var message = new MessageRecord(
            MessageId: 456L,
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
            MessageId = 456L,
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

    private void SetupAdminWithMapping()
    {
        // Return one admin for the chat
        var admin = new ChatAdmin
        {
            Id = 1,
            ChatId = TestChatId,
            User = new UserIdentity(TestAdminTelegramId, "Admin", null, "admin")
        };
        _mockChatAdminsRepo.GetChatAdminsAsync(TestChatId, Arg.Any<CancellationToken>())
            .Returns(new List<ChatAdmin> { admin });

        // Admin has linked their account
        var mapping = new TelegramUserMappingRecord(
            Id: 1,
            TelegramId: TestAdminTelegramId,
            TelegramUsername: "admin",
            UserId: "web-user-1",
            LinkedAt: DateTimeOffset.UtcNow.AddDays(-30),
            IsActive: true);
        _mockUserMappingRepo.GetByTelegramIdAsync(TestAdminTelegramId, Arg.Any<CancellationToken>())
            .Returns(mapping);
    }
}
