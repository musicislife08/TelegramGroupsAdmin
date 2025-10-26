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

    public async Task<DetectionAccuracyStats> GetDetectionAccuracyStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
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

        // False negative = message initially detected as ham, then manually marked as spam
        // Strategy: Find ham detections that were later overridden by manual spam detection
        var falseNegatives = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => !dr.IsSpam && dr.DetectionSource != "manual") // Initial ham detection
            .Where(dr => context.DetectionResults.Any(correction =>
                correction.MessageId == dr.MessageId &&
                correction.DetectionSource == "manual" &&
                correction.IsSpam &&
                correction.DetectedAt > dr.DetectedAt)) // Later corrected to spam
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

        // Fetch detection results (database does filtering)
        var detections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.DetectionSource != "manual")
            .Select(dr => new { dr.DetectedAt, dr.IsSpam, dr.MessageId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Get false positive and false negative message IDs (for lookup)
        var fpMessageIds = falsePositives.Select(fp => fp.MessageId).ToHashSet();
        var fnMessageIds = falseNegatives.Select(fn => fn.MessageId).ToHashSet();

        // Group by user's local date (C# server-side grouping)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyBreakdownMapped = detections
            .GroupBy(dr => {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(dr.DetectedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyAccuracy
            {
                Date = g.Key,
                TotalDetections = g.Count(),
                FalsePositiveCount = g.Count(dr => fpMessageIds.Contains(dr.MessageId)),
                FalseNegativeCount = g.Count(dr => fnMessageIds.Contains(dr.MessageId)),
                FalsePositivePercentage = 0, // Calculate below
                FalseNegativePercentage = 0, // Calculate below
                Accuracy = 0 // Calculate below
            })
            .OrderBy(d => d.Date)
            .ToList();

        // Calculate percentages
        foreach (var day in dailyBreakdownMapped)
        {
            day.FalsePositivePercentage = day.TotalDetections > 0
                ? (day.FalsePositiveCount / (double)day.TotalDetections * 100.0)
                : 0;
            day.FalseNegativePercentage = day.TotalDetections > 0
                ? (day.FalseNegativeCount / (double)day.TotalDetections * 100.0)
                : 0;
            var correctCount = day.TotalDetections - day.FalsePositiveCount - day.FalseNegativeCount;
            day.Accuracy = day.TotalDetections > 0
                ? (correctCount / (double)day.TotalDetections * 100.0)
                : 0;
        }

        return new DetectionAccuracyStats
        {
            DailyBreakdown = dailyBreakdownMapped,
            TotalFalsePositives = falsePositives.Count,
            TotalFalseNegatives = falseNegatives.Count,
            TotalDetections = totalDetections,
            FalsePositivePercentage = totalDetections > 0
                ? (falsePositives.Count / (double)totalDetections * 100.0)
                : 0,
            FalseNegativePercentage = totalDetections > 0
                ? (falseNegatives.Count / (double)totalDetections * 100.0)
                : 0
        };
    }

    public async Task<ResponseTimeStats> GetResponseTimeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
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

        // Calculate daily averages (group by user's local date)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyAverages = responseTimes
            .GroupBy(rt => {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(rt.DetectedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
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
        string timeZoneId,
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
            // Find false positives for this method (spam → ham corrections)
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

            // Find false negatives for this method (ham → spam corrections)
            var falseNegatives = await context.DetectionResults
                .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
                .Where(dr => dr.DetectionMethod == method.MethodName && !dr.IsSpam)
                .Where(dr => context.DetectionResults.Any(correction =>
                    correction.MessageId == dr.MessageId &&
                    correction.DetectionSource == "manual" &&
                    correction.IsSpam &&
                    correction.DetectedAt > dr.DetectedAt))
                .Select(dr => dr.MessageId)
                .Distinct()
                .CountAsync(cancellationToken);

            var hamDetected = method.TotalChecks - method.SpamDetected;

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
                    : 0,
                FalseNegatives = falseNegatives,
                FalseNegativeRate = hamDetected > 0
                    ? (falseNegatives / (double)hamDetected * 100.0)
                    : 0
            });
        }

        return result.OrderByDescending(m => m.TotalChecks).ToList();
    }

    public async Task<List<DailyDetectionTrend>> GetDailyDetectionTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Fetch detection results (database does filtering)
        var detections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Select(dr => new { dr.DetectedAt, dr.IsSpam })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Group by user's local date (C# server-side grouping)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyTrends = detections
            .GroupBy(dr => {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(dr.DetectedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyDetectionTrend
            {
                Date = g.Key,
                SpamCount = g.Count(dr => dr.IsSpam),
                HamCount = g.Count(dr => !dr.IsSpam)
            })
            .OrderBy(d => d.Date)
            .ToList();

        return dailyTrends;
    }
}
