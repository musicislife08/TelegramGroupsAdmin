using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Unit tests for ProfileScoringEngine.
///
/// Testing strategy:
/// - All external dependencies are mocked with NSubstitute.
/// - AI feature is disabled by default in SetUp; tests that exercise the AI layer
///   explicitly enable it via the IsFeatureAvailableAsync mock.
/// - No real HTTP, database, or file system access.
/// - Two scoring layers are exercised independently:
///     Layer 1 — rule-based: IsScam/IsFake flags, blocked URLs (+3.0), stop words (+1.5).
///     Layer 2 — AI: direct 0.0-5.0 score passthrough with clamp, nudity flag, malformed JSON fallback.
/// - Outcome thresholds use defaults: banThreshold=4.0, notifyThreshold=2.0.
/// </summary>
[TestFixture]
public class ProfileScoringEngineTests
{
    private const decimal BanThreshold = 4.0m;
    private const decimal NotifyThreshold = 2.0m;

    private static readonly UserIdentity TestUser = UserIdentity.FromId(12345L);
    private static readonly ChatIdentity TestChat = new(67890L, "Test Chat");

    private IUrlPreFilterService _urlPreFilter = null!;
    private IUrlContentScrapingService _urlContentScraping = null!;
    private IStopWordsRepository _stopWordsRepository = null!;
    private IChatService _chatService = null!;
    private ILogger<ProfileScoringEngine> _logger = null!;
    private ProfileScoringEngine _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _urlPreFilter = Substitute.For<IUrlPreFilterService>();
        _urlContentScraping = Substitute.For<IUrlContentScrapingService>();
        _stopWordsRepository = Substitute.For<IStopWordsRepository>();
        _chatService = Substitute.For<IChatService>();
        _logger = Substitute.For<ILogger<ProfileScoringEngine>>();

        _sut = new ProfileScoringEngine(_urlPreFilter, _urlContentScraping, _stopWordsRepository, _chatService, _logger);

        // Safe defaults — no block, no stop words, AI disabled, no URL metadata
        _urlContentScraping
            .ScrapeUrlMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(false, null, null));

        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper: build ProfileData with sensible defaults
    // ═══════════════════════════════════════════════════════════════════════════

    private static ProfileData BuildProfile(
        string? bio = null,
        bool isScam = false,
        bool isFake = false,
        string? personalChannelTitle = null,
        string? personalChannelAbout = null,
        string? pinnedStoryCaptions = null)
    {
        return new ProfileData(
            User: TestUser,
            Chat: TestChat,
            FirstName: "Test",
            LastName: "User",
            Username: "testuser",
            Bio: bio,
            PersonalChannelId: personalChannelTitle is not null ? 999L : null,
            PersonalChannelTitle: personalChannelTitle,
            PersonalChannelAbout: personalChannelAbout,
            HasPinnedStories: pinnedStoryCaptions is not null,
            PinnedStoryCaptions: pinnedStoryCaptions,
            StoryCount: pinnedStoryCaptions is not null ? 1 : 0,
            StoryCaptions: pinnedStoryCaptions is not null ? [pinnedStoryCaptions] : null,
            IsScam: isScam,
            IsFake: isFake,
            IsVerified: false);
    }

    private static ChatCompletionResult AiResponse(string json) =>
        new() { Content = json };

    private void EnableAiWithResponse(string json)
    {
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse(json));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Telegram-flagged accounts
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_IsScamTrue_ReturnsMaxScoreAndBannedWithoutAi()
    {
        // Arrange
        var profile = BuildProfile(bio: "normal bio", isScam: true);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Score, Is.EqualTo(5.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
            Assert.That(result.RuleScore, Is.EqualTo(5.0m));
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
        }

        // Verify AI layer was never invoked
        await _chatService.DidNotReceive().IsFeatureAvailableAsync(Arg.Any<AIFeatureType>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_IsFakeTrue_ReturnsMaxScoreAndBannedWithoutAi()
    {
        // Arrange
        var profile = BuildProfile(bio: "normal bio", isFake: true);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Score, Is.EqualTo(5.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
            Assert.That(result.RuleScore, Is.EqualTo(5.0m));
        }

        await _chatService.DidNotReceive().IsFeatureAvailableAsync(Arg.Any<AIFeatureType>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_IsScamAndIsFakeBothTrue_ReturnsMaxScore()
    {
        // Arrange
        var profile = BuildProfile(isScam: true, isFake: true);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.Score, Is.EqualTo(5.0m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Empty text — no content to evaluate
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_NoTextContent_ReturnsZeroRuleScore()
    {
        // Arrange — no bio, no channel, no story captions
        var profile = BuildProfile();

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuleScore, Is.EqualTo(0.0m));
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.Score, Is.EqualTo(0.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }

        // URL filter and stop words should never be called when there is no text
        await _urlPreFilter.DidNotReceive().CheckHardBlockAsync(
            Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>());
        await _stopWordsRepository.DidNotReceive().GetEnabledStopWordsAsync(Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Blocked URL detection
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_BlockedUrlInBio_AddsThreePointsToRuleScore()
    {
        // Arrange
        var profile = BuildProfile(bio: "Visit my site: https://spam-site.ru");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked domain", "spam-site.ru"));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuleScore, Is.EqualTo(3.0m));
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.Score, Is.EqualTo(3.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
        }
    }

    [Test]
    public async Task ScoreAsync_BlockedUrlInChannelTitle_AddsThreePointsToRuleScore()
    {
        // Arrange
        var profile = BuildProfile(personalChannelTitle: "crypto profits guaranteed");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked domain", "blocked.example"));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(3.0m));
    }

    [Test]
    public async Task ScoreAsync_BlockedUrlInPinnedStoryCaptions_AddsThreePointsToRuleScore()
    {
        // Arrange
        var profile = BuildProfile(pinnedStoryCaptions: "Click my link for free crypto");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked domain", "blocked.example"));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(3.0m));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Stop word detection
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_StopWordMatchInBio_AddsOnePointFiveToRuleScore()
    {
        // Arrange
        var profile = BuildProfile(bio: "Earn crypto fast — guaranteed profits");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["guaranteed profits", "click here"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuleScore, Is.EqualTo(1.5m));
            Assert.That(result.Score, Is.EqualTo(1.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }
    }

    [Test]
    public async Task ScoreAsync_StopWordMatchCaseInsensitive_AddsOnePointFive()
    {
        // Arrange — stop word stored in lowercase, bio has mixed case
        var profile = BuildProfile(bio: "GUARANTEED PROFITS awaiting you");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["guaranteed profits"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(1.5m));
    }

    [Test]
    public async Task ScoreAsync_MultipleStopWordsMatch_AddsOnePointFiveOnce()
    {
        // Arrange — two stop words both present; stop word detection adds flat 1.5 regardless of count
        var profile = BuildProfile(bio: "click here for guaranteed profits now");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["click here", "guaranteed profits"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(1.5m));
    }

    [Test]
    public async Task ScoreAsync_StopWordsListEmpty_DoesNotAddScore()
    {
        // Arrange
        var profile = BuildProfile(bio: "I am totally normal");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(0.0m));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Combined URL block + stop word
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_BlockedUrlAndStopWord_RuleScoreIsFourPointFive()
    {
        // Arrange
        var profile = BuildProfile(bio: "Guaranteed profits at spam-site.ru");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked", "spam-site.ru"));
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["guaranteed profits"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuleScore, Is.EqualTo(4.5m));
            Assert.That(result.Score, Is.EqualTo(4.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 1: Rule score at or above ban threshold skips AI
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_RuleScoreAtBanThreshold_SkipsAiLayer()
    {
        // Arrange — URL block (3.0) + stop word (1.5) = 4.5 >= banThreshold 4.0
        var profile = BuildProfile(bio: "Guaranteed profits at spam-site.ru");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked", "spam-site.ru"));
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["guaranteed profits"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — AI must not have been consulted
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
        await _chatService.DidNotReceive().IsFeatureAvailableAsync(Arg.Any<AIFeatureType>(), Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI feature availability
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiFeatureUnavailable_ReturnsRuleScoreOnly()
    {
        // Arrange — AI returns false (default from SetUp)
        var profile = BuildProfile(bio: "Some bio text");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));

            Assert.That(result.AiReason, Is.Null);
        }

        // Verify no completion was attempted
        await _chatService.DidNotReceive().GetCompletionAsync(
            Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI returns null
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiReturnsNull_AiScoreIsZero()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns((ChatCompletionResult?)null);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI score passthrough (new 0-5 direct scoring)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiReturnsCleanScore_AiScoreIsZero()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 0.0, "reason": "genuine community member", "signals_detected": [], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.AiReason, Is.EqualTo("genuine community member"));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }
    }

    [Test]
    public async Task ScoreAsync_AiReturnsSuspiciousScore_OutcomeIsHeldForReview()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 2.8, "reason": "suggestive photo + name", "signals_detected": ["suggestive_photo", "suggestive_name"], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(2.8m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
        }
    }

    [Test]
    public async Task ScoreAsync_AiReturnsBanScore_OutcomeIsBanned()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 4.5, "reason": "commercial account with product photos", "signals_detected": ["commercial_name", "product_photo"], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(4.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
        }
    }

    [Test]
    public async Task ScoreAsync_AiScoreExceedsFive_ClampedToMaxScore()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 7.5, "reason": "extreme risk", "signals_detected": [], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.AiScore, Is.EqualTo(5.0m));
    }

    [Test]
    public async Task ScoreAsync_AiScoreNegative_ClampedToZero()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": -1.0, "reason": "invalid", "signals_detected": [], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.AiScore, Is.EqualTo(0.0m));
    }

    [Test]
    public async Task ScoreAsync_RuleScorePlusAiScore_Additive()
    {
        // Rule: stop word (+1.5) + AI score 3.0 = 4.5 → ban
        var profile = BuildProfile(bio: "guaranteed profits from my service");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["guaranteed profits"]);
        EnableAiWithResponse("""{"score": 3.0, "reason": "scam promotion", "signals_detected": ["scam_language"], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RuleScore, Is.EqualTo(1.5m));
            Assert.That(result.AiScore, Is.EqualTo(3.0m));
            Assert.That(result.Score, Is.EqualTo(4.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: Nudity flag passthrough
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiReturnsNudityTrue_ContainsNudityIsTrue()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 4.8, "reason": "visible nudity in profile photo", "signals_detected": ["nudity"], "contains_nudity": true}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.ContainsNudity, Is.True);
    }

    [Test]
    public async Task ScoreAsync_AiReturnsNudityFalse_ContainsNudityIsFalse()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 3.5, "reason": "suggestive but not nude", "signals_detected": ["suggestive_photo"], "contains_nudity": false}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        Assert.That(result.ContainsNudity, Is.False);
    }

    [Test]
    public async Task ScoreAsync_NudityFlagIndependentOfScore_LowScoreCanHaveNudity()
    {
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 1.5, "reason": "minimal risk but nudity present", "signals_detected": ["nudity"], "contains_nudity": true}""");

        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ContainsNudity, Is.True);
            Assert.That(result.AiScore, Is.EqualTo(1.5m));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: Malformed / null JSON fallback
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiReturnsMalformedJson_AiScoreIsZeroGracefully()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("not valid json at all!!!"));

        // Act — must not throw
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
    }

    [Test]
    public async Task ScoreAsync_AiReturnsEmptyJsonObject_AiScoreIsZero()
    {
        // Arrange — valid JSON but missing fields; Score defaults to 0
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("{}"));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI call path — no images uses text completion
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_NoImages_UsesTextCompletionNotVision()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 0.0, "reason": "clean", "signals_detected": [], "contains_nudity": false}"""));

        // Act
        await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — text path was used
        await _chatService.Received(1).GetCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());

        // Vision overloads must not have been called
        await _chatService.DidNotReceive().GetVisionCompletionAsync(
            Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<byte[]>(), Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
        await _chatService.DidNotReceive().GetVisionCompletionAsync(
            Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ImageInput>>(),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_SingleImage_UsesSingleVisionCompletion()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        var singleImage = new ImageInput(new byte[] { 0xFF, 0xD8 }, "image/jpeg");

        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetVisionCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<byte[]>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 0.0, "reason": "clean", "signals_detected": [], "contains_nudity": false}"""));

        // Act
        await _sut.ScoreAsync(profile, [singleImage], "Image 1: profile photo", BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — single-image overload
        await _chatService.Received(1).GetVisionCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Any<string>(),
            singleImage.Data, singleImage.MimeType,
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_MultipleImages_UsesMultiImageVisionCompletion()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        var images = new List<ImageInput>
        {
            new(new byte[] { 0xFF, 0xD8 }, "image/jpeg"),
            new(new byte[] { 0x89, 0x50 }, "image/png")
        };

        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetVisionCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ImageInput>>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 0.0, "reason": "clean", "signals_detected": [], "contains_nudity": false}"""));

        // Act
        await _sut.ScoreAsync(profile, images, "Image 1: profile photo, Image 2: story", BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — multi-image overload
        await _chatService.Received(1).GetVisionCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ImageInput>>(l => l.Count == 2),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Outcome determination
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_TotalScoreBelowNotifyThreshold_OutcomeIsClean()
    {
        // Arrange — rule score 0, AI score 0 → total 0.0 < notifyThreshold 2.0
        var profile = BuildProfile(bio: "I am clean");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        Assert.That(result.Score, Is.EqualTo(0.0m));
    }

    [Test]
    public async Task ScoreAsync_TotalScoreAtNotifyThreshold_OutcomeIsHeldForReview()
    {
        // Arrange — AI returns score 2.5 directly → total 2.5, which is >= 2.0 < 4.0
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 2.5, "reason": "suspicious", "signals_detected": [], "contains_nudity": false}""");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Score, Is.EqualTo(2.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
        }
    }

    [Test]
    public async Task ScoreAsync_TotalScoreAtBanThreshold_OutcomeIsBanned()
    {
        // Arrange — AI returns score 4.5 directly → >= banThreshold 4.0
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 4.5, "reason": "definitive spam", "signals_detected": [], "contains_nudity": false}""");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Score, Is.EqualTo(4.5m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Score capping at 5.0
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_RuleScoreExceedsMaxScore_CappedAtFive()
    {
        // Arrange — IsScam returns MaxScore (5.0); make sure cap does not exceed 5.0
        var profile = BuildProfile(isScam: true);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.Score, Is.EqualTo(5.0m));
        Assert.That(result.Score, Is.LessThanOrEqualTo(5.0m));
    }

    [Test]
    public async Task ScoreAsync_CombinedRuleAndAiScoreExceedsFive_CappedAtFive()
    {
        // Arrange — blocked URL (3.0) + AI score 4.5 = 7.5 raw, capped at 5.0
        // Rule score 3.0 < banThreshold 4.0 so AI layer is reached.
        var profile = BuildProfile(bio: "spam site link");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked", "spam.example"));
        EnableAiWithResponse("""{"score": 4.5, "reason": "definitive spam", "signals_detected": [], "contains_nudity": false}""");

        // Note: rule score 3.0 < banThreshold 4.0 so AI layer is reached.
        // Total raw = 3.0 + 4.5 = 7.5, should be capped at 5.0.

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.Score, Is.EqualTo(5.0m));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Custom threshold behaviour
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_CustomHighBanThreshold_ScoreBelowThresholdIsHeldForReview()
    {
        // Arrange — with a high ban threshold of 5.0, AI score 4.5 should become HeldForReview
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 4.5, "reason": "definitive spam", "signals_detected": [], "contains_nudity": false}""");

        // Act — ban threshold raised so 4.5 does not trigger ban
        var result = await _sut.ScoreAsync(profile, [], null,
            banThreshold: 5.0m, notifyThreshold: 2.0m, CancellationToken.None);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
    }

    [Test]
    public async Task ScoreAsync_CustomLowNotifyThreshold_LowScoreTriggersHeldForReview()
    {
        // Arrange — notify threshold 0.5m means any positive score triggers HeldForReview
        var profile = BuildProfile(bio: "slightly suspicious bio");
        EnableAiWithResponse("""{"score": 0.0, "reason": "minor flags", "signals_detected": [], "contains_nudity": false}""");
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["suspicious"]);

        // Act — stop word adds 1.5 which is > notifyThreshold 0.5
        var result = await _sut.ScoreAsync(profile, [], null,
            banThreshold: 4.0m, notifyThreshold: 0.5m, CancellationToken.None);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Text aggregation — all profile text fields contribute to scoring
    // ═══════════════════════════════════════════════════════════════════════════

    [TestCase("stopword bio", null, null, null, Description = "Bio contains stop word")]
    [TestCase(null, "stopword channel title", null, null, Description = "Channel title contains stop word")]
    [TestCase(null, null, "stopword channel about", null, Description = "Channel about contains stop word")]
    [TestCase(null, null, null, "stopword story captions", Description = "Story captions contain stop word")]
    public async Task ScoreAsync_StopWordInAnyTextField_MatchDetected(
        string? bio, string? channelTitle, string? channelAbout, string? storyCaptions)
    {
        // Arrange
        var profile = BuildProfile(
            bio: bio,
            personalChannelTitle: channelTitle,
            personalChannelAbout: channelAbout,
            pinnedStoryCaptions: storyCaptions);
        _stopWordsRepository
            .GetEnabledStopWordsAsync(Arg.Any<CancellationToken>())
            .Returns(["stopword"]);

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.RuleScore, Is.EqualTo(1.5m),
            $"Expected stop word detected in: bio={bio}, channelTitle={channelTitle}, channelAbout={channelAbout}, storyCaptions={storyCaptions}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ScoringResult structure correctness
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_CleanProfile_ReturnsExpectedScoringResultFields()
    {
        // Arrange
        var profile = BuildProfile(bio: "Normal honest bio");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Score, Is.EqualTo(0.0m));
            Assert.That(result.RuleScore, Is.EqualTo(0.0m));
            Assert.That(result.AiScore, Is.EqualTo(0.0m));

            Assert.That(result.AiReason, Is.Null);
            Assert.That(result.AiSignals, Is.Null);
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }
    }

    [Test]
    public async Task ScoreAsync_AiSpamWithSignals_SignalsPopulatedOnResult()
    {
        // Arrange
        var profile = BuildProfile(bio: "Some bio text");
        EnableAiWithResponse("""{"score": 4.5, "reason": "crypto scam", "signals_detected": ["promo_link", "explicit_content"], "contains_nudity": false}""");

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiSignals, Is.Not.Null);
            Assert.That(result.AiSignals, Has.Length.EqualTo(2));
            Assert.That(result.AiSignals, Is.EquivalentTo(new[] { "promo_link", "explicit_content" }));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: URL metadata scraping for AI context
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_UrlMetadataPassedToAiPrompt_WhenScrapingReturnsData()
    {
        // Arrange — bio has a URL, scraping returns metadata
        var profile = BuildProfile(bio: "Check out https://example.com");
        _urlContentScraping
            .ScrapeUrlMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://example.com\nFree Adult Videos - Watch Now");

        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(),
                Arg.Is<string>(prompt => prompt.Contains("url_metadata")),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 4.5, "reason": "adult site URL", "signals_detected": ["adult_url_metadata"], "contains_nudity": false}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — AI was called with a prompt containing URL metadata
        await _chatService.Received(1).GetCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(),
            Arg.Is<string>(prompt => prompt.Contains("url_metadata")),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
        Assert.That(result.AiScore, Is.EqualTo(4.5m));
    }

    [Test]
    public async Task ScoreAsync_UrlScrapingFailure_ContinuesWithoutMetadata()
    {
        // Arrange — scraping throws, scoring should still complete
        var profile = BuildProfile(bio: "Visit https://example.com");
        _urlContentScraping
            .ScrapeUrlMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new HttpRequestException("Connection refused"));

        EnableAiWithResponse("""{"score": 0.0, "reason": "clean", "signals_detected": [], "contains_nudity": false}""");

        // Act — must not throw
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — scoring completed gracefully
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
        Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
    }

    [Test]
    public async Task ScoreAsync_UrlScrapingSkipped_WhenAiFeatureUnavailable()
    {
        // Arrange — AI disabled (default), URL scraping should not be called
        var profile = BuildProfile(bio: "Visit https://example.com");

        // Act
        await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — URL scraping must not be called when AI is unavailable
        await _urlContentScraping.DidNotReceive()
            .ScrapeUrlMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_UrlScrapingSkipped_WhenRuleScoreExceedsBanThreshold()
    {
        // Arrange — Telegram-flagged account hits max score, AI layer skipped entirely
        var profile = BuildProfile(bio: "Check https://example.com", isScam: true);

        // Act
        await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — URL scraping must not be called when rule score already bans
        await _urlContentScraping.DidNotReceive()
            .ScrapeUrlMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScoreAsync_NoUrlMetadata_PromptDoesNotContainUrlMetadataSection()
    {
        // Arrange — bio without URLs, scraping returns null
        var profile = BuildProfile(bio: "Just a normal bio without URLs");

        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(),
                Arg.Is<string>(prompt => !prompt.Contains("url_metadata")),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"score": 0.0, "reason": "clean", "signals_detected": [], "contains_nudity": false}"""));

        // Act
        await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert — prompt without url_metadata section was used
        await _chatService.Received(1).GetCompletionAsync(
            AIFeatureType.ProfileScan, Arg.Any<string>(),
            Arg.Is<string>(prompt => !prompt.Contains("url_metadata")),
            Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>());
    }
}
