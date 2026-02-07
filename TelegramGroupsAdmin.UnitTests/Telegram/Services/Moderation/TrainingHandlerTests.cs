using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
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
    private const long TestMessageId = 456L;
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
    public async Task CreateSpamSampleAsync_ExistingDetectionRecord_SkipsInsert()
    {
        // Arrange - message exists and already has a detection record (from automated pipeline)
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        var existingDetection = new DetectionResultRecord
        {
            Id = 1,
            MessageId = TestMessageId,
            DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            DetectionSource = "automated",
            DetectionMethod = "TextClassifier",
            Confidence = 95,
            AddedBy = Actor.FromSystem("automated_pipeline"),
            UserId = TestUserId,
            NetConfidence = 95
        };
        _mockDetectionRepo.GetByMessageIdAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(new List<DetectionResultRecord> { existingDetection });

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, executor);

        // Assert - InsertAsync should NOT be called since detection already exists
        await _mockDetectionRepo.DidNotReceive()
            .InsertAsync(Arg.Any<DetectionResultRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_NoExistingDetectionRecord_CreatesRecord()
    {
        // Arrange - message exists but no detection record yet (manual spam marking)
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        _mockDetectionRepo.GetByMessageIdAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(new List<DetectionResultRecord>());

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, executor);

        // Assert - InsertAsync called with correct fields
        await _mockDetectionRepo.Received(1).InsertAsync(
            Arg.Is<DetectionResultRecord>(d =>
                d.MessageId == TestMessageId &&
                d.DetectionSource == "manual" &&
                d.DetectionMethod == "Manual" &&
                d.Confidence == 100 &&
                d.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateSpamSampleAsync_ExistingDetectionRecord_StillCreatesTrainingLabel()
    {
        // Arrange - detection record already exists, but training label should still be created
        var message = CreateTestMessage(messageText: "Buy cheap watches");
        _mockMessageRepo.GetMessageAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        var existingDetection = new DetectionResultRecord
        {
            Id = 1,
            MessageId = TestMessageId,
            DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            DetectionSource = "automated",
            DetectionMethod = "TextClassifier",
            Confidence = 95,
            AddedBy = Actor.FromSystem("automated_pipeline"),
            UserId = TestUserId,
            NetConfidence = 95
        };
        _mockDetectionRepo.GetByMessageIdAsync(TestMessageId, Arg.Any<CancellationToken>())
            .Returns(new List<DetectionResultRecord> { existingDetection });

        _mockImageTrainingRepo.SaveTrainingSampleAsync(
                TestMessageId, true, Arg.Any<Actor>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Actor.FromTelegramUser(12345, "moderator");

        // Act
        await _handler.CreateSpamSampleAsync(TestMessageId, executor);

        // Assert - training label still created even though detection record was skipped
        await _mockTrainingLabelsRepo.Received(1).UpsertLabelAsync(
            TestMessageId,
            TrainingLabel.Spam,
            Arg.Any<long?>(),
            Arg.Is<string>(s => s.Contains("spam")),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());

        // Retraining still triggered
        await _mockJobTriggerService.Received(1).TriggerNowAsync(
            BackgroundJobNames.TextClassifierRetraining,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private static MessageRecord CreateTestMessage(string? messageText = null)
    {
        return new MessageRecord(
            MessageId: TestMessageId,
            UserId: TestUserId,
            UserName: "spammer",
            FirstName: "Spam",
            LastName: "User",
            ChatId: -100123456789L,
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-5),
            MessageText: messageText,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            ChatName: "Test Chat",
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
