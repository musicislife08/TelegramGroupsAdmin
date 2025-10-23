using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for analytics queries across detection results and user actions.
/// Phase 5: Performance metrics for testing validation
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AnalyticsRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<FalsePositiveStats> GetFalsePositiveStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // False positive = message initially detected as spam, then manually marked as ham
        // Strategy: Find spam detections that were later overridden by manual ham detection
        var falsePositives = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.IsSpam && dr.DetectionSource != "manual") // Initial spam detection
            .Where(dr => context.DetectionResults.Any(correction =>
                correction.MessageId == dr.MessageId &&
                correction.DetectionSource == "manual" &&
                !correction.IsSpam &&
                correction.DetectedAt > dr.DetectedAt)) // Later corrected to ham
            .Select(dr => new
            {
                dr.MessageId,
                dr.DetectedAt
            })
            .Distinct()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalDetections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.DetectionSource != "manual") // Exclude manual reviews from total
            .CountAsync(cancellationToken);

        // Group by date
        var dailyBreakdown = falsePositives
            .GroupBy(fp => DateOnly.FromDateTime(fp.DetectedAt.Date))
            .Select(g => new DailyFalsePositive
            {
                Date = g.Key,
                FalsePositiveCount = g.Count(),
                TotalDetections = context.DetectionResults
                    .Count(dr => DateOnly.FromDateTime(dr.DetectedAt.Date) == g.Key &&
                                 dr.DetectionSource != "manual"),
                Percentage = 0 // Will calculate below
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Calculate percentages
        foreach (var day in dailyBreakdown)
        {
            day.Percentage = day.TotalDetections > 0
                ? (day.FalsePositiveCount / (double)day.TotalDetections * 100.0)
                : 0;
        }

        return new FalsePositiveStats
        {
            DailyBreakdown = dailyBreakdown,
            TotalFalsePositives = falsePositives.Count,
            TotalDetections = totalDetections,
            OverallPercentage = totalDetections > 0
                ? (falsePositives.Count / (double)totalDetections * 100.0)
                : 0
        };
    }

    public async Task<ResponseTimeStats> GetResponseTimeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Join spam detections with subsequent user actions (ban/warn)
        var responseTimes = await (
            from dr in context.DetectionResults
            where dr.DetectedAt >= startDate && dr.DetectedAt <= endDate
            where dr.IsSpam
            join ua in context.UserActions on dr.MessageId equals ua.MessageId
            where ua.ActionType == Data.Models.UserActionType.Ban ||
                  ua.ActionType == Data.Models.UserActionType.Warn
            where ua.IssuedAt >= dr.DetectedAt // Action after detection
            select new
            {
                dr.DetectedAt,
                ua.IssuedAt,
                ResponseMs = (ua.IssuedAt - dr.DetectedAt).TotalMilliseconds
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (!responseTimes.Any())
        {
            return new ResponseTimeStats
            {
                DailyAverages = new(),
                AverageMs = 0,
                MedianMs = 0,
                P95Ms = 0,
                TotalActions = 0
            };
        }

        // Calculate daily averages
        var dailyAverages = responseTimes
            .GroupBy(rt => DateOnly.FromDateTime(rt.DetectedAt.Date))
            .Select(g => new DailyResponseTime
            {
                Date = g.Key,
                AverageMs = g.Average(x => x.ResponseMs),
                ActionCount = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Calculate percentiles
        var sortedTimes = responseTimes.Select(rt => rt.ResponseMs).OrderBy(x => x).ToList();
        var medianMs = sortedTimes[sortedTimes.Count / 2];
        var p95Index = (int)(sortedTimes.Count * 0.95);
        var p95Ms = sortedTimes[p95Index];

        return new ResponseTimeStats
        {
            DailyAverages = dailyAverages,
            AverageMs = responseTimes.Average(rt => rt.ResponseMs),
            MedianMs = medianMs,
            P95Ms = p95Ms,
            TotalActions = responseTimes.Count
        };
    }

    public async Task<List<DetectionMethodStats>> GetDetectionMethodComparisonAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Group by detection method
        var methodStats = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.DetectionSource != "manual") // Exclude manual reviews
            .GroupBy(dr => dr.DetectionMethod)
            .Select(g => new
            {
                MethodName = g.Key,
                TotalChecks = g.Count(),
                SpamDetected = g.Count(dr => dr.IsSpam),
                AverageConfidence = g.Average(dr => dr.Confidence),
                MessageIds = g.Select(dr => dr.MessageId).Distinct().ToList()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<DetectionMethodStats>();

        foreach (var method in methodStats)
        {
            // Find false positives for this method
            var falsePositives = await context.DetectionResults
                .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
                .Where(dr => dr.DetectionMethod == method.MethodName && dr.IsSpam)
                .Where(dr => context.DetectionResults.Any(correction =>
                    correction.MessageId == dr.MessageId &&
                    correction.DetectionSource == "manual" &&
                    !correction.IsSpam &&
                    correction.DetectedAt > dr.DetectedAt))
                .Select(dr => dr.MessageId)
                .Distinct()
                .CountAsync(cancellationToken);

            result.Add(new DetectionMethodStats
            {
                MethodName = method.MethodName ?? "unknown",
                TotalChecks = method.TotalChecks,
                SpamDetected = method.SpamDetected,
                SpamPercentage = method.TotalChecks > 0
                    ? (method.SpamDetected / (double)method.TotalChecks * 100.0)
                    : 0,
                AverageConfidence = method.AverageConfidence,
                FalsePositives = falsePositives,
                FalsePositiveRate = method.SpamDetected > 0
                    ? (falsePositives / (double)method.SpamDetected * 100.0)
                    : 0
            });
        }

        return result.OrderByDescending(m => m.TotalChecks).ToList();
    }

    public async Task<List<DailyDetectionTrend>> GetDailyDetectionTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var dailyTrends = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .GroupBy(dr => DateOnly.FromDateTime(dr.DetectedAt.Date))
            .Select(g => new DailyDetectionTrend
            {
                Date = g.Key,
                SpamCount = g.Count(dr => dr.IsSpam),
                HamCount = g.Count(dr => !dr.IsSpam)
            })
            .OrderBy(d => d.Date)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return dailyTrends;
    }
}
