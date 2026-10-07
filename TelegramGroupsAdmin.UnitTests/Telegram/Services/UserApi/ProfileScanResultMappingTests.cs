using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The one outcome rule (score against the ban and notify thresholds) and the one mapping from a
/// scoring result onto a scan result, shared by the full scan and the name-only scan.
/// </summary>
[TestFixture]
public class ProfileScanResultMappingTests
{
    [TestCase(4.0, ProfileScanOutcome.Banned)]
    [TestCase(4.5, ProfileScanOutcome.Banned)]
    [TestCase(3.9, ProfileScanOutcome.HeldForReview)]
    [TestCase(2.0, ProfileScanOutcome.HeldForReview)]
    [TestCase(1.9, ProfileScanOutcome.Clean)]
    [TestCase(0.0, ProfileScanOutcome.Clean)]
    public void OutcomeFor_ComparesTheScoreWithBothThresholds(decimal score, ProfileScanOutcome expected)
    {
        Assert.That(ScoringResult.OutcomeFor(score, banThreshold: 4.0m, notifyThreshold: 2.0m), Is.EqualTo(expected));
    }

    [TestCase(ProfileScanSource.FullScan)]
    [TestCase(ProfileScanSource.NameOnly)]
    public void WithScoring_CopiesEveryScoreDerivedField_AndKeepsTheProfileFields(ProfileScanSource source)
    {
        var profile = new ProfileScanResult(7, "bio", 11, "channel", "about", true, "captions",
            IsScam: false, IsFake: true, IsVerified: false, 0m, ProfileScanOutcome.Clean, null, null);
        var scoring = new ScoringResult(4.2m, ProfileScanOutcome.Banned, 1.0m, 3.2m, "reason", ["a", "b"],
            ContainsNudity: true, ExplicitDisplayText: true, PromotionalDisplayText: true);

        var result = profile.WithScoring(scoring, source);

        Assert.That(result, Is.EqualTo(profile with
        {
            Score = 4.2m,
            Outcome = ProfileScanOutcome.Banned,
            AiReason = "reason",
            AiSignalsDetected = scoring.AiSignals,
            ContainsNudity = true,
            ExplicitDisplayText = true,
            PromotionalDisplayText = true,
            Source = source
        }));
    }
}
