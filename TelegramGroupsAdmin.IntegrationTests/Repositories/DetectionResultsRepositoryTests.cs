using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for DetectionResultsRepository — composite key correctness.
///
/// Covers methods that were updated during the composite PK migration:
/// - GetDetectionHistoryBatchAsync: batch retrieval with chatId filter
/// - InvalidateTrainingDataForMessageAsync: targeted training data invalidation
/// - AddManualTrainingSampleAsync: synthetic ChatId=0 samples with composite FK
///
/// Test Infrastructure:
/// - Unique PostgreSQL database per test (cloned from golden_template)
/// - Canonical dataset provides 376 detection results across multiple chats
///
/// Canonical anchors used:
/// - Batch retrieval: message_id=20465 and message_id=20466 in chat -100055570785509
///   (both have canonical detection_results rows; 20465 is the multi-DR anchor)
/// - Invalidation insert target: message_id=212340 in MainChat -100026957614982
///   (exists in canonical messages with no existing detection_result — clean insert target)
/// </summary>
[TestFixture]
public class DetectionResultsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IDetectionResultsRepository? _repository;

    // Canonical chat that holds message_id=20465 and message_id=20466
    private const long BatchChatId = -100055570785509L;
    private const int BatchMsg1Id = 20465;
    private const int BatchMsg2Id = 20466;

    // MainChat — used for the InvalidateTrainingData tests (insert a fresh DR here)
    private const long MainChatId = -100026957614982L;
    private const int InvalidateTargetMessageId = 212340;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IDetectionResultsRepository, DetectionResultsRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.CreateScope()
            .ServiceProvider.GetRequiredService<IDetectionResultsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region GetDetectionHistoryBatchAsync

    [Test]
    public async Task GetDetectionHistoryBatchAsync_WithCorrectChatId_ReturnsResults()
    {
        // Arrange — both canonical anchors (20465, 20466) have detection_results in BatchChatId
        int[] messageIds = [BatchMsg1Id, BatchMsg2Id];

        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(BatchChatId, messageIds);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.ContainsKey(BatchMsg1Id), Is.True);
            Assert.That(results.ContainsKey(BatchMsg2Id), Is.True);
        }
    }

    [Test]
    public async Task GetDetectionHistoryBatchAsync_WithWrongChatId_ReturnsEmpty()
    {
        // Arrange — use a different chat ID than the canonical anchors live in
        var wrongChatId = 999999L;
        int[] messageIds = [BatchMsg1Id, BatchMsg2Id];

        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(wrongChatId, messageIds);

        // Assert — same message IDs but wrong chat should yield no results
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task GetDetectionHistoryBatchAsync_WithEmptyMessageIds_ReturnsEmpty()
    {
        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(BatchChatId, []);

        // Assert
        Assert.That(results, Is.Empty);
    }

    #endregion

    #region InvalidateTrainingDataForMessageAsync

    [Test]
    public async Task InvalidateTrainingDataForMessageAsync_SetsUsedForTrainingFalse()
    {
        // Arrange — insert a detection result with used_for_training = true on a canonical
        // message that has no existing detection_result (clean insert target)
        await using (var context = _testHelper!.GetDbContext())
        {
            context.DetectionResults.Add(new Data.Models.DetectionResultRecordDto
            {
                MessageId = InvalidateTargetMessageId,
                ChatId = MainChatId,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = "System",
                DetectionMethod = "TestMethod",
                Score = 4.75,
                NetScore = 4.75,
                Reason = "Test detection",
                SystemIdentifier = "test",
                UsedForTraining = true,
                EditVersion = 0
            });
            await context.SaveChangesAsync();
        }

        // Act
        await _repository!.InvalidateTrainingDataForMessageAsync(InvalidateTargetMessageId, MainChatId);

        // Assert — verify used_for_training is now false
        await using (var context = _testHelper.GetDbContext())
        {
            var result = await context.DetectionResults
                .FirstAsync(dr => dr.MessageId == InvalidateTargetMessageId && dr.ChatId == MainChatId);
            Assert.That(result.UsedForTraining, Is.False);
        }
    }

    [Test]
    public async Task InvalidateTrainingDataForMessageAsync_WrongChatId_DoesNotAffectOtherChats()
    {
        // Arrange — insert a detection result with used_for_training = true
        await using (var context = _testHelper!.GetDbContext())
        {
            context.DetectionResults.Add(new Data.Models.DetectionResultRecordDto
            {
                MessageId = InvalidateTargetMessageId,
                ChatId = MainChatId,
                DetectedAt = DateTimeOffset.UtcNow,
                DetectionSource = "System",
                DetectionMethod = "TestMethod",
                Score = 4.75,
                NetScore = 4.75,
                Reason = "Test detection",
                SystemIdentifier = "test",
                UsedForTraining = true,
                EditVersion = 0
            });
            await context.SaveChangesAsync();
        }

        // Act — invalidate with a DIFFERENT chat ID
        await _repository!.InvalidateTrainingDataForMessageAsync(InvalidateTargetMessageId, 999999L);

        // Assert — original record should still have used_for_training = true
        await using (var context = _testHelper.GetDbContext())
        {
            var result = await context.DetectionResults
                .FirstAsync(dr => dr.MessageId == InvalidateTargetMessageId && dr.ChatId == MainChatId);
            Assert.That(result.UsedForTraining, Is.True);
        }
    }

    #endregion

    #region AddManualTrainingSampleAsync

    [Test]
    public async Task AddManualTrainingSampleAsync_CreatesMessageWithChatIdZero()
    {
        // Act — add a manual spam sample
        var resultId = await _repository!.AddManualTrainingSampleAsync(
            messageText: "Buy cheap watches now!!!",
            isSpam: true,
            source: "ManualUI",
            score: 5.0,
            addedBy: "test-admin");

        // Assert — verify the message, detection result, and training label all use ChatId=0
        Assert.That(resultId, Is.GreaterThan(0));

        await using var context = _testHelper!.GetDbContext();

        // Verify message has ChatId=0 and negative MessageId
        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ChatId == 0 && m.MessageId < 0);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.MessageText, Is.EqualTo("Buy cheap watches now!!!"));

        // Verify detection result references the same composite key
        var detection = await context.DetectionResults
            .FirstOrDefaultAsync(dr => dr.MessageId == message.MessageId && dr.ChatId == 0);
        Assert.That(detection, Is.Not.Null);
        Assert.That(detection!.UsedForTraining, Is.True);
        Assert.That(detection.NetScore, Is.EqualTo(5.0)); // Spam → positive

    }

    [Test]
    public async Task AddManualTrainingSampleAsync_WithTranslation_CreatesTranslationWithChatIdZero()
    {
        // Act — add a manual sample with translation
        var resultId = await _repository!.AddManualTrainingSampleAsync(
            messageText: "Купи дешевые часы!!!",
            isSpam: true,
            source: "ManualUI",
            score: 5.0,
            addedBy: "test-admin",
            translatedText: "Buy cheap watches!!!",
            detectedLanguage: "ru");

        // Assert
        Assert.That(resultId, Is.GreaterThan(0));

        await using var context = _testHelper!.GetDbContext();

        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ChatId == 0 && m.MessageId < 0);
        Assert.That(message, Is.Not.Null);

        // Verify translation has ChatId=0 and links to the message
        var translation = await context.MessageTranslations
            .FirstOrDefaultAsync(mt => mt.MessageId == message!.MessageId && mt.ChatId == 0);
        Assert.That(translation, Is.Not.Null);
        Assert.That(translation!.TranslatedText, Is.EqualTo("Buy cheap watches!!!"));
        Assert.That(translation.DetectedLanguage, Is.EqualTo("ru"));
        Assert.That(translation.EditId, Is.Null, "Manual sample translation should use message arc, not edit arc");
    }

    [Test]
    public async Task AddManualTrainingSampleAsync_HamSample_UsesNegativeNetScore()
    {
        // Act — add a ham sample
        await _repository!.AddManualTrainingSampleAsync(
            messageText: "Hello everyone, how's your day going?",
            isSpam: false,
            source: "ManualUI",
            score: 5.0,
            addedBy: "test-admin");

        // Assert
        await using var context = _testHelper!.GetDbContext();

        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ChatId == 0 && m.MessageId < 0);
        Assert.That(message, Is.Not.Null);

        var detection = await context.DetectionResults
            .FirstOrDefaultAsync(dr => dr.MessageId == message!.MessageId && dr.ChatId == 0);
        Assert.That(detection, Is.Not.Null);
        Assert.That(detection!.NetScore, Is.EqualTo(-5.0), "Ham sample should have negative net_score");
    }

    #endregion
}
