using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.E2ETests.Tests.Analytics;

/// <summary>
/// What the Content Detection analytics tab should show, computed from a test's clone with the same
/// predicates as the component's readers (MessageStatsRepository, StopWordsRepository and
/// DetectionResultsRepository's veto analytics), mirrored here rather than called: detector statistics
/// are ContentScan rows, spam is the Spam classification set, the 24h and 30-day windows are
/// UtcNow-relative, and a veto is a non-abstained OpenAI score of 0 over another check's spam flag.
/// </summary>
internal static class ContentDetectionAnalyticsExpectations
{
    /// <summary>The spam detection stats of the ContentScan rows, all of them or those detected at or after <paramref name="since"/>.</summary>
    public static async Task<Ratio> ScanStatsAsync(AppDbContext context, DateTimeOffset? since = null)
    {
        var classifications = await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.Source == (int)VerdictSource.ContentScan)
            .Where(dr => since == null || dr.DetectedAt >= since)
            .Select(dr => dr.Classification)
            .ToListAsync();
        return new Ratio(classifications.Count, classifications.Count(IsSpamClassification));
    }

    /// <summary>All stop words and the enabled ones.</summary>
    public static async Task<Ratio> StopWordCountsAsync(AppDbContext context)
    {
        var total = await context.StopWords.AsNoTracking().CountAsync();
        var enabled = await context.StopWords.AsNoTracking().CountAsync(sw => sw.Enabled);
        return new Ratio(total, enabled);
    }

    /// <summary>The curated training set (explicit labels plus confident implicit spam) and its spam part.</summary>
    public static async Task<Ratio> CuratedTrainingCountsAsync(AppDbContext context)
    {
        var curated = context.MessageVerdicts.AsNoTracking()
            .Where(v => VerdictClassifications.CuratedValues.Contains(v.Classification));
        return new Ratio(await curated.CountAsync(), await curated.CountAsync(v => v.IsSpam));
    }

    /// <summary>The OpenAI veto analytics of the ContentScan rows with check results detected at or after <paramref name="since"/>.</summary>
    public static async Task<VetoExpectation> VetoAnalyticsAsync(AppDbContext context, DateTimeOffset since)
    {
        var scans = await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.DetectedAt >= since
                && dr.Source == (int)VerdictSource.ContentScan
                && dr.CheckResultsJson != null)
            .Select(dr => new { dr.Classification, dr.CheckResultsJson })
            .ToListAsync();

        var checksPerScan = scans
            .Select(s => (IsSpam: IsSpamClassification(s.Classification), Checks: CheckResultsSerializer.Deserialize(s.CheckResultsJson!)))
            .ToList();

        var vetoed = checksPerScan.Where(s => !s.IsSpam && IsOpenAIVeto(s.Checks)).ToList();

        var algorithms = vetoed
            .SelectMany(s => OverriddenAlgorithms(s.Checks))
            .GroupBy(name => name)
            .ToDictionary(
                g => g.Key,
                g => new Ratio(
                    Total: checksPerScan.Count(s => OverriddenAlgorithms(s.Checks).Contains(g.Key)),
                    Part: g.Count()));

        return new VetoExpectation(scans.Count, vetoed.Count, algorithms);
    }

    /// <summary>
    /// Every stored veto (no window), as the "Recent Vetoed Messages" panel counts them: non-spam ContentScan
    /// rows with check results whose message exists in the same chat, capped at the component's limit of 50.
    /// </summary>
    public static async Task<int> RecentVetoedMessageCountAsync(AppDbContext context)
    {
        var candidates = await context.DetectionResults.AsNoTracking()
            .Where(dr => dr.Source == (int)VerdictSource.ContentScan
                && !VerdictClassifications.SpamValues.Contains(dr.Classification)
                && dr.CheckResultsJson != null)
            .Join(context.Messages, dr => new { dr.MessageId, dr.ChatId }, m => new { m.MessageId, m.ChatId }, (dr, m) => dr.CheckResultsJson!)
            .ToListAsync();
        return Math.Min(50, candidates.Count(json => IsOpenAIVeto(CheckResultsSerializer.Deserialize(json))));
    }

    /// <summary>
    /// The newest detection the Recent Spam Checks table lists (enriched_detections: every detection joined to
    /// its message by message and chat, newest detected_at first), and how many rows the table shows (at most 20).
    /// </summary>
    public static async Task<(RecentDetectionExpectation Newest, int RowCount)> RecentSpamChecksAsync(AppDbContext context)
    {
        var enriched = context.DetectionResults.AsNoTracking()
            .Join(context.Messages, dr => new { dr.MessageId, dr.ChatId }, m => new { m.MessageId, m.ChatId },
                (dr, m) => new { dr.DetectedAt, dr.Classification, dr.Score, m.UserId });
        var newest = await enriched.OrderByDescending(d => d.DetectedAt).FirstAsync();
        var rowCount = Math.Min(20, await enriched.CountAsync());
        return (new RecentDetectionExpectation(newest.UserId, IsSpamClassification(newest.Classification), newest.Score), rowCount);
    }

    public static bool IsSpamClassification(int classification) => VerdictClassifications.SpamValues.Contains(classification);

    /// <summary>A non-abstained OpenAI score of 0 while another check flagged spam.</summary>
    public static bool IsOpenAIVeto(IReadOnlyList<CheckResult> checks)
        => checks.FirstOrDefault(c => c.CheckName == CheckName.OpenAI) is { Abstained: false, Score: 0 }
            && checks.Any(c => c.CheckName != CheckName.OpenAI && c.IsSpam);

    private static IEnumerable<string> OverriddenAlgorithms(IReadOnlyList<CheckResult> checks)
        => checks.Where(c => c.CheckName != CheckName.OpenAI && c.IsSpam).Select(c => c.CheckName.ToString());

    /// <summary>A count and the part of it that satisfies a predicate (spam of all scans, enabled of all stop words, …).</summary>
    public sealed record Ratio(int Total, int Part)
    {
        /// <summary>Part of Total as a percentage, 0 for an empty total — the components' formula.</summary>
        public double Percentage => Total > 0 ? (double)Part / Total * 100 : 0;
    }

    public sealed record VetoExpectation(int TotalDetections, int VetoedCount, IReadOnlyDictionary<string, Ratio> Algorithms)
    {
        /// <summary>The veto rate the component renders (<c>decimal</c>, like OpenAIVetoAnalytics.VetoRate).</summary>
        public decimal VetoRate => TotalDetections > 0 ? (decimal)VetoedCount / TotalDetections * 100 : 0;
    }

    public sealed record RecentDetectionExpectation(long UserId, bool IsSpam, double Score);
}
