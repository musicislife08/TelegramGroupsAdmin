using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for DetectionResultsRepository — composite key correctness.
///
/// Covers methods that were updated during the composite PK migration:
/// - GetDetectionHistoryBatchAsync: batch retrieval with chatId filter
/// - AddManualTrainingSampleAsync: synthetic ChatId=0 samples with composite FK
///
/// Test Infrastructure:
/// - Unique PostgreSQL database per test (cloned from golden_template)
/// - Canonical dataset provides 376 detection results across multiple chats
///
/// Canonical anchors used:
/// - Batch retrieval: message_id=20465 and message_id=20466 in chat -100055570785509
///   (both have canonical detection_results rows; 20465 is the multi-DR anchor)
/// - Verdict writes: GoldenDatasetConstants.Verdicts anchors (see IntegrationTests/CLAUDE.md Part 2)
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

    // MainChat
    private const long MainChatId = -100026957614982L;

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

    #region AddManualTrainingSampleAsync

    private static readonly Actor ManualSampleActor = Actor.FromSystem("test-admin");

    [Test]
    public async Task AddManualTrainingSampleAsync_CreatesMessageWithChatIdZero()
    {
        // Act — add a manual spam sample
        var resultId = await _repository!.AddManualTrainingSampleAsync(
            messageText: "Buy cheap watches now!!!",
            isSpam: true,
            actor: ManualSampleActor);

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
            actor: ManualSampleActor,
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
            actor: ManualSampleActor);

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

    #region Verdict writes

    // A canonical message with no detection rows. (212340 was the brief's choice, but canonical
    // gives it a LegacyManual row, so the dedicated unscanned anchor is used instead.)
    private const int EmptyTargetMessageId = GoldenDatasetConstants.Verdicts.UnscannedMsgId;

    [Test]
    public async Task RecordScanAsync_AIReviewBelowThreshold_StoresUntrainedHam_AndViewAgrees()
    {
        var before = await _repository!.GetCurrentVerdictAsync(EmptyTargetMessageId, MainChatId);
        Assert.That(before!.Classification, Is.EqualTo(VerdictClassification.Unscanned), "canonical precondition");

        var scan = new ContentDetectionResult
        {
            IsSpam = false,
            TotalScore = 2.0,
            PrimaryReason = "AI below review threshold",
            CheckResults =
            [
                new ContentCheckResponseV2 { CheckName = CheckName.Similarity, Score = 3.5, Abstained = false, Details = "sim" },
                new ContentCheckResponseV2 { CheckName = CheckName.OpenAI, Score = 2.0, Abstained = false, Details = "AI: Review" }
            ]
        };

        var record = await _repository.RecordScanAsync(EmptyTargetMessageId, MainChatId, scan, editVersion: 0);
        var verdict = await _repository.GetCurrentVerdictAsync(EmptyTargetMessageId, MainChatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Source, Is.EqualTo(VerdictSource.ContentScan));
            Assert.That(record.Classification, Is.EqualTo(VerdictClassification.UntrainedHam));
            Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.UntrainedHam));
            Assert.That(verdict.IsSpam, Is.False);
        }
    }

    [Test]
    public async Task RecordDecisionAsync_WebMarkHamOnAutoBannedMessage_ViewBecomesExplicitHam()
    {
        var chatId = GoldenDatasetConstants.Chats.LandOwnersChatId;
        var msgId = GoldenDatasetConstants.Verdicts.MarkAsHamSubjectMsgId;
        var before = await _repository!.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(before!.Classification, Is.EqualTo(VerdictClassification.ExplicitSpam), "canonical precondition");

        await _repository.RecordDecisionAsync(msgId, chatId,
            VerdictSource.WebMarkHam, Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId), "false positive");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.Classification, Is.EqualTo(VerdictClassification.ExplicitHam));
    }

    [Test]
    public async Task RecordDecisionAsync_DismissAfterMarkAsHam_AppendsAndViewShowsLatest()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId;
        Assert.That((await _repository!.GetCurrentVerdictAsync(msgId, chatId))!.Classification,
            Is.EqualTo(VerdictClassification.ExplicitHam), "canonical precondition");

        await _repository.RecordDecisionAsync(msgId, chatId, VerdictSource.ReviewDismiss,
            Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId), "Report dismissed");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.Classification, Is.EqualTo(VerdictClassification.ImplicitHam));
    }

    [Test]
    public async Task RecordFileScanAsync_DoesNotChangeTheMessageVerdict()
    {
        var chatId = GoldenDatasetConstants.Chats.MainChatId;
        var msgId = GoldenDatasetConstants.Verdicts.AutoBanMsgId;
        var before = await _repository!.GetCurrentVerdictAsync(msgId, chatId);

        await _repository.RecordFileScanAsync(msgId, chatId, infected: true, score: 5.0, details: "Malware");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        Assert.That(after!.VerdictId, Is.EqualTo(before!.VerdictId));
    }

    [Test]
    public async Task GetCurrentVerdictAsync_MessageWithoutRows_IsUnscanned()
    {
        var verdict = await _repository!.GetCurrentVerdictAsync(
            GoldenDatasetConstants.Verdicts.UnscannedMsgId, GoldenDatasetConstants.Chats.MainChatId);
        Assert.That(verdict!.Classification, Is.EqualTo(VerdictClassification.Unscanned));
    }

    #endregion

    #region GetRecentMessageVerdictsForUserAsync

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_OneMessageEditedManyTimes_CountsOnce()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(9468093502025L, limit: 10); // @financerope
        Assert.That(rows.Count(r => r.MessageId == GoldenDatasetConstants.Verdicts.EditFlipMsgId), Is.EqualTo(1));
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_EditFlippedToSpam_IsJudgedOnLatestVersion()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(9468093502025L, limit: 10);
        Assert.That(rows.Single(r => r.MessageId == GoldenDatasetConstants.Verdicts.EditFlipMsgId).IsSpam, Is.True);
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_SpamInWindow_IsReturned()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(GoldenDatasetConstants.Verdicts.SpamInTrustWindowUserId, limit: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows.Count(r => r.IsSpam), Is.EqualTo(1));
            Assert.That(rows.Single(r => r.IsSpam).MessageId, Is.EqualTo(GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId));
        }
    }

    [Test]
    public async Task GetRecentMessageVerdictsForUserAsync_AllHamUser_ReturnsThreeHam()
    {
        var rows = await _repository!.GetRecentMessageVerdictsForUserAsync(GoldenDatasetConstants.Verdicts.AllHamUserId, limit: 3);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows.Any(r => r.IsSpam), Is.False);
        }
    }

    #endregion
}
