using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;
using static TelegramGroupsAdmin.E2ETests.Tests.Analytics.ContentDetectionAnalyticsExpectations;

namespace TelegramGroupsAdmin.E2ETests.Tests.Analytics;

/// <summary>
/// The Analytics page's Content Detection tab (the default tab) on canonical with five scans re-timed into
/// today, so the UtcNow-relative windows (the System Health "Last 24h" detection rate and the 30-day OpenAI
/// veto analysis) hold data: three spam scans, one ham scan OpenAI abstained on, and the canonical OpenAI
/// veto. Canonical's own timestamps are frozen months before either window. Every number is compared to a
/// value computed from this test's clone after the re-time with the component's predicates
/// (<see cref="ContentDetectionAnalyticsExpectations"/>).
/// </summary>
[TestFixture]
public class ContentDetectionAnalyticsGoldenTests : GoldenE2ETestBase
{
    /// <summary>The canonical OpenAI veto: StopWords and Bayes flagged spam, OpenAI answered clean (ImplicitHam).</summary>
    private const long VetoScanId = GoldenDatasetConstants.Verdicts.OpenAIVetoScanRowId;

    /// <summary>A ham scan Bayes flagged but OpenAI abstained on: in both windows, not a veto.</summary>
    private const long AbstainedHamScanId = GoldenDatasetConstants.Analytics.DrId_FnAuto;

    private static readonly long[] SpamScanIds =
    [
        GoldenDatasetConstants.Analytics.DrId_TodaySpam1,
        GoldenDatasetConstants.Analytics.DrId_TodaySpam2,
        GoldenDatasetConstants.Analytics.DrId_TodaySpam3,
    ];

    /// <summary>
    /// Midnight-anchored offsets into today (see <see cref="TimestampShift"/>): a non-negative offset is at or
    /// after the local midnight that precedes NOW(), so it is inside a trailing 24h window and the 30-day window
    /// whatever the time of day or the server's time zone. The veto is the newest, so the Recent Spam Checks
    /// table lists it first.
    /// </summary>
    private static readonly TimestampShift[] Shifts =
    [
        new(AbstainedHamScanId, TimeSpan.FromMinutes(3)),
        new(SpamScanIds[0], TimeSpan.FromMinutes(5)),
        new(SpamScanIds[1], TimeSpan.FromMinutes(6)),
        new(SpamScanIds[2], TimeSpan.FromMinutes(7)),
        new(VetoScanId, TimeSpan.FromMinutes(8)),
    ];

    private AnalyticsPage _analytics = null!;
    private Ratio _allScans = null!;
    private Ratio _last24hScans = null!;
    private Ratio _stopWords = null!;
    private Ratio _curated = null!;
    private VetoExpectation _veto = null!;
    private int _recentVetoedCount;
    private RecentDetectionExpectation _newestDetection = null!;
    private int _recentSpamCheckRowCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var since30Days = now.AddDays(-30);
        var since24Hours = now.AddDays(-1);

        // Canonical is frozen at its snapshot date: nothing of its own is inside either window, so the
        // windows below are fed by the re-time alone.
        Assert.That(await context.DetectionResults.CountAsync(dr => dr.DetectedAt >= since30Days), Is.Zero,
            "canonical detections must all predate the 30-day window");

        // Guard the anchors' shapes (canonical edit 2026-09-28 for the veto row).
        var anchors = await context.DetectionResults.AsNoTracking()
            .Where(dr => Shifts.Select(s => s.Id).Contains(dr.Id))
            .Select(dr => new { dr.Id, dr.Source, dr.Classification, dr.CheckResultsJson })
            .ToDictionaryAsync(dr => dr.Id);
        Assert.That(anchors.Keys, Is.EquivalentTo(Shifts.Select(s => s.Id)), "every re-timed row must exist");
        Assert.That(anchors.Values.Select(a => a.Source), Is.All.EqualTo((int)VerdictSource.ContentScan), "the anchors are detector scans");
        Assert.That(anchors.Values.Select(a => a.CheckResultsJson), Is.All.Not.Null, "the anchors carry check results");
        foreach (var id in SpamScanIds)
        {
            Assert.That(IsSpamClassification(anchors[id].Classification), $"scan {id} must be spam");
        }

        var veto = anchors[VetoScanId];
        Assert.That(IsSpamClassification(veto.Classification), Is.False, "the veto row is ham");
        Assert.That(IsOpenAIVeto(CheckResultsSerializer.Deserialize(veto.CheckResultsJson!)), "the veto row must carry a non-abstained OpenAI 0 over a spam flag");

        var abstained = anchors[AbstainedHamScanId];
        Assert.That(IsSpamClassification(abstained.Classification), Is.False, "the abstained row is ham");
        Assert.That(IsOpenAIVeto(CheckResultsSerializer.Deserialize(abstained.CheckResultsJson!)), Is.False, "the abstained row is not a veto");

        await GoldenDataset.Mutate(context)
            .ShiftDetectionResultTimestamps(Shifts)
            .ApplyAsync();

        _allScans = await ScanStatsAsync(context);
        _last24hScans = await ScanStatsAsync(context, since24Hours);
        Assert.That(_last24hScans.Total, Is.EqualTo(Shifts.Length), "the re-time must have moved every anchor into the last 24h");
        Assert.That(_last24hScans.Part, Is.EqualTo(SpamScanIds.Length));
        Assert.That(_allScans.Total, Is.GreaterThan(_last24hScans.Total), "the all-time totals must differ from the window");

        _stopWords = await StopWordCountsAsync(context);
        _curated = await CuratedTrainingCountsAsync(context);

        _veto = await VetoAnalyticsAsync(context, since30Days);
        Assert.That(_veto.VetoedCount, Is.EqualTo(1), "exactly the one veto is inside the 30-day window");
        Assert.That(_veto.TotalDetections, Is.EqualTo(Shifts.Length));
        Assert.That(_veto.Algorithms.Keys, Is.EquivalentTo(["StopWords", "Bayes"]), "the veto overrode StopWords and Bayes");

        _recentVetoedCount = await RecentVetoedMessageCountAsync(context);
        Assert.That(_recentVetoedCount, Is.EqualTo(GoldenDatasetConstants.Verdicts.AllVetoScanRowIds.Length), "every canonical veto is listed, unwindowed");

        (_newestDetection, _recentSpamCheckRowCount) = await RecentSpamChecksAsync(context);
        Assert.That(_newestDetection.IsSpam, Is.False, "the newest detection is the re-timed veto, a ham scan");
        Assert.That(_recentSpamCheckRowCount, Is.EqualTo(20), "canonical has more than a page of detections");
    }

    [SetUp]
    public void CreatePageObject() => _analytics = new AnalyticsPage(Page);

    private async Task OpenContentDetectionAsOwnerAsync()
    {
        await LoginAsOwnerAsync();
        await _analytics.NavigateAsync();
        await Expect(_analytics.Tab("Content Detection")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Test]
    public async Task Overview_ShowsTheAllTimeScanTotals()
    {
        await OpenContentDetectionAsOwnerAsync();

        await Expect(_analytics.ContentDetectionHeading("Overview")).ToBeVisibleAsync();
        await Expect(_analytics.OverviewValue("Total Checks")).ToHaveTextAsync(_allScans.Total.ToString());
        await Expect(_analytics.OverviewValue("Spam Detected")).ToHaveTextAsync(_allScans.Part.ToString());
        await Expect(_analytics.OverviewPercentage("Spam Detected")).ToHaveTextAsync($"{_allScans.Percentage:F1}%");
        await Expect(_analytics.OverviewValue("Stop Words Enabled")).ToHaveTextAsync($"{_stopWords.Part} / {_stopWords.Total}");
        await Expect(_analytics.OverviewPercentage("Stop Words Enabled")).ToHaveTextAsync($"{_stopWords.Percentage:F0}%");
        await Expect(_analytics.OverviewValue("Admin-Labeled Training Samples")).ToHaveTextAsync(_curated.Total.ToString());

        await Expect(_analytics.ContentDetectionHeading("Training Data Overview")).ToBeVisibleAsync();
        await Expect(_analytics.ConfirmedTrainingLabelsValue).ToHaveTextAsync(_curated.Total.ToString());
    }

    [Test]
    public async Task SystemHealth_ShowsTheLast24hDetectionRateOfTheReTimedScans()
    {
        await OpenContentDetectionAsOwnerAsync();

        await Expect(_analytics.ContentDetectionHeading("System Health")).ToBeVisibleAsync();
        await Expect(_analytics.HealthCardValue("Detection Rate")).ToHaveTextAsync($"{_last24hScans.Percentage:F1}%");
        await Expect(_analytics.HealthCardValue("Stop Words")).ToHaveTextAsync(_stopWords.Total.ToString());
        await Expect(_analytics.HealthCardValue("Spam Samples")).ToHaveTextAsync(_curated.Part.ToString());
        await Expect(_analytics.HealthCardValue("Training Data")).ToHaveTextAsync(_curated.Total.ToString());
    }

    [Test]
    public async Task VetoAnalysis_CountsTheReTimedVetoWithinThirtyDays()
    {
        await OpenContentDetectionAsOwnerAsync();

        await Expect(_analytics.ContentDetectionHeading("OpenAI False Positive Prevention")).ToBeVisibleAsync();
        await Expect(_analytics.VetoSummaryAlert).ToContainTextAsync(
            $"OpenAI prevented {_veto.VetoedCount} false positives ({_veto.VetoRate:F1}% of detections) in the last 30 days.");

        await Expect(_analytics.AlgorithmVetoRateRows).ToHaveCountAsync(_veto.Algorithms.Count);
        foreach (var (algorithm, flags) in _veto.Algorithms)
        {
            var row = _analytics.AlgorithmVetoRateRow(algorithm);
            await Expect(row.Locator("td").Nth(1)).ToHaveTextAsync(flags.Total.ToString());
            await Expect(row.Locator("td").Nth(2)).ToHaveTextAsync(flags.Part.ToString());
            await Expect(row.Locator("td").Nth(3)).ToHaveTextAsync($"{(decimal)flags.Part / flags.Total * 100:F1}%");
        }

        // The table has rendered, so the empty state's absence is a real check.
        await Expect(_analytics.VetoEmptyAlert).Not.ToBeVisibleAsync();
        await Expect(_analytics.RecentVetoedMessagesHeader(_recentVetoedCount)).ToBeVisibleAsync();
    }

    [Test]
    public async Task RecentSpamChecks_ListsTheNewestDetectionFirst()
    {
        await OpenContentDetectionAsOwnerAsync();

        await Expect(_analytics.ContentDetectionHeading("Recent Spam Checks")).ToBeVisibleAsync();
        await Expect(_analytics.RecentSpamCheckRows).ToHaveCountAsync(_recentSpamCheckRowCount);
        await Expect(_analytics.RecentSpamCheckResult(0)).ToHaveTextAsync(_newestDetection.IsSpam ? "SPAM" : "CLEAN");
        await Expect(_analytics.RecentSpamCheckCell(0, 2)).ToHaveTextAsync(_newestDetection.Score.ToString("F1"));
        await Expect(_analytics.RecentSpamCheckCell(0, 4)).ToHaveTextAsync(_newestDetection.UserId.ToString());
    }
}
