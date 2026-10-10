using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// The outcome and source tags on the profile-scan metrics. Every defined value has a fixed tag; an
/// unmapped value fails loudly like the other metric mappings (WelcomeMetrics).
/// </summary>
[TestFixture]
public class ProfileScanMetricTagTests
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

    [TestCase(ProfileScanOrigin.ChatEvent, "welcome")]
    [TestCase(ProfileScanOrigin.Rescan, "rescan")]
    [TestCase(ProfileScanOrigin.Manual, "manual")]
    public void OriginToTag_DefinedOrigin_MapsToFixedTag(ProfileScanOrigin origin, string expected)
    {
        Assert.That(ProfileScanService.OriginToTag(origin), Is.EqualTo(expected));
    }

    [Test]
    public void OriginToTag_UnmappedOrigin_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileScanService.OriginToTag((ProfileScanOrigin)99));
    }
}
