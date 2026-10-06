using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The outcome tag on the profile-scan and explicit-username metrics. Every defined outcome has a
/// fixed tag; an unmapped value fails loudly like the other metric mappings (WelcomeMetrics).
/// </summary>
[TestFixture]
public class ProfileScanOutcomeTagTests
{
    [TestCase(ProfileScanOutcome.Clean, "clean")]
    [TestCase(ProfileScanOutcome.HeldForReview, "held_for_review")]
    [TestCase(ProfileScanOutcome.Banned, "banned")]
    public void OutcomeToTag_DefinedOutcome_MapsToFixedTag(ProfileScanOutcome outcome, string expected)
    {
        Assert.That(ProfileScanService.OutcomeToTag(outcome), Is.EqualTo(expected));
    }

    [Test]
    public void OutcomeToTag_UnmappedOutcome_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileScanService.OutcomeToTag((ProfileScanOutcome)99));
    }
}
