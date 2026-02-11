using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Unit tests for TrainingHandler - ML training data creation from spam classifications.
///
/// Architecture:
/// - TrainingHandler creates training data when admins mark messages as spam
/// - Dual recording pattern: detection_results (history) + training_labels (ML intent)
/// - Triggers immediate ML.NET text classifier retraining via JobTriggerService
/// - Saves image training samples for vision-based detection
///
/// Test Coverage (5 tests):
/// - CreateSpamSampleAsync with text: Verifies label + retraining trigger
/// - CreateSpamSampleAsync message not found: Logs warning, no action
/// - CreateSpamSampleAsync without text: Skips training label/retraining
/// - CreateSpamSampleAsync with photo: Saves image sample
/// - Actor telegram user ID extraction: Verifies labeled_by_user_id
///
/// Mocking Strategy:
/// - NSubstitute for all dependencies
/// - Verify repository calls with Arg.Is<T> matchers
/// - Verify job trigger called with correct parameters
/// </summary>
[TestFixture]
public class TrainingHandlerTests
{
    private IMessageHistoryRepository _mockMessageRepo = null!;
    private IDetectionResultsRepository _mockDetectionRepo = null!;
    private ITrainingLabelsRepository _mockTrainingRepo = null!;
    private IImageTrainingSamplesRepository _mockImageRepo = null!;
    private IJobTriggerService _mockJobTrigger = null!;
    private ILogger<TrainingHandler> _mockLogger = null!;
    private TrainingHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _mockMessageRepo = Substitute.For<IMessageHistoryRepository>();
        _mockDetectionRepo = Substitute.For<IDetectionResultsRepository>();
        _mockTrainingRepo = Substitute.For<ITrainingLabelsRepository>();
        _mockImageRepo = Substitute.For<IImageTrainingSamplesRepository>();
        _mockJobTrigger = Substitute.For<IJobTriggerService>();
        _mockLogger = Substitute.For<ILogger<TrainingHandler>>();

        // Default: no existing detection records (Bug 1+2 guard passes through)
        _mockDetectionRepo.GetByMessageIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<DetectionResultRecord>());

        _handler = new TrainingHandler(
            _mockMessageRepo,
            _mockDetectionRepo,
            _mockTrainingRepo,
            _mockImageRepo,
            _mockJobTrigger,
            _mockLogger);
    }

    /// <summary>
    /// Creates a test MessageRecord with specified properties.
    /// Fills all required constructor parameters with defaults.
    /// </summary>
    private static MessageRecord CreateTestMessage(
        long messageId,
        long userId,
        long chatId,
        string? messageText = null,
        MediaType? mediaType = null,
        string? mediaLocalPath = null)
    {
        return new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(userId, "Test", "User", "test_user"),
            Chat: new ChatIdentity(chatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: messageText,
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
            MediaType: mediaType,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: mediaLocalPath,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
        );
    }

    #region CreateSpamSampleAsync Tests

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithText_CreatesLabelAndTriggersRetraining()
    {
        // Arrange
        const long messageId = 12345;
        const long userId = 67890;
        var executor = Actor.FromTelegramUser(userId);

        var message = CreateTestMessage(messageId, userId, chatId: 1, messageText: "spam message text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false); // No photo

        // Act
        await _handler.CreateSpamSampleAsync(messageId, executor);

        // Assert - Verify detection result created (history only)
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(dr =>
                dr.MessageId == messageId &&
                dr.UsedForTraining == false // History only!
            ),
            Arg.Any<CancellationToken>());

        // Assert - Verify training label created (ML training)
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            messageId,
            TrainingLabel.Spam,
            userId,
            Arg.Any<string>(),
            auditLogId: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - Verify retraining job triggered
        await _mockJobTrigger.Received(1).TriggerNowAsync(
            BackgroundJobNames.TextClassifierRetraining,
            Arg.Any<object>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageNotFound_LogsWarningAndReturns()
    {
        // Arrange
        const long messageId = 99999;
        var executor = Actor.FromTelegramUser(123);

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, executor);

        // Assert - Should not create any records
        await _mockDetectionRepo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await _mockTrainingRepo.DidNotReceiveWithAnyArgs().UpsertLabelAsync(
            default, default, default, default, default, default);
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithoutText_SkipsTrainingLabelAndRetraining()
    {
        // Arrange
        const long messageId = 12345;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: 1,
            messageText: null,
            mediaType: MediaType.Document, // Example media type
            mediaLocalPath: "/data/media/document.pdf");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false); // No photo

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, executor);

        // Assert - Detection result still created
        await _mockDetectionRepo.Received(1).InsertAsync(Arg.Any<DetectionResultRecord>(), Arg.Any<CancellationToken>());

        // Assert - NO training label created (no text)
        await _mockTrainingRepo.DidNotReceiveWithAnyArgs().UpsertLabelAsync(
            default, default, default, default, default, default);

        // Assert - NO retraining triggered (no text training data)
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithPhoto_SavesImageSample()
    {
        // Arrange
        const long messageId = 12345;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: 1,
            messageText: "spam with image");
        // Note: Photos use PhotoFileId/PhotoLocalPath fields in MessageRecord, not MediaType

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(messageId, true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, executor);

        // Assert - Image sample saved
        await _mockImageRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            isSpam: true,
            Arg.Any<Actor>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_TelegramUserActor_ExtractsUserIdCorrectly()
    {
        // Arrange
        const long telegramUserId = 999888;
        var message = CreateTestMessage(12345, userId: 123, chatId: 1, messageText: "test");

        _mockMessageRepo.GetMessageAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        var executor = Actor.FromTelegramUser(telegramUserId);

        // Act
        await _handler.CreateSpamSampleAsync(12345, executor);

        // Assert - Training label should have correct user ID
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            Arg.Any<long>(),
            Arg.Any<TrainingLabel>(),
            telegramUserId, // Should extract from Actor
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
