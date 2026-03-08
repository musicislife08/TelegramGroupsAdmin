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

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Moderation;

/// <summary>
/// Unit tests for TrainingHandler.
/// Validates duplicate detection guard (Bug 1+2 fix) and training label creation.
/// </summary>
[TestFixture]
public class TrainingHandlerTests
{
    private const int TestMessageId = 456;
    private const long TestUserId = 789L;

    private IMessageHistoryRepository _mockMessageRepo = null!;
    private IDetectionResultsRepository _mockDetectionRepo = null!;
    private ITrainingLabelsRepository _mockTrainingLabelsRepo = null!;
    private IImageTrainingSamplesRepository _mockImageTrainingRepo = null!;
    private IJobTriggerService _mockJobTriggerService = null!;
    private ILogger<TrainingHandler> _mockLogger = null!;

    private TrainingHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _mockMessageRepo = Substitute.For<IMessageHistoryRepository>();
        _mockDetectionRepo = Substitute.For<IDetectionResultsRepository>();
        _mockTrainingLabelsRepo = Substitute.For<ITrainingLabelsRepository>();
        _mockImageTrainingRepo = Substitute.For<IImageTrainingSamplesRepository>();
        _mockJobTriggerService = Substitute.For<IJobTriggerService>();
        _mockLogger = Substitute.For<ILogger<TrainingHandler>>();

        _handler = new TrainingHandler(
            _mockMessageRepo,
            _mockDetectionRepo,
            _mockTrainingLabelsRepo,
            _mockImageTrainingRepo,
            _mockJobTriggerService,
            _mockLogger);
    }

    [Test]
    public async Task CreateSpamSampleAsync_ExistingDetectionRecord_StillCreatesManualResult()
    {
        // Arrange - message exists and already has a detection record (from automated pipeline)
        // Manual result should ALWAYS be created for the detection history timeline
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, Arg.Any<long>(), true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, ChatIdentity.FromId(-100), executor);

        // Assert - InsertAsync IS called (manual result alongside existing auto result)
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(d =>
                d.MessageId == TestMessageId &&
                d.DetectionSource == "manual" &&
                d.DetectionMethod == "Manual" &&
                d.Score == 5.0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_NoExistingDetectionRecord_CreatesRecord()
    {
        // Arrange - message exists but no detection record yet (manual spam marking)
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, Arg.Any<long>(), true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, ChatIdentity.FromId(-100), executor);

        // Assert - InsertAsync called with correct fields
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(d =>
                d.MessageId == TestMessageId &&
                d.DetectionSource == "manual" &&
                d.DetectionMethod == "Manual" &&
                d.Score == 5.0 &&
                d.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_AlwaysCreatesTrainingLabel()
    {
        // Arrange - training label should always be created alongside detection result
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(message);

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, Arg.Any<long>(), true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, ChatIdentity.FromId(-100), executor);

        // Assert - training label created alongside detection result
        await _mockTrainingLabelsRepo.Received(1).UpsertLabelAsync(
            TestMessageId,
            Arg.Any<long>(),
            TrainingLabel.Spam,
            Arg.Any<long?>(),
            Arg.Is<string>(s => s.Contains("spam")),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());

        // Retraining still triggered (both text and Bayes classifiers)
        await _mockJobTriggerService.Received(1).TriggerNowAsync(
            BackgroundJobNames.TextClassifierRetraining,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await _mockJobTriggerService.Received(1).TriggerNowAsync(
            BackgroundJobNames.BayesClassifierRetraining,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private static MessageRecord CreateTestMessage(string? messageText = null)
    {
        return new MessageRecord(
            MessageId: TestMessageId,
            User: new UserIdentity(TestUserId, "Spam", "User", "spammer"),
            Chat: new ChatIdentity(-100123456789L, "Test Chat"),
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
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
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped);
    }
}
