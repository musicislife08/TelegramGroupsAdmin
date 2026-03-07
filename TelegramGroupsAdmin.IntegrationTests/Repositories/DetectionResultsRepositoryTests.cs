using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
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
/// - Unique PostgreSQL database per test (test_db_xxx)
/// - GoldenDataset provides 2 detection results (Result1 for Msg1, Result2 for Msg11)
/// </summary>
[TestFixture]
public class DetectionResultsRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IDetectionResultsRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

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

        // Seed golden dataset
        await using var context = _testHelper.GetDbContext();
        await GoldenDataset.SeedAsync(context);
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
        // Arrange — both golden dataset detection results are for MainChat
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;
        int[] messageIds = [GoldenDataset.DetectionResults.Result1_MessageId, GoldenDataset.DetectionResults.Result2_MessageId];

        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(chatId, messageIds);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.ContainsKey(GoldenDataset.DetectionResults.Result1_MessageId), Is.True);
            Assert.That(results.ContainsKey(GoldenDataset.DetectionResults.Result2_MessageId), Is.True);
        }
    }

    [Test]
    public async Task GetDetectionHistoryBatchAsync_WithWrongChatId_ReturnsEmpty()
    {
        // Arrange — use a different chat ID than the golden dataset
        var wrongChatId = 999999L;
        int[] messageIds = [GoldenDataset.DetectionResults.Result1_MessageId, GoldenDataset.DetectionResults.Result2_MessageId];

        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(wrongChatId, messageIds);

        // Assert — same message IDs but wrong chat should yield no results
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task GetDetectionHistoryBatchAsync_WithEmptyMessageIds_ReturnsEmpty()
    {
        // Arrange
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;

        // Act
        var results = await _repository!.GetDetectionHistoryBatchAsync(chatId, []);

        // Assert
        Assert.That(results, Is.Empty);
    }

    #endregion

    #region InvalidateTrainingDataForMessageAsync

    [Test]
    public async Task InvalidateTrainingDataForMessageAsync_SetsUsedForTrainingFalse()
    {
        // Arrange — insert a detection result with used_for_training = true
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;
        var messageId = GoldenDataset.Messages.Msg3_Id; // Use a message that doesn't have a detection result yet

        await using (var context = _testHelper!.GetDbContext())
        {
            context.DetectionResults.Add(new Data.Models.DetectionResultRecordDto
            {
                MessageId = messageId,
                ChatId = chatId,
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
        await _repository!.InvalidateTrainingDataForMessageAsync(messageId, chatId);

        // Assert — verify used_for_training is now false
        await using (var context = _testHelper.GetDbContext())
        {
            var result = await context.DetectionResults
                .FirstAsync(dr => dr.MessageId == messageId && dr.ChatId == chatId);
            Assert.That(result.UsedForTraining, Is.False);
        }
    }

    [Test]
    public async Task InvalidateTrainingDataForMessageAsync_WrongChatId_DoesNotAffectOtherChats()
    {
        // Arrange — insert a detection result with used_for_training = true
        var chatId = GoldenDataset.ManagedChats.MainChat_Id;
        var messageId = GoldenDataset.Messages.Msg3_Id;

        await using (var context = _testHelper!.GetDbContext())
        {
            context.DetectionResults.Add(new Data.Models.DetectionResultRecordDto
            {
                MessageId = messageId,
                ChatId = chatId,
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
        await _repository!.InvalidateTrainingDataForMessageAsync(messageId, 999999L);

        // Assert — original record should still have used_for_training = true
        await using (var context = _testHelper.GetDbContext())
        {
            var result = await context.DetectionResults
                .FirstAsync(dr => dr.MessageId == messageId && dr.ChatId == chatId);
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
