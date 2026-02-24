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
///     Layer 2 — AI: confidence tiers (>=80 → 4.5, >=40 → 2.5, <40 → 0.0), malformed JSON fallback.
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
    private IStopWordsRepository _stopWordsRepository = null!;
    private IChatService _chatService = null!;
    private ILogger<ProfileScoringEngine> _logger = null!;
    private ProfileScoringEngine _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _urlPreFilter = Substitute.For<IUrlPreFilterService>();
        _stopWordsRepository = Substitute.For<IStopWordsRepository>();
        _chatService = Substitute.For<IChatService>();
        _logger = Substitute.For<ILogger<ProfileScoringEngine>>();

        _sut = new ProfileScoringEngine(_urlPreFilter, _stopWordsRepository, _chatService, _logger);

        // Safe defaults — no block, no stop words, AI disabled
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
            Assert.That(result.AiConfidence, Is.Null);
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.AiConfidence, Is.Null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI returns spam=false
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiReturnsSpamFalse_AiScoreIsZeroAndClean()
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
            .Returns(AiResponse("""{"spam": false, "confidence": 90, "reason": "clean profile"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.AiConfidence, Is.EqualTo(90));
            Assert.That(result.AiReason, Is.EqualTo("clean profile"));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Layer 2: AI confidence tiers (spam=true)
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidence80_AiScoreIsFourPointFive()
    {
        // Arrange — confidence >= 80 → 4.5 AI score → total >= banThreshold
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 80, "reason": "explicit spam", "signals_detected": ["promo_link"]}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(4.5m));
            Assert.That(result.AiConfidence, Is.EqualTo(80));
            Assert.That(result.AiReason, Is.EqualTo("explicit spam"));
            Assert.That(result.AiSignals, Is.EquivalentTo(new[] { "promo_link" }));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Banned));
        }
    }

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidenceExactly80_AiScoreIsFourPointFive()
    {
        // Arrange — boundary test: confidence of exactly 80 must hit the >=80 tier
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 80, "reason": "boundary"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(4.5m));
    }

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidence50_AiScoreIsTwoPointFive()
    {
        // Arrange — confidence >= 40 → 2.5 AI score → HeldForReview
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 50, "reason": "suspicious"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(2.5m));
            Assert.That(result.AiConfidence, Is.EqualTo(50));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.HeldForReview));
        }
    }

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidenceExactly40_AiScoreIsTwoPointFive()
    {
        // Arrange — boundary test: confidence of exactly 40 must hit the >=40 tier
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 40, "reason": "boundary"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(2.5m));
    }

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidence20_AiScoreIsZero()
    {
        // Arrange — confidence < 40 → 0 AI score (minor signals treated as clean)
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 20, "reason": "minor signals"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
        }
    }

    [Test]
    public async Task ScoreAsync_AiSpamTrueConfidenceExactly39_AiScoreIsZero()
    {
        // Arrange — boundary: 39 is one below the >=40 tier, must produce 0
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 39, "reason": "below threshold"}"""));

        // Act
        var result = await _sut.ScoreAsync(profile, [], null, BanThreshold, NotifyThreshold, CancellationToken.None);

        // Assert
        Assert.That(result.AiScore, Is.EqualTo(0.0m));
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AiScore, Is.EqualTo(0.0m));
            Assert.That(result.AiConfidence, Is.Null);
        }
    }

    [Test]
    public async Task ScoreAsync_AiReturnsEmptyJsonObject_AiScoreIsZero()
    {
        // Arrange — valid JSON but missing fields; Spam defaults to false
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
            .Returns(AiResponse("""{"spam": false, "confidence": 10, "reason": "clean"}"""));

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
            .Returns(AiResponse("""{"spam": false, "confidence": 5, "reason": "clean"}"""));

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
            .Returns(AiResponse("""{"spam": false, "confidence": 5, "reason": "clean"}"""));

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
        // Arrange — stop word hit → rule score 1.5, AI adds 0.5 needed to reach exactly 2.0
        // Easier: use AI confidence >=40 → AI score 2.5 alone → total 2.5, which is >= 2.0 < 4.0
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 50, "reason": "suspicious"}"""));

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
        // Arrange — AI returns 4.5 score alone → >= banThreshold 4.0
        var profile = BuildProfile(bio: "Some bio text");
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 95, "reason": "definitive spam"}"""));

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
        // Arrange — blocked URL (3.0) + AI spam confidence 95 (4.5) = 7.5 raw, capped at 5.0
        var profile = BuildProfile(bio: "spam site link");
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(false, null, null)); // No block — ensure AI layer runs

        // The rule score alone (3.0) is below banThreshold (4.0) so AI runs.
        // But to keep the test reliable, let's set rule score below ban threshold, then AI pushes it over.
        // Actually, blocked URL alone = 3.0, no stop words → AI runs, AI returns 4.5 → total = 7.5 → capped at 5.0
        _urlPreFilter
            .CheckHardBlockAsync(Arg.Any<string>(), Arg.Any<ChatIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new HardBlockResult(true, "Blocked", "spam.example"));
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 95, "reason": "definitive spam"}"""));

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
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 95, "reason": "definitive spam"}"""));

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
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": false, "confidence": 30, "reason": "minor flags"}"""));
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
            Assert.That(result.AiConfidence, Is.Null);
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
        _chatService
            .IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>())
            .Returns(true);
        _chatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AiResponse("""{"spam": true, "confidence": 85, "reason": "crypto scam", "signals_detected": ["promo_link", "explicit_content"]}"""));

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
}
