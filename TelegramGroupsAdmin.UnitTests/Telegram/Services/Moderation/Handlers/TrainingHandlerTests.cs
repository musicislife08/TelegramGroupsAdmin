using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Unit tests for TrainingHandler - ML training data creation from spam classifications.
///
/// Architecture:
/// - TrainingHandler creates training data when admins mark messages as spam
/// - Dual recording pattern: detection_results (history) + training_labels (ML intent)
/// - Triggers immediate ML.NET text classifier retraining via JobTriggerService
/// - Saves image AND video training samples for vision-based detection
/// - Defensively downloads media if MediaLocalPath is null but a file ID exists
///
/// Test Coverage (12 tests):
/// - CreateSpamSampleAsync with text: Verifies label + retraining trigger + detection result fields
/// - CreateSpamSampleAsync message not found: Logs warning, no action
/// - CreateSpamSampleAsync without text: Skips training label/retraining
/// - CreateSpamSampleAsync with photo: Saves image sample
/// - Actor telegram user ID extraction: Verifies labeled_by_user_id
/// - System actor (auto-detection): Skips detection_result insert, still creates training data
/// - WebUser actor: Inserts detection_result (guards against broadening System skip)
/// - Download-when-missing for image: MediaLocalPath null + MediaFileId → downloads before saving
/// - Download-when-missing for video: MediaLocalPath null + MediaFileId → downloads before saving
/// - No-download-when-present: MediaLocalPath set → no download attempted
/// - Download-failure-graceful: download returns null → still attempts training sample save
/// - Video sample saved: verifies IVideoTrainingSamplesRepository.SaveTrainingSampleAsync called
///
/// Mocking Strategy:
/// - NSubstitute for all dependencies (including ITelegramMediaService)
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
    private IVideoTrainingSamplesRepository _mockVideoRepo = null!;
    private ITelegramMediaService _mockMediaService = null!;
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
        _mockVideoRepo = Substitute.For<IVideoTrainingSamplesRepository>();
        _mockMediaService = Substitute.For<ITelegramMediaService>();
        _mockJobTrigger = Substitute.For<IJobTriggerService>();
        _mockLogger = Substitute.For<ILogger<TrainingHandler>>();

        // Default: video repo returns false (no video)
        _mockVideoRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _handler = new TrainingHandler(
            _mockMessageRepo,
            _mockDetectionRepo,
            _mockTrainingRepo,
            _mockImageRepo,
            _mockVideoRepo,
            _mockMediaService,
            _mockJobTrigger,
            _mockLogger);
    }

    /// <summary>
    /// Creates a test MessageRecord with specified properties.
    /// Fills all required constructor parameters with defaults.
    /// </summary>
    private static MessageRecord CreateTestMessage(
        int messageId,
        long userId,
        long chatId,
        string? messageText = null,
        MediaType? mediaType = null,
        string? mediaLocalPath = null,
        string? mediaFileId = null,
        string? photoFileId = null)
    {
        return new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(userId, "Test", "User", "test_user"),
            Chat: new ChatIdentity(chatId, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: messageText,
            PhotoFileId: photoFileId,
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
            MediaFileId: mediaFileId,
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
        const int messageId = 12345;
        const long userId = 67890;
        var executor = Actor.FromTelegramUser(userId);

        var message = CreateTestMessage(messageId, userId, chatId: 1, messageText: "spam message text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false); // No photo

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Verify detection result created with correct field values
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(dr =>
                dr.MessageId == messageId &&
                dr.DetectionSource == SpamDetectionConstants.ManualDetectionSource &&
                dr.DetectionMethod == SpamDetectionConstants.ManualDetectionMethod &&
                dr.Reason == SpamDetectionConstants.ManualSpamReason &&
                dr.Score == 5.0 &&
                dr.NetScore == 5.0 &&
                dr.UserId == userId &&
                dr.AddedBy == executor &&
                dr.UsedForTraining == false // History only!
            ),
            Arg.Any<CancellationToken>());

        // Assert - Verify training label created (ML training)
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            messageId,
            Arg.Any<long>(),
            TrainingLabel.Spam,
            executor,
            Arg.Any<string>(),
            auditLogId: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - Verify combined classifier retraining job triggered once
        await _mockJobTrigger.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining,
            Arg.Any<object>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageNotFound_LogsWarningAndReturns()
    {
        // Arrange
        const int messageId = 99999;
        var executor = Actor.FromTelegramUser(123);

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Should not create any records
        await _mockDetectionRepo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await _mockTrainingRepo.DidNotReceiveWithAnyArgs().UpsertLabelAsync(
            default, default, default, default!, default, default, default);
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithoutText_SkipsTrainingLabelAndRetraining()
    {
        // Arrange
        const int messageId = 12345;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: 1,
            messageText: null,
            mediaType: MediaType.Document, // Example media type
            mediaLocalPath: "/data/media/document.pdf");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false); // No photo

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Detection result still created
        await _mockDetectionRepo.Received(1).InsertAsync(Arg.Any<DetectionResultRecord>(), Arg.Any<CancellationToken>());

        // Assert - NO training label created (no text)
        await _mockTrainingRepo.DidNotReceiveWithAnyArgs().UpsertLabelAsync(
            default, default, default, default!, default, default, default);

        // Assert - NO retraining triggered (no text training data)
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithPhoto_SavesImageSample()
    {
        // Arrange
        const int messageId = 12345;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: 1,
            messageText: "spam with image");
        // Note: Photos use PhotoFileId/PhotoLocalPath fields in MessageRecord, not MediaType

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(messageId, Arg.Any<long>(), true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Image sample saved
        await _mockImageRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            Arg.Any<long>(),
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

        _mockMessageRepo.GetMessageAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        var executor = Actor.FromTelegramUser(telegramUserId);

        // Act
        await _handler.CreateSpamSampleAsync(12345, ChatIdentity.FromId(-100), executor);

        // Assert - Training label should have correct user ID
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<TrainingLabel>(),
            Arg.Is<Actor>(a => a.GetTelegramUserId() == telegramUserId), // Should extract from Actor
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_SystemActor_SkipsDetectionResultInsert()
    {
        // Arrange — auto-detection already has a detection_result from the pipeline;
        // TrainingHandler should NOT insert a second "manual" entry
        const int messageId = 12345;
        var message = CreateTestMessage(messageId, userId: 123, chatId: 1, messageText: "spam text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.AutoDetection;

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - NO detection result inserted (auto-detection pipeline already created one)
        await _mockDetectionRepo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);

        // Assert - Training label IS still created with auto-detected reason
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            messageId,
            Arg.Any<long>(),
            TrainingLabel.Spam,
            Arg.Is<Actor>(a => a == executor), // System actor has no telegram user ID
            SpamDetectionConstants.AutoDetectedSpamReason,
            auditLogId: null,
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - Combined classifier retraining job IS still triggered
        await _mockJobTrigger.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining,
            Arg.Any<object>(),
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - Image sample IS still attempted
        await _mockImageRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            Arg.Any<long>(),
            isSpam: true,
            Arg.Any<Actor>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_WebUserActor_InsertsDetectionResult()
    {
        // Arrange — web admin marking a message as spam should insert a manual detection result
        const int messageId = 12345;
        var message = CreateTestMessage(messageId, userId: 123, chatId: 1, messageText: "spam text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromWebUser("admin-guid", "admin@example.com");

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Detection result IS inserted (web admin override, not system)
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(dr =>
                dr.MessageId == messageId &&
                dr.DetectionSource == SpamDetectionConstants.ManualDetectionSource &&
                dr.DetectionMethod == SpamDetectionConstants.ManualDetectionMethod &&
                dr.Reason == SpamDetectionConstants.ManualSpamReason &&
                dr.Score == 5.0 &&
                dr.NetScore == 5.0 &&
                dr.UserId == 123 && // message.User.Id
                dr.AddedBy == executor),
            Arg.Any<CancellationToken>());

        // Assert - Training label uses manual reason (not auto-detected)
        await _mockTrainingRepo.Received(1).UpsertLabelAsync(
            messageId,
            Arg.Any<long>(),
            TrainingLabel.Spam,
            Arg.Is<Actor>(a => a == executor), // WebUser has no telegram user ID
            SpamDetectionConstants.ManualSpamReason,
            auditLogId: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion

    #region Defensive Download Tests (BACK-03)

    [Test]
    public async Task CreateSpamSampleAsync_MissingMediaLocalPath_WithMediaFileId_DownloadsBeforeImageSample()
    {
        // Arrange — message has a MediaFileId but MediaLocalPath is null (e.g., download failed at receive time)
        const int messageId = 12345;
        const string fileId = "ABC123media";
        const string downloadedPath = "video_12345_ABC123.mp4";

        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: -100,
            messageText: null,
            mediaType: MediaType.Animation,
            mediaLocalPath: null,        // Missing local path — triggers defensive download
            mediaFileId: fileId);

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockMediaService.DownloadAndSaveMediaAsync(
                fileId,
                MediaType.Animation,
                Arg.Any<string?>(),
                Arg.Any<long>(),
                messageId,
                Arg.Any<CancellationToken>())
            .Returns(downloadedPath);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Download was attempted
        await _mockMediaService.Received(1).DownloadAndSaveMediaAsync(
            fileId,
            MediaType.Animation,
            Arg.Any<string?>(),
            Arg.Any<long>(),
            messageId,
            Arg.Any<CancellationToken>());

        // Assert - DB updated with new local path
        await _mockMessageRepo.Received(1).UpdateMediaLocalPathAsync(
            messageId,
            Arg.Any<long>(),
            downloadedPath,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_MissingMediaLocalPath_WithPhotoFileId_DownloadsBeforeImageSample()
    {
        // Arrange — message has a PhotoFileId but no local path (photo not cached)
        const int messageId = 22222;
        const string photoFileId = "PHOTO_FILE_ID";
        const string downloadedPath = "photo_22222_ABC.jpg";

        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: -100,
            messageText: null,
            mediaType: MediaType.Photo,
            mediaLocalPath: null,        // Missing local path
            mediaFileId: null,
            photoFileId: photoFileId);   // Has photo file ID

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockMediaService.DownloadAndSaveMediaAsync(
                photoFileId,
                MediaType.Photo,
                Arg.Any<string?>(),
                Arg.Any<long>(),
                messageId,
                Arg.Any<CancellationToken>())
            .Returns(downloadedPath);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Download was attempted using PhotoFileId
        await _mockMediaService.Received(1).DownloadAndSaveMediaAsync(
            photoFileId,
            MediaType.Photo,
            Arg.Any<string?>(),
            Arg.Any<long>(),
            messageId,
            Arg.Any<CancellationToken>());

        // Assert - DB updated
        await _mockMessageRepo.Received(1).UpdateMediaLocalPathAsync(
            messageId,
            Arg.Any<long>(),
            downloadedPath,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_ExistingMediaLocalPath_DoesNotAttemptDownload()
    {
        // Arrange — message already has a local path; no download should be attempted
        const int messageId = 33333;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: -100,
            messageText: null,
            mediaType: MediaType.Video,
            mediaLocalPath: "video/existing_33333.mp4",  // Already cached
            mediaFileId: "SOME_FILE_ID");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - No download attempted (file already cached)
        await _mockMediaService.DidNotReceiveWithAnyArgs().DownloadAndSaveMediaAsync(
            default!, default, default, default, default, default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_DownloadFails_StillAttemptsTrainingSampleSave()
    {
        // Arrange — download returns null (e.g., file expired on Telegram servers)
        // Training samples should still be attempted (they will return false gracefully)
        const int messageId = 44444;
        const string fileId = "EXPIRED_FILE_ID";

        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: -100,
            messageText: null,
            mediaType: MediaType.Animation,
            mediaLocalPath: null,   // Missing
            mediaFileId: fileId);

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockMediaService.DownloadAndSaveMediaAsync(
                fileId, Arg.Any<MediaType>(), Arg.Any<string?>(), Arg.Any<long>(),
                messageId, Arg.Any<CancellationToken>())
            .Returns((string?)null); // Download failed

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(123);

        // Act — should NOT throw
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Image sample save was still attempted despite download failure
        await _mockImageRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            Arg.Any<long>(),
            isSpam: true,
            Arg.Any<Actor>(),
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - Video sample save was also attempted
        await _mockVideoRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            Arg.Any<long>(),
            isSpam: true,
            Arg.Any<Actor>(),
            cancellationToken: Arg.Any<CancellationToken>());

        // Assert - DB path NOT updated (download returned null)
        await _mockMessageRepo.DidNotReceiveWithAnyArgs().UpdateMediaLocalPathAsync(
            default, default, default!, default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_VideoMessage_SavesVideoTrainingSample()
    {
        // Arrange — message with a video (already cached)
        const int messageId = 55555;
        var message = CreateTestMessage(
            messageId,
            userId: 123,
            chatId: -100,
            messageText: null,
            mediaType: MediaType.Video,
            mediaLocalPath: "video/video_55555.mp4",
            mediaFileId: "VIDEO_FILE_ID");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageRepo.SaveTrainingSampleAsync(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _mockVideoRepo.SaveTrainingSampleAsync(messageId, Arg.Any<long>(), true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor);

        // Assert - Video training sample was saved
        await _mockVideoRepo.Received(1).SaveTrainingSampleAsync(
            messageId,
            Arg.Any<long>(),
            isSpam: true,
            Arg.Any<Actor>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    #endregion
}
