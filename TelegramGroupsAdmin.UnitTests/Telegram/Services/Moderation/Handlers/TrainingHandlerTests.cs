using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
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
/// - Every decision is a verdict event (RecordDecisionAsync); classifiers train on message_verdicts
/// - Triggers immediate ML.NET text classifier retraining via JobTriggerService
/// - Defensively downloads media if MediaLocalPath is null but a file ID exists
///
/// Test Coverage (14 tests):
/// - CreateSpamSampleAsync with text: Verifies recorded decision + retraining trigger
/// - CreateSpamSampleAsync message not found: Logs warning, no action
/// - CreateSpamSampleAsync without text: Records the decision, skips retraining
/// - Actor telegram user ID extraction: The decision carries the Telegram user actor
/// - System actor (auto-ban): Records an AutoBan decision, still triggers retraining
/// - WebUser actor: Records a WebMarkSpam decision
/// - CreateHamSampleAsync: WebMarkHam records decision + retraining; ReviewDismiss records decision; missing message skips everything; other sources throw
/// - Download-when-missing for animation: MediaLocalPath null + MediaFileId → downloads (startup backfill hashes it)
/// - Download-when-missing for photo: MediaLocalPath null + PhotoFileId → downloads (startup backfill hashes it)
/// - No-download-when-present: MediaLocalPath set → no download attempted
/// - Download-failure-graceful: download returns null → decision still recorded, path untouched
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
    private ITelegramMediaService _mockMediaService = null!;
    private IJobTriggerService _mockJobTrigger = null!;
    private ILogger<TrainingHandler> _mockLogger = null!;
    private TrainingHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _mockMessageRepo = Substitute.For<IMessageHistoryRepository>();
        _mockDetectionRepo = Substitute.For<IDetectionResultsRepository>();
        _mockMediaService = Substitute.For<ITelegramMediaService>();
        _mockJobTrigger = Substitute.For<IJobTriggerService>();
        _mockLogger = Substitute.For<ILogger<TrainingHandler>>();

        _handler = new TrainingHandler(
            _mockMessageRepo,
            _mockDetectionRepo,
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
    public async Task CreateSpamSampleAsync_MessageWithText_RecordsDecisionAndTriggersRetraining()
    {
        // Arrange
        const int messageId = 12345;
        const long userId = 67890;
        var executor = Actor.FromTelegramUser(userId);

        var message = CreateTestMessage(messageId, userId, chatId: 1, messageText: "spam message text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - Verify the spam decision is recorded as a verdict event
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            messageId, -100, VerdictSource.WebMarkSpam, executor, Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());

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
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - Should not create any records
        await _mockDetectionRepo.DidNotReceiveWithAnyArgs().RecordDecisionAsync(default, default, default, default!, default!, default, default, default);
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_MessageWithoutText_SkipsRetraining()
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

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - Decision still recorded
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            messageId, -100, VerdictSource.WebMarkSpam, executor, Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());

        // Assert - NO retraining triggered (no text training data)
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(
            string.Empty, new object(), default);
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
        await _handler.CreateSpamSampleAsync(12345, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - The decision carries the Telegram user as its actor
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            12345, -100, VerdictSource.WebMarkSpam,
            Arg.Is<Actor>(a => a!.GetTelegramUserId() == telegramUserId),
            Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_SystemActor_RecordsAutoBanDecision()
    {
        // Arrange — auto-bans record an explicit AutoBan decision like every other executor
        const int messageId = 12345;
        var message = CreateTestMessage(messageId, userId: 123, chatId: 1, messageText: "spam text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        var executor = Actor.AutoDetection;

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.AutoBan, "Auto-ban: high confidence spam");

        // Assert - AutoBan decision recorded
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            messageId, -100, VerdictSource.AutoBan, executor, "Auto-ban: high confidence spam", null, null, Arg.Any<CancellationToken>());

        // Assert - Combined classifier retraining job IS still triggered
        await _mockJobTrigger.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining,
            Arg.Any<object>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_WebUserActor_RecordsWebMarkSpamDecision()
    {
        // Arrange — web admin marking a message as spam records a WebMarkSpam decision
        const int messageId = 12345;
        var message = CreateTestMessage(messageId, userId: 123, chatId: 1, messageText: "spam text");

        _mockMessageRepo.GetMessageAsync(messageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        var executor = Actor.FromWebUser("admin-guid", "admin@example.com");

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - WebMarkSpam decision recorded
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            messageId, -100, VerdictSource.WebMarkSpam, executor, Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());
    }

    #endregion

    #region CreateHamSampleAsync Tests

    [Test]
    public async Task CreateHamSampleAsync_WebMarkHam_RecordsDecisionAndTriggersRetraining()
    {
        var executor = Actor.FromWebUser("admin-1");
        _mockMessageRepo.GetMessageAsync(4242, -100, Arg.Any<CancellationToken>())
            .Returns(CreateTestMessage(4242, userId: 123, chatId: -100, messageText: "hello"));

        await _handler.CreateHamSampleAsync(4242, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkHam, "false positive");

        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            4242, -100, VerdictSource.WebMarkHam, executor, "false positive", null, null, Arg.Any<CancellationToken>());
        await _mockJobTrigger.Received(1).TriggerNowAsync(
            BackgroundJobNames.ClassifierRetraining, Arg.Any<object>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateHamSampleAsync_ReviewDismiss_RecordsImplicitHamDecision()
    {
        var executor = Actor.FromWebUser("admin-1");
        _mockMessageRepo.GetMessageAsync(4242, -100, Arg.Any<CancellationToken>())
            .Returns(CreateTestMessage(4242, userId: 123, chatId: -100, messageText: "hello"));

        await _handler.CreateHamSampleAsync(4242, ChatIdentity.FromId(-100), executor, VerdictSource.ReviewDismiss, "Report #1 dismissed");

        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            4242, -100, VerdictSource.ReviewDismiss, executor, "Report #1 dismissed", null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateHamSampleAsync_MessageNotFound_SkipsDecisionAndRetraining()
    {
        _mockMessageRepo.GetMessageAsync(4242, -100, Arg.Any<CancellationToken>())
            .Returns((MessageRecord?)null);

        await _handler.CreateHamSampleAsync(4242, ChatIdentity.FromId(-100), Actor.FromWebUser("admin-1"),
            VerdictSource.ReviewDismiss, "Report #1 dismissed");

        await _mockDetectionRepo.DidNotReceiveWithAnyArgs().RecordDecisionAsync(default, default, default, default!, default!, default, default, default);
        await _mockJobTrigger.DidNotReceiveWithAnyArgs().TriggerNowAsync(string.Empty, new object(), default);
    }

    [Test]
    public void CreateHamSampleAsync_NonHamSource_Throws()
        => Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _handler.CreateHamSampleAsync(1, ChatIdentity.FromId(-100), Actor.AutoDetection, VerdictSource.WebMarkSpam, "x"));

    #endregion

    #region Defensive Download Tests (BACK-03)

    [Test]
    public async Task CreateSpamSampleAsync_MissingMediaLocalPath_WithMediaFileId_DownloadsMedia()
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

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

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
    public async Task CreateSpamSampleAsync_MissingMediaLocalPath_WithPhotoFileId_DownloadsMedia()
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

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

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

        var executor = Actor.FromTelegramUser(123);

        // Act
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - No download attempted (file already cached)
        await _mockMediaService.DidNotReceiveWithAnyArgs().DownloadAndSaveMediaAsync(
            default!, default, default, default, default, default);
    }

    [Test]
    public async Task CreateSpamSampleAsync_DownloadFails_StillRecordsDecision()
    {
        // Arrange — download returns null (e.g., file expired on Telegram servers)
        // The decision is recorded regardless; the message simply stays without media
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

        var executor = Actor.FromTelegramUser(123);

        // Act — should NOT throw
        await _handler.CreateSpamSampleAsync(messageId, ChatIdentity.FromId(-100), executor, VerdictSource.WebMarkSpam, "Marked as spam by moderator");

        // Assert - Decision recorded despite download failure
        await _mockDetectionRepo.Received(1).RecordDecisionAsync(
            messageId, -100, VerdictSource.WebMarkSpam, executor, Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());

        // Assert - DB path NOT updated (download returned null)
        await _mockMessageRepo.DidNotReceiveWithAnyArgs().UpdateMediaLocalPathAsync(
            default, default, default!, default);
    }

    #endregion
}
