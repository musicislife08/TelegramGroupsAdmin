using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.ContentDetection.Utilities;
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
/// - Training data page: the curated current verdicts (message_verdicts view)
/// - File scans: GoldenDatasetConstants.Verdicts.FileScanRowId (the canonical FileScan row)
/// - OpenAI veto analytics: GoldenDatasetConstants.Verdicts.OpenAIVetoScanRowId (canonical edit 2026-09-28)
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
        Assert.That(detection!.Source, Is.EqualTo((int)VerdictSource.TrainingDataPage));
        Assert.That(detection.Classification, Is.EqualTo((int)VerdictClassification.ExplicitSpam));
        Assert.That(detection.IsSpam, Is.True, "is_spam is generated from classification");
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
    public async Task AddManualTrainingSampleAsync_HamSample_IsExplicitHam()
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
        Assert.That(detection!.Classification, Is.EqualTo((int)VerdictClassification.ExplicitHam));
        Assert.That(detection.IsSpam, Is.False, "is_spam is generated from classification");
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
    public async Task RecordDecisionAsync_ReviewCleanOnUntrainedSpam_BecomesExplicitHam()
    {
        // A review-queued flag (UntrainedSpam, never trained) that an admin marks clean.
        var chatId = GoldenDatasetConstants.Chats.LandOwnersChatId;
        var msgId = GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId;
        Assert.That((await _repository!.GetCurrentVerdictAsync(msgId, chatId))!.Classification,
            Is.EqualTo(VerdictClassification.UntrainedSpam), "canonical precondition");

        await _repository.RecordDecisionAsync(msgId, chatId, VerdictSource.ReviewClean,
            Actor.FromWebUser(GoldenDatasetConstants.WebUsers.OwnerId), "Report #1 - marked clean");

        var after = await _repository.GetCurrentVerdictAsync(msgId, chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after!.Classification, Is.EqualTo(VerdictClassification.ExplicitHam));
            Assert.That(after.Source, Is.EqualTo(VerdictSource.ReviewClean));
        }
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

    #region Training data (TrainingData.razor)

    [Test]
    public async Task GetAllTrainingDataAsync_ReturnsTheCurrentVerdictRowOfEachCuratedMessage()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var curatedVerdictIds = await ctx.MessageVerdicts
            .Where(v => v.VerdictId != null && VerdictClassifications.CuratedValues.Contains(v.Classification))
            .Select(v => v.VerdictId!.Value)
            .ToListAsync();
        Assert.That(curatedVerdictIds, Is.Not.Empty, "canonical precondition");

        var samples = await _repository!.GetAllTrainingDataAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(samples.Select(s => s.Id), Is.EquivalentTo(curatedVerdictIds), "one row per curated message, its current verdict");
            Assert.That(samples.Select(s => s.Classification), Is.All.AnyOf(VerdictClassifications.Curated.Cast<object>().ToArray()));
            Assert.That(samples.Select(s => s.DetectedAt), Is.Ordered.Descending);

            // A later manual ham correction supersedes the scan: the sample is the decision row, not the scan.
            var corrected = samples.Single(s => s.MessageId == GoldenDatasetConstants.Verdicts.CorrectedToHamMsgId
                                                && s.ChatId == MainChatId);
            Assert.That(corrected.Classification, Is.EqualTo(VerdictClassification.ExplicitHam));
            Assert.That(corrected.Source, Is.EqualTo(VerdictSource.LegacyManual));

            // Confident implicit spam is curated; an edit rescan that superseded an admin decision is the sample.
            var editFlip = samples.Single(s => s.MessageId == GoldenDatasetConstants.Verdicts.EditFlipMsgId
                                               && s.ChatId == GoldenDatasetConstants.Verdicts.EditFlipChatId);
            Assert.That(editFlip.Classification, Is.EqualTo(VerdictClassification.ImplicitSpam));
            Assert.That(editFlip.Source, Is.EqualTo(VerdictSource.ContentScan));

            // Untrained, unscanned and FileScan-adjacent messages are not training samples.
            Assert.That(samples.Any(s => s.MessageId == GoldenDatasetConstants.Verdicts.UnscannedMsgId), Is.False);
            Assert.That(samples.Any(s => s.MessageId == GoldenDatasetConstants.Verdicts.UntrainedHamMsgId), Is.False);
            Assert.That(samples.Any(s => s.MessageId == GoldenDatasetConstants.Verdicts.SpamInTrustWindowMsgId), Is.False);
            Assert.That(samples.Any(s => s.MessageId == GoldenDatasetConstants.Verdicts.FileScanBesideScanMsgId), Is.False);
            Assert.That(samples.Any(s => s.Id == GoldenDatasetConstants.Verdicts.FileScanRowId), Is.False);
        }
    }

    [Test]
    public async Task GetTrainingDataStatsAsync_CountsCuratedVerdictsBySpamAndSourceName()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var curated = await ctx.MessageVerdicts
            .Where(v => v.VerdictId != null && VerdictClassifications.CuratedValues.Contains(v.Classification))
            .Select(v => new { v.IsSpam, v.Source })
            .ToListAsync();
        var expectedSpam = curated.Count(v => v.IsSpam);
        var expectedBySource = curated
            .GroupBy(v => ((VerdictSource)v.Source!.Value).ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var stats = await _repository!.GetTrainingDataStatsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.TotalSamples, Is.EqualTo(curated.Count));
            Assert.That(stats.SpamSamples, Is.EqualTo(expectedSpam));
            Assert.That(stats.HamSamples, Is.EqualTo(curated.Count - expectedSpam));
            Assert.That(stats.SpamSamples, Is.GreaterThan(0), "canonical has curated spam");
            Assert.That(stats.HamSamples, Is.GreaterThan(0), "canonical has curated ham");
            Assert.That(stats.SpamPercentage, Is.EqualTo((double)expectedSpam / curated.Count * 100).Within(1e-9));
            Assert.That(stats.SamplesBySource, Is.EquivalentTo(expectedBySource));
            Assert.That(stats.SamplesBySource.Values.Sum(), Is.EqualTo(stats.TotalSamples));
            // Keys are VerdictSource names: curated canonical verdicts come from scans, auto-bans and legacy labels.
            Assert.That(stats.SamplesBySource.Keys, Is.All.AnyOf(Enum.GetNames<VerdictSource>().Cast<object>().ToArray()));
            Assert.That(stats.SamplesBySource.Keys, Is.SupersetOf(new[]
            {
                nameof(VerdictSource.ContentScan), nameof(VerdictSource.AutoBan), nameof(VerdictSource.LegacyManual)
            }));
            // A FileScan row is never a message's verdict, so it can never be a training source.
            Assert.That(stats.SamplesBySource.ContainsKey(nameof(VerdictSource.FileScan)), Is.False);
        }
    }

    #endregion

    #region File scans

    [Test]
    public async Task GetFileScanResultsAsync_ReturnsOnlyFileScanRows()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var fileScanIds = await ctx.DetectionResults
            .Where(d => d.Source == (int)VerdictSource.FileScan)
            .Select(d => d.Id)
            .ToListAsync();
        Assert.That(fileScanIds, Does.Contain(GoldenDatasetConstants.Verdicts.FileScanRowId), "canonical precondition");

        var results = await _repository!.GetFileScanResultsAsync(limit: 1000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Id), Is.EquivalentTo(fileScanIds));
            Assert.That(results.Select(r => r.Source), Is.All.EqualTo(VerdictSource.FileScan));
            var anchor = results.Single(r => r.Id == GoldenDatasetConstants.Verdicts.FileScanRowId);
            Assert.That(anchor.MessageId, Is.EqualTo(GoldenDatasetConstants.Verdicts.FileScanBesideScanMsgId));
            Assert.That(anchor.ChatId, Is.EqualTo(GoldenDatasetConstants.Verdicts.FileScanBesideScanChatId));
            Assert.That(anchor.Classification, Is.EqualTo(VerdictClassification.UntrainedHam), "a clean file scan");
            Assert.That(anchor.DetectionMethod, Is.EqualTo("FileScanningCheck"));
        }
    }

    [Test]
    public async Task GetFileScanResultsAsync_OffsetPastTheEnd_ReturnsEmpty()
    {
        var total = await _repository!.GetFileScanResultsCountAsync();

        var results = await _repository.GetFileScanResultsAsync(limit: 50, offset: total);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task GetFileScanResultsCountAsync_CountsOnlyFileScanRows()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var expected = await ctx.DetectionResults.CountAsync(d => d.Source == (int)VerdictSource.FileScan);
        var allRows = await ctx.DetectionResults.CountAsync();

        var count = await _repository!.GetFileScanResultsCountAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(count, Is.GreaterThanOrEqualTo(1), "canonical has the FileScan anchor");
            Assert.That(count, Is.LessThan(allRows), "scans and decisions are not file scans");
        }
    }

    #endregion

    #region OpenAI veto analytics

    // dr1492 (the FN-pair scan) carries an *abstained* OpenAI check ("API key not configured") over a
    // Bayes spam flag, which the SUT currently also counts as a veto. These tests assert only on the real
    // (non-abstained, Score 0) veto anchor and on algorithms dr1492 does not touch.

    private static readonly DateTimeOffset Always = DateTimeOffset.MinValue;

    [Test]
    public async Task GetOpenAIVetoAnalyticsAsync_CountsTheCanonicalVetoPerOverriddenAlgorithm()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var veto = await ctx.DetectionResults.SingleAsync(d => d.Id == GoldenDatasetConstants.Verdicts.OpenAIVetoScanRowId);
        var vetoChecks = CheckResultsSerializer.Deserialize(veto.CheckResultsJson!);
        using (Assert.EnterMultipleScope())
        {
            // guard the canonical edit
            Assert.That(veto.Source, Is.EqualTo((int)VerdictSource.ContentScan));
            Assert.That(veto.Classification, Is.EqualTo((int)VerdictClassification.ImplicitHam));
            var openAi = vetoChecks.Single(c => c.CheckName == CheckName.OpenAI);
            Assert.That(openAi.Abstained, Is.False);
            Assert.That(openAi.Score, Is.Zero);
        }

        // Detector runs only: admin decisions and file scans are not content scans.
        var jsonRows = await ctx.DetectionResults
            .Where(d => d.Source == (int)VerdictSource.ContentScan && d.CheckResultsJson != null)
            .Select(d => d.CheckResultsJson!).ToListAsync();
        var stopWordsFlags = jsonRows.Sum(json => CheckResultsSerializer.Deserialize(json)
            .Count(c => c.CheckName == CheckName.StopWords && c.IsSpam));

        var analytics = await _repository!.GetOpenAIVetoAnalyticsAsync(Always);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analytics.TotalDetections, Is.EqualTo(jsonRows.Count));
            Assert.That(analytics.VetoedCount, Is.EqualTo(1), "only the canonical veto; an abstained OpenAI check is no veto");
            // StopWords was overridden only by the canonical veto, so its stat is exact.
            var stopWords = analytics.AlgorithmStats.Single(s => s.AlgorithmName == nameof(CheckName.StopWords));
            Assert.That(stopWords.VetoedCount, Is.EqualTo(1));
            Assert.That(stopWords.SpamFlagsCount, Is.EqualTo(stopWordsFlags));
            Assert.That(stopWords.VetoRate, Is.EqualTo((decimal)1 / stopWordsFlags * 100));
            Assert.That(analytics.AlgorithmStats.Select(s => s.AlgorithmName), Does.Contain(nameof(CheckName.Bayes)));
            Assert.That(analytics.AlgorithmStats.Select(s => s.AlgorithmName), Does.Not.Contain(nameof(CheckName.OpenAI)));
            Assert.That(analytics.AlgorithmStats.Select(s => s.VetoRate), Is.Ordered.Descending);
        }
    }

    [Test]
    public async Task GetOpenAIVetoAnalyticsAsync_SinceAfterAllDetections_IsEmpty()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var newest = await ctx.DetectionResults.MaxAsync(d => d.DetectedAt);

        var analytics = await _repository!.GetOpenAIVetoAnalyticsAsync(newest.AddSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analytics.TotalDetections, Is.Zero);
            Assert.That(analytics.VetoedCount, Is.Zero);
            Assert.That(analytics.VetoRate, Is.Zero);
            Assert.That(analytics.AlgorithmStats, Is.Empty);
        }
    }

    [Test]
    public async Task GetRecentVetoedMessagesAsync_AbstainedOpenAI_IsNotAVeto()
    {
        // dr1492 (msg 211184): OpenAI abstained ("API key not configured") beside a spam-flagging check.
        // The engine only vetoes on a non-abstained score of 0, so analytics must not list it.
        var vetoed = await _repository!.GetRecentVetoedMessagesAsync(limit: 1000);

        Assert.That(vetoed.Select(v => v.MessageId), Has.None.EqualTo((int)GoldenDatasetConstants.Analytics.MsgId_FalseNegative));
    }

    [Test]
    public async Task GetRecentVetoedMessagesAsync_SmallLimit_StillFindsOlderVetoes()
    {
        // The canonical veto is older than many non-spam scans; a limit of 1 must still reach it.
        var vetoed = await _repository!.GetRecentVetoedMessagesAsync(limit: 1);

        Assert.That(vetoed.Select(v => v.MessageId), Is.EqualTo(new[] { GoldenDatasetConstants.Verdicts.OpenAIVetoMsgId }));
    }

    [Test]
    public async Task GetRecentVetoedMessagesAsync_ReturnsTheCanonicalVetoWithItsOverriddenChecks()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var message = await ctx.Messages.SingleAsync(m => m.MessageId == GoldenDatasetConstants.Verdicts.OpenAIVetoMsgId
                                                          && m.ChatId == MainChatId);
        var veto = await ctx.DetectionResults.SingleAsync(d => d.Id == GoldenDatasetConstants.Verdicts.OpenAIVetoScanRowId);
        Assert.That(message.MessageText!.Length, Is.GreaterThan(100), "canonical precondition: preview is truncated");

        var vetoed = await _repository!.GetRecentVetoedMessagesAsync(limit: 1000);

        var entry = vetoed.Single(v => v.MessageId == GoldenDatasetConstants.Verdicts.OpenAIVetoMsgId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.DetectedAt, Is.EqualTo(veto.DetectedAt));
            Assert.That(entry.ContentCheckNames, Is.EquivalentTo(new[] { nameof(CheckName.StopWords), nameof(CheckName.Bayes) }));
            Assert.That(entry.OpenAIScore, Is.Zero);
            Assert.That(entry.OpenAIReason, Does.StartWith("AI: Clean - "));
            Assert.That(entry.MessagePreview, Is.EqualTo(message.MessageText[..100] + "..."));
        }
    }

    #endregion
}
