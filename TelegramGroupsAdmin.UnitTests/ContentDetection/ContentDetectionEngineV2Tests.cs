using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Metrics;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Tests for ContentDetectionEngineV2 - SpamAssassin-style additive scoring pipeline.
/// Covers hard block pre-filter, pipeline scoring, AI veto logic, action thresholds,
/// check disabling, guard conditions, and exception isolation.
/// </summary>
[TestFixture]
public class ContentDetectionEngineV2Tests
{
    private ILogger<ContentDetectionEngineV2> _logger = null!;
    private IContentDetectionConfigRepository _configRepository = null!;
    private ISystemConfigRepository _systemConfigRepo = null!;
    private IPromptVersionRepository _promptVersionRepo = null!;
    private IUrlPreFilterService _preFilterService = null!;
    private IOptions<ContentDetectionOptions> _options = null!;

    private static readonly UserIdentity TestUser = UserIdentity.FromId(12345);
    private static readonly ChatIdentity TestChat = ChatIdentity.FromId(67890);

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<ContentDetectionEngineV2>>();
        _configRepository = Substitute.For<IContentDetectionConfigRepository>();
        _systemConfigRepo = Substitute.For<ISystemConfigRepository>();
        _promptVersionRepo = Substitute.For<IPromptVersionRepository>();
        _preFilterService = Substitute.For<IUrlPreFilterService>();
        _options = Options.Create(new ContentDetectionOptions());

        // Default: no hard block
        _preFilterService
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(false, null, null));

        // Default: return a permissive config with all checks disabled, so tests can opt-in selectively
        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(BuildPermissiveConfig());

        // Default: AI infrastructure is disabled
        _systemConfigRepo
            .GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns((AIProviderConfig?)null);

        // Default: no active custom prompt
        _promptVersionRepo
            .GetActiveVersionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((PromptVersion?)null);
    }

    // ---------------------------------------------------------------------------
    // Hard Block Pre-Filter
    // ---------------------------------------------------------------------------

    #region Hard Block Pre-Filter

    [Test]
    public async Task CheckMessageAsync_HardBlockTriggered_ReturnsTotalScoreFiveAndAutoBan()
    {
        // Arrange
        _preFilterService
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Domain on hard block list", "evil.example.com"));

        var engine = BuildEngine([]);
        var request = BuildRequest("Visit http://evil.example.com for free crypto");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSpam, Is.True);
            Assert.That(result.TotalScore, Is.EqualTo(ContentDetectionConstants.MaxScore));
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.AutoBan));
            Assert.That(result.RequiresAIConfirmation, Is.False);
            Assert.That(result.HardBlock, Is.Not.Null);
            Assert.That(result.HardBlock!.ShouldBlock, Is.True);
            Assert.That(result.CheckResults, Has.Count.EqualTo(1));
            Assert.That(result.CheckResults[0].CheckName, Is.EqualTo(CheckName.UrlBlocklist));
            Assert.That(result.CheckResults[0].Score, Is.EqualTo(ContentDetectionConstants.MaxScore));
            Assert.That(result.CheckResults[0].Abstained, Is.False);
        }
    }

    [Test]
    public async Task CheckMessageAsync_HardBlock_EmptyMessage_SkipsPreFilter()
    {
        // Arrange - pre-filter should NOT be called when message is null/whitespace
        var preFilterCalled = false;
        _preFilterService
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                preFilterCalled = true;
                return new HardBlockResult(true, "Should not be reached", null);
            });

        var engine = BuildEngine([]);
        var request = BuildRequest("");  // empty message - URL-only content with no message field

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - pre-filter skipped, pipeline ran with no spam signals
        Assert.That(preFilterCalled, Is.False);
        Assert.That(result.IsSpam, Is.False);
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
        Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.Allow));
    }

    [Test]
    public async Task CheckMessageAsync_NoHardBlock_ContinuesToPipeline()
    {
        // Arrange - hard block does not trigger
        _preFilterService
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(false, null, null));

        var check = BuildCheck(CheckName.StopWords, score: 2.0, abstained: false);
        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Buy cheap stuff now!");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - pipeline ran, HardBlock is null
        Assert.That(result.HardBlock, Is.Null);
        Assert.That(result.TotalScore, Is.EqualTo(2.0));
    }

    #endregion

    // ---------------------------------------------------------------------------
    // Pipeline Scoring
    // ---------------------------------------------------------------------------

    #region Pipeline Scoring

    [Test]
    public async Task CheckMessageAsync_MultipleChecksRun_ScoresSummedCorrectly()
    {
        // Arrange
        var stopWordsCheck = BuildCheck(CheckName.StopWords, score: 2.0, abstained: false);
        var bayesCheck = BuildCheck(CheckName.Bayes, score: 1.5, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.Bayes.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([stopWordsCheck, bayesCheck]);
        var request = BuildRequest("Win big cash prizes today!");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalScore, Is.EqualTo(3.5));
            Assert.That(result.CheckResults, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task CheckMessageAsync_AbstractedCheckDoesNotContributeToScore()
    {
        // Arrange - one check scores, one abstains
        var scoringCheck = BuildCheck(CheckName.StopWords, score: 2.0, abstained: false);
        var abstainedCheck = BuildCheck(CheckName.Bayes, score: 0.0, abstained: true);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.Bayes.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([scoringCheck, abstainedCheck]);
        var request = BuildRequest("Buy now!");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Abstained check score of 0.0 does not add to total
            Assert.That(result.TotalScore, Is.EqualTo(2.0));
            // Both check responses are recorded
            Assert.That(result.CheckResults, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task CheckMessageAsync_AllChecksAbstain_TotalScoreIsZero()
    {
        // Arrange
        var check1 = BuildCheck(CheckName.StopWords, score: 0.0, abstained: true);
        var check2 = BuildCheck(CheckName.Bayes, score: 0.0, abstained: true);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.Bayes.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check1, check2]);
        var request = BuildRequest("Normal message");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalScore, Is.EqualTo(0.0));
            Assert.That(result.IsSpam, Is.False);
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.Allow));
        }
    }

    #endregion

    // ---------------------------------------------------------------------------
    // AI Veto Logic
    // ---------------------------------------------------------------------------

    #region AI Veto - Cleans (Vetoes Spam)

    [Test]
    public async Task CheckMessageAsync_AIVetoClean_OverridesSpamPipeline()
    {
        // Arrange - pipeline returns spam signal, AI returns Score=0 (clean, not abstained)
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.5, abstained: false);
        var aiCheck = BuildAICheck(score: 0.0, abstained: false, details: "Message is legitimate");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Get this amazing deal!");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // AI veto overrides: result is clean
            Assert.That(result.IsSpam, Is.False);
            Assert.That(result.TotalScore, Is.EqualTo(0.0));
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.Allow));
            Assert.That(result.RequiresAIConfirmation, Is.False);
            // Both pipeline check and AI check appear in results
            Assert.That(result.CheckResults, Has.Count.EqualTo(2));
        }
    }

    #endregion

    #region AI Veto - Confirms Spam

    [Test]
    public async Task CheckMessageAsync_AIVetoConfirmsSpam_UsesAIScoreAlone()
    {
        // Arrange - pipeline 3.0 points, AI returns 3.0 → AI score alone determines action
        // With default config (AutoBanThreshold=4.0, ReviewQueueThreshold=2.5), 3.0 → ReviewQueue
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.0, abstained: false);
        var aiCheck = BuildAICheck(score: 3.0, abstained: false, details: "AI confirmed spam");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Spam content here");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSpam, Is.True);
            // AI score replaces pipeline score (not additive)
            Assert.That(result.TotalScore, Is.EqualTo(3.0));
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.ReviewQueue));
            // AI check appended to results
            Assert.That(result.CheckResults, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task CheckMessageAsync_AIVetoConfirmsSpam_ScoreAboveAutoBanThreshold_ReturnAutoBan()
    {
        // Arrange - pipeline 2.0, AI returns 4.5 → AI score alone (4.5 >= AutoBanThreshold 4.0) → AutoBan
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 2.0, abstained: false);
        var aiCheck = BuildAICheck(score: 4.5, abstained: false, details: "Very high confidence spam");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Extreme spam content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // AI score alone, not additive with pipeline
            Assert.That(result.TotalScore, Is.EqualTo(4.5));
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.AutoBan));
        }
    }

    #endregion

    #region AI Veto - Abstained

    [Test]
    public async Task CheckMessageAsync_AIVetoAbstains_DeferstoPipelineVerdict()
    {
        // Arrange - pipeline returns spam, AI abstains (e.g. API timeout)
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.5, abstained: false);
        var aiCheck = BuildAICheck(score: 0.0, abstained: true, details: "API timeout");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Potentially spammy content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Pipeline verdict preserved; AI abstention did not change outcome
            Assert.That(result.TotalScore, Is.EqualTo(3.5));
            Assert.That(result.IsSpam, Is.True);
            // AI check still recorded for visibility
            Assert.That(result.CheckResults, Has.Count.EqualTo(2));
            Assert.That(result.CheckResults.Any(r => r.CheckName == CheckName.OpenAI && r.Abstained), Is.True);
        }
    }

    #endregion

    #region AI Veto - Bypassed

    [Test]
    public async Task CheckMessageAsync_AIVeto_NoPipelineSpam_VetoNotRun()
    {
        // Arrange - pipeline finds nothing (score=0), AI veto must be skipped
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 0.0, abstained: true);
        var aiCheck = BuildAICheck(score: 4.0, abstained: false, details: "Would have flagged spam");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Clean message text");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - AI veto check was never invoked
        await aiCheck.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
        Assert.That(result.IsSpam, Is.False);
    }

    [Test]
    public async Task CheckMessageAsync_AIVeto_InfrastructureDisabled_VetoNotRun()
    {
        // Arrange - pipeline has spam signals but AI connection is disabled
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.5, abstained: false);
        var aiCheck = BuildAICheck(score: 2.0, abstained: false, details: "Would have confirmed spam");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        // AI infrastructure disabled (connection.Enabled = false)
        SetupDisabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Spam content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - AI veto skipped, pipeline result returned as-is
        await aiCheck.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(3.5));
    }

    [Test]
    public async Task CheckMessageAsync_AIVeto_PerChatVetoDisabled_VetoNotRun()
    {
        // Arrange - infrastructure enabled but per-chat AIVeto.Enabled = false
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.5, abstained: false);
        var aiCheck = BuildAICheck(score: 2.0, abstained: false, details: "Would have confirmed spam");

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = false;  // Per-chat veto disabled

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        var engine = BuildEngine([pipelineCheck, aiCheck]);
        var request = BuildRequest("Spam content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - AI veto skipped
        await aiCheck.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(3.5));
    }

    [Test]
    public async Task CheckMessageAsync_AIVeto_NoAICheckRegistered_PipelineResultReturned()
    {
        // Arrange - infrastructure enabled, per-chat enabled, but no IContentCheckV2 with OpenAI name
        var pipelineCheck = BuildCheck(CheckName.StopWords, score: 3.5, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.AIVeto.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        SetupEnabledAIInfrastructure();

        // Only StopWords check registered - no OpenAI check
        var engine = BuildEngine([pipelineCheck]);
        var request = BuildRequest("Spam content");

        // Act - should not throw
        var result = await engine.CheckMessageAsync(request);

        // Assert - pipeline result is returned unchanged
        Assert.That(result.TotalScore, Is.EqualTo(3.5));
        Assert.That(result.CheckResults, Has.Count.EqualTo(1));
    }

    #endregion

    // ---------------------------------------------------------------------------
    // Action Determination (boundary values)
    // ---------------------------------------------------------------------------

    #region Action Determination

    // Pipeline results cap at ReviewQueue — AutoBan requires AI confirmation
    [TestCase(2.4, DetectionAction.Allow, TestName = "Score_2point4_Returns_Allow")]
    [TestCase(2.5, DetectionAction.ReviewQueue, TestName = "Score_2point5_Returns_ReviewQueue")]
    [TestCase(3.9, DetectionAction.ReviewQueue, TestName = "Score_3point9_Returns_ReviewQueue")]
    [TestCase(4.0, DetectionAction.ReviewQueue, TestName = "Score_4point0_Returns_ReviewQueue")]
    [TestCase(5.0, DetectionAction.ReviewQueue, TestName = "Score_5point0_Returns_ReviewQueue")]
    public async Task CheckMessageAsync_ActionDetermination_CorrectActionForScore(double score, DetectionAction expectedAction)
    {
        // Arrange - configure a single check to return the target score
        var check = BuildCheck(CheckName.StopWords, score: score, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Test message for scoring");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert
        Assert.That(result.RecommendedAction, Is.EqualTo(expectedAction));
    }

    [TestCase(2.4, false, TestName = "Score_2point4_IsSpam_False")]
    [TestCase(2.5, true, TestName = "Score_2point5_IsSpam_True")]
    public async Task CheckMessageAsync_IsSpamFlag_SetByReviewThreshold(double score, bool expectedIsSpam)
    {
        // Arrange
        var check = BuildCheck(CheckName.StopWords, score: score, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Test message");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert
        Assert.That(result.IsSpam, Is.EqualTo(expectedIsSpam));
    }

    #endregion

    // ---------------------------------------------------------------------------
    // Check Disabling
    // ---------------------------------------------------------------------------

    #region Check Disabling

    [Test]
    public async Task CheckMessageAsync_DisabledCheck_CheckNotInvoked()
    {
        // Arrange - StopWords check disabled in config
        var check = BuildCheck(CheckName.StopWords, score: 3.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = false;  // explicitly disabled

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Potentially spammy content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - check was never executed
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
        Assert.That(result.CheckResults, Is.Empty);
    }

    [Test]
    public async Task CheckMessageAsync_OneCheckEnabledOneDisabled_OnlyEnabledCheckRuns()
    {
        // Arrange
        var enabledCheck = BuildCheck(CheckName.StopWords, score: 2.5, abstained: false);
        var disabledCheck = BuildCheck(CheckName.Bayes, score: 3.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.Bayes.Enabled = false;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([enabledCheck, disabledCheck]);
        var request = BuildRequest("Spammy content");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Only enabled check ran
            await enabledCheck.Received(1).CheckAsync(Arg.Any<ContentCheckRequestBase>());
            await disabledCheck.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
            Assert.That(result.TotalScore, Is.EqualTo(2.5));
            Assert.That(result.CheckResults, Has.Count.EqualTo(1));
        }
    }

    #endregion

    // ---------------------------------------------------------------------------
    // ShouldRunCheckV2 Guards (data-dependent checks)
    // ---------------------------------------------------------------------------

    #region ShouldRunCheckV2 Guards

    [Test]
    public async Task CheckMessageAsync_ThreatIntelCheck_NoUrls_CheckSkipped()
    {
        // Arrange - ThreatIntel requires request.Urls.Any()
        var check = BuildCheck(CheckName.ThreatIntel, score: 4.5, abstained: false);

        var config = BuildPermissiveConfig();
        config.ThreatIntel.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Text without any URLs", urls: []);  // No URLs

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - ThreatIntel should not run when there are no URLs
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    [Test]
    public async Task CheckMessageAsync_ThreatIntelCheck_WithUrls_CheckRuns()
    {
        // Arrange - ThreatIntel enabled and URLs are present
        var check = BuildCheck(CheckName.ThreatIntel, score: 4.5, abstained: false);
        check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(true);

        var config = BuildPermissiveConfig();
        config.ThreatIntel.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Visit this site", urls: ["http://suspicious.example.com"]);

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - ThreatIntel ran
        await check.Received(1).CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(4.5));
    }

    [Test]
    public async Task CheckMessageAsync_ImageSpamCheck_NoImageData_CheckSkipped()
    {
        // Arrange - ImageSpam requires ImageData or PhotoFileId or PhotoLocalPath
        var check = BuildCheck(CheckName.ImageSpam, score: 4.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.ImageSpam.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        // No image data/file ids
        var request = new ContentCheckRequest
        {
            Message = "Just text, no image",
            User = TestUser,
            Chat = TestChat
        };

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - ImageSpam check skipped
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    [Test]
    public async Task CheckMessageAsync_VideoSpamCheck_NoVideoPath_CheckSkipped()
    {
        // Arrange - VideoSpam requires non-empty VideoLocalPath
        var check = BuildCheck(CheckName.VideoSpam, score: 4.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.VideoSpam.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        // No video path
        var request = new ContentCheckRequest
        {
            Message = "Just text, no video",
            User = TestUser,
            Chat = TestChat
        };

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - VideoSpam check skipped
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    [Test]
    public async Task CheckMessageAsync_ChannelReplyCheck_NotReplyToChannel_CheckSkipped()
    {
        // Arrange - ChannelReply requires Metadata.IsReplyToChannelPost
        var check = BuildCheck(CheckName.ChannelReply, score: 3.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.ChannelReply.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Normal reply message");  // IsReplyToChannelPost defaults to false

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - ChannelReply check skipped
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    [Test]
    public async Task CheckMessageAsync_UrlBlocklistCheck_WithUrls_CheckRuns()
    {
        // Arrange
        var check = BuildCheck(CheckName.UrlBlocklist, score: 2.0, abstained: false);
        check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(true);

        var config = BuildPermissiveConfig();
        config.UrlBlocklist.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Check out this link", urls: ["http://blocklisted.example.com"]);

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - UrlBlocklist check ran
        await check.Received(1).CheckAsync(Arg.Any<ContentCheckRequestBase>());
    }

    [Test]
    public async Task CheckMessageAsync_AlwaysRun_True_TrustedUser_CheckStillRuns()
    {
        // Arrange - Spacing enabled + AlwaysRun, but user is trusted
        var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);
        // ShouldExecute returns false (simulates trusted user skip)
        check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(false);

        var config = BuildPermissiveConfig();
        config.Spacing.Enabled = true;
        config.Spacing.AlwaysRun = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = new ContentCheckRequest
        {
            Message = "test message",
            User = TestUser,
            Chat = TestChat,
            IsUserTrusted = true
        };

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - AlwaysRun bypasses ShouldExecute, so check runs
        await check.Received(1).CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(3.0));
    }

    [Test]
    public async Task CheckMessageAsync_AlwaysRun_False_TrustedUser_CheckSkipped()
    {
        // Arrange - Spacing enabled but NOT AlwaysRun, user is trusted
        var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);
        check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(false);

        var config = BuildPermissiveConfig();
        config.Spacing.Enabled = true;
        config.Spacing.AlwaysRun = false;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = new ContentCheckRequest
        {
            Message = "test message",
            User = TestUser,
            Chat = TestChat,
            IsUserTrusted = true
        };

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - ShouldExecute returned false, AlwaysRun is false, so check is skipped
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    [Test]
    public async Task CheckMessageAsync_AlwaysRun_True_Disabled_CheckStillSkipped()
    {
        // Arrange - Spacing disabled, even with AlwaysRun = true
        var check = BuildCheck(CheckName.Spacing, score: 3.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.Spacing.Enabled = false;
        config.Spacing.AlwaysRun = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("test message");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - Enabled=false short-circuits, AlwaysRun doesn't matter
        await check.DidNotReceive().CheckAsync(Arg.Any<ContentCheckRequestBase>());
        Assert.That(result.TotalScore, Is.EqualTo(0.0));
    }

    #endregion

    // ---------------------------------------------------------------------------
    // Exception Isolation
    // ---------------------------------------------------------------------------

    #region Exception Isolation

    [Test]
    public async Task CheckMessageAsync_OneCheckThrows_OtherChecksStillRun()
    {
        // Arrange - first check throws, second check should still execute
        var throwingCheck = Substitute.For<IContentCheckV2>();
        throwingCheck.CheckName.Returns(CheckName.StopWords);
        throwingCheck.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(true);
        throwingCheck.CheckAsync(Arg.Any<ContentCheckRequestBase>())
            .Returns(new ValueTask<ContentCheckResponseV2>(Task.FromException<ContentCheckResponseV2>(
                new InvalidOperationException("Simulated check failure"))));

        var healthyCheck = BuildCheck(CheckName.Bayes, score: 2.0, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;
        config.Bayes.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([throwingCheck, healthyCheck]);
        var request = BuildRequest("Message to check");

        // Act - should not throw
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            // Failing check excluded from results, healthy check score included
            Assert.That(result.TotalScore, Is.EqualTo(2.0));
            Assert.That(result.CheckResults, Has.Count.EqualTo(1));
            Assert.That(result.CheckResults[0].CheckName, Is.EqualTo(CheckName.Bayes));
        }
    }

    [Test]
    public async Task CheckMessageAsync_AllChecksThrow_ReturnsEmptyResultWithZeroScore()
    {
        // Arrange
        var throwingCheck = Substitute.For<IContentCheckV2>();
        throwingCheck.CheckName.Returns(CheckName.StopWords);
        throwingCheck.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(true);
        throwingCheck.CheckAsync(Arg.Any<ContentCheckRequestBase>())
            .Returns(new ValueTask<ContentCheckResponseV2>(Task.FromException<ContentCheckResponseV2>(
                new InvalidOperationException("Total failure"))));

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([throwingCheck]);
        var request = BuildRequest("Message");

        // Act
        var result = await engine.CheckMessageAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalScore, Is.EqualTo(0.0));
            Assert.That(result.IsSpam, Is.False);
            Assert.That(result.CheckResults, Is.Empty);
            Assert.That(result.RecommendedAction, Is.EqualTo(DetectionAction.Allow));
        }
    }

    [Test]
    public async Task CheckMessageAsync_ConfigRepositoryThrows_UsesDefaultConfig()
    {
        // Arrange - config repo throws, engine should fall back to default config
        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ContentDetectionConfig>(new InvalidOperationException("DB connection failed")));

        var engine = BuildEngine([]);
        var request = BuildRequest("Message with no checks enabled by default");

        // Act - should not throw; engine catches the exception and falls back to default config
        var result = await engine.CheckMessageAsync(request);

        // Assert - default config returns clean (no checks enabled on default ContentDetectionConfig)
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    // ---------------------------------------------------------------------------
    // RequiresAIConfirmation Flag
    // ---------------------------------------------------------------------------

    #region RequiresAIConfirmation Flag

    [Test]
    public async Task CheckMessageAsync_PositiveScore_RequiresAIConfirmationIsTrue()
    {
        // Arrange
        var check = BuildCheck(CheckName.StopWords, score: 1.5, abstained: false);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Mildly suspicious");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert - RequiresAIConfirmation is true whenever TotalScore > 0
        Assert.That(result.RequiresAIConfirmation, Is.True);
    }

    [Test]
    public async Task CheckMessageAsync_ZeroScore_RequiresAIConfirmationIsFalse()
    {
        // Arrange
        var check = BuildCheck(CheckName.StopWords, score: 0.0, abstained: true);

        var config = BuildPermissiveConfig();
        config.StopWords.Enabled = true;

        _configRepository
            .GetEffectiveConfigAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(config);

        var engine = BuildEngine([check]);
        var request = BuildRequest("Clean message");

        // Act
        var result = await engine.CheckMessageAsync(request);

        // Assert
        Assert.That(result.RequiresAIConfirmation, Is.False);
    }

    #endregion

    // ---------------------------------------------------------------------------
    // Private Helpers
    // ---------------------------------------------------------------------------

    #region Helpers

    private ContentDetectionEngineV2 BuildEngine(IEnumerable<IContentCheckV2> checks)
    {
        return new ContentDetectionEngineV2(
            _logger,
            _configRepository,
            _systemConfigRepo,
            _promptVersionRepo,
            checks,
            _preFilterService,
            _options,
            new DetectionMetrics());
    }

    private static ContentCheckRequest BuildRequest(
        string message,
        List<string>? urls = null)
    {
        return new ContentCheckRequest
        {
            Message = message,
            User = TestUser,
            Chat = TestChat,
            Urls = urls ?? []
        };
    }

    /// <summary>
    /// Builds a mock IContentCheckV2 that always returns the given score/abstained values.
    /// ShouldExecute defaults to true so the engine will attempt to run the check.
    /// Note: ShouldExecute is still subject to the ShouldRunCheckV2 guard in the engine
    /// (config.Enabled AND data-dependent guards). Tests that want the check to actually
    /// execute must also enable it in the config.
    /// </summary>
    private static IContentCheckV2 BuildCheck(CheckName name, double score, bool abstained, string? details = null)
    {
        var check = Substitute.For<IContentCheckV2>();
        check.CheckName.Returns(name);
        check.ShouldExecute(Arg.Any<ContentCheckRequest>()).Returns(true);
        check.CheckAsync(Arg.Any<ContentCheckRequestBase>())
            .Returns(new ValueTask<ContentCheckResponseV2>(new ContentCheckResponseV2
            {
                CheckName = name,
                Score = score,
                Abstained = abstained,
                Details = details ?? $"Check {name} result"
            }));
        return check;
    }

    /// <summary>
    /// Builds a mock IContentCheckV2 representing the OpenAI AI veto check.
    /// </summary>
    private static IContentCheckV2 BuildAICheck(double score, bool abstained, string details)
    {
        return BuildCheck(CheckName.OpenAI, score, abstained, details);
    }

    /// <summary>
    /// Builds a ContentDetectionConfig with all checks disabled (opt-in per test).
    /// </summary>
    private static ContentDetectionConfig BuildPermissiveConfig()
    {
        return new ContentDetectionConfig
        {
            StopWords = new StopWordsConfig { Enabled = false },
            Bayes = new BayesConfig { Enabled = false },
            Similarity = new SimilarityConfig { Enabled = false },
            Spacing = new SpacingConfig { Enabled = false },
            InvisibleChars = new InvisibleCharsConfig { Enabled = false },
            ThreatIntel = new ThreatIntelConfig { Enabled = false },
            UrlBlocklist = new UrlBlocklistConfig { Enabled = false },
            ImageSpam = new ImageContentConfig { Enabled = false },
            VideoSpam = new VideoContentConfig { Enabled = false },
            ChannelReply = new ChannelReplyConfig { Enabled = false },
            AIVeto = new AIVetoConfig { Enabled = false }
        };
    }

    /// <summary>
    /// Configures _systemConfigRepo to return an AI provider config with
    /// a single enabled connection mapped to SpamDetection.
    /// </summary>
    private void SetupEnabledAIInfrastructure()
    {
        const string connectionId = "openai";

        var aiConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = connectionId, Enabled = true, Provider = AIProviderType.OpenAI }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = connectionId,
                    Model = "gpt-4o-mini",
                    MaxTokens = 500
                }
            }
        };

        _systemConfigRepo
            .GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(aiConfig);
    }

    /// <summary>
    /// Configures _systemConfigRepo to return an AI provider config where
    /// the connection is disabled (infrastructure kill switch).
    /// </summary>
    private void SetupDisabledAIInfrastructure()
    {
        const string connectionId = "openai";

        var aiConfig = new AIProviderConfig
        {
            Connections =
            [
                new AIConnection { Id = connectionId, Enabled = false, Provider = AIProviderType.OpenAI }
            ],
            Features = new Dictionary<AIFeatureType, AIFeatureConfig>
            {
                [AIFeatureType.SpamDetection] = new AIFeatureConfig
                {
                    ConnectionId = connectionId,
                    Model = "gpt-4o-mini",
                    MaxTokens = 500
                }
            }
        };

        _systemConfigRepo
            .GetAIProviderConfigAsync(Arg.Any<CancellationToken>())
            .Returns(aiConfig);
    }

    #endregion
}
