using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;
using static TelegramGroupsAdmin.E2ETests.Tests.Analytics.ContentDetectionAnalyticsExpectations;

namespace TelegramGroupsAdmin.E2ETests.Tests.Analytics;

/// <summary>
/// The Content Detection tab on canonical as it is: every detection predates the UtcNow-relative windows, so the
/// "Last 24h" detection rate is 0.0% and the 30-day OpenAI veto analysis renders its empty state, while the
/// all-time figures and the unwindowed "Recent Vetoed Messages" count still show canonical's data.
/// </summary>
[TestFixture]
public class ContentDetectionAnalyticsEmptyWindowGoldenTests : GoldenE2ETestBase
{
    private AnalyticsPage _analytics = null!;
    private Ratio _allScans = null!;
    private int _recentVetoedCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var since30Days = DateTimeOffset.UtcNow.AddDays(-30);
        Assert.That(await context.DetectionResults.CountAsync(dr => dr.DetectedAt >= since30Days), Is.Zero,
            "canonical detections must all predate the 30-day window, so both windows are empty");

        _allScans = await ScanStatsAsync(context);
        Assert.That(_allScans.Part, Is.GreaterThan(0), "canonical holds spam scans, so the all-time figures are non-zero");

        _recentVetoedCount = await RecentVetoedMessageCountAsync(context);
        Assert.That(_recentVetoedCount, Is.EqualTo(GoldenDatasetConstants.Verdicts.AllVetoScanRowIds.Length), "every canonical veto is listed, unwindowed");
    }

    [SetUp]
    public async Task OpenContentDetectionAsOwner()
    {
        _analytics = new AnalyticsPage(Page);
        await LoginAsOwnerAsync();
        await _analytics.NavigateAsync();
        await Expect(_analytics.Tab("Content Detection")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Test]
    public async Task SystemHealth_DetectionRateIsZeroWithNoScansInTheLast24h()
    {
        await Expect(_analytics.ContentDetectionHeading("System Health")).ToBeVisibleAsync();
        await Expect(_analytics.HealthCardValue("Detection Rate")).ToHaveTextAsync("0.0%");

        // The all-time Overview keeps canonical's figures: only the window is empty.
        await Expect(_analytics.OverviewValue("Spam Detected")).ToHaveTextAsync(_allScans.Part.ToString());
    }

    [Test]
    public async Task VetoAnalysis_ShowsTheEmptyStateWithNoScansInThirtyDays()
    {
        await Expect(_analytics.ContentDetectionHeading("OpenAI False Positive Prevention")).ToBeVisibleAsync();
        await Expect(_analytics.VetoSummaryAlert).ToContainTextAsync(
            "OpenAI prevented 0 false positives (0.0% of detections) in the last 30 days.");
        await Expect(_analytics.VetoEmptyAlert).ToBeVisibleAsync();

        // The empty state has rendered, so the table's absence is a real check.
        await Expect(_analytics.AlgorithmVetoRatesTable).ToHaveCountAsync(0);
        await Expect(_analytics.RecentVetoedMessagesHeader(_recentVetoedCount)).ToBeVisibleAsync();
    }
}
