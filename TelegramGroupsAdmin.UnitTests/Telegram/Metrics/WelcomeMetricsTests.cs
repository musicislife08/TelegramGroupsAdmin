using NUnit.Framework;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Metrics;

[TestFixture]
public class WelcomeMetricsTests
{
    [Test]
    public void RecordBypassOutcome_None_ThrowsInvalidOperationException()
    {
        var metrics = new WelcomeMetrics();
        Assert.Throws<InvalidOperationException>(() =>
            metrics.RecordBypassOutcome(BypassDecision.None, 0.0));
    }

    [Test]
    public void RecordBypassOutcome_Admin_DoesNotThrow()
    {
        var metrics = new WelcomeMetrics();
        Assert.DoesNotThrow(() =>
            metrics.RecordBypassOutcome(BypassDecision.Admin, 12.5));
    }

    [Test]
    public void RecordBypassOutcome_Trusted_DoesNotThrow()
    {
        var metrics = new WelcomeMetrics();
        Assert.DoesNotThrow(() =>
            metrics.RecordBypassOutcome(BypassDecision.Trusted, 12.5));
    }
}
