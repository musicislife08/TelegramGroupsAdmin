using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories.Mappings;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for analytics queries across detection results and user actions.
/// Phase 5: Performance metrics for testing validation
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<AnalyticsRepository> _logger;

    public AnalyticsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<AnalyticsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<DetectionAccuracyStats> GetDetectionAccuracyStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Use DetectionAccuracyView which pre-computes FP/FN flags
        // Eliminates expensive correlated sub-queries with .Any() EXISTS clauses
        var accuracyRecords = await context.DetectionAccuracy
            .AsNoTracking()
            .Where(v => v.DetectedAt >= startDate && v.DetectedAt <= endDate)
            .ToListAsync(cancellationToken);

        var totalDetections = accuracyRecords.Count;
        var totalFalsePositives = accuracyRecords.Count(r => r.IsFalsePositive);
        var totalFalseNegatives = accuracyRecords.Count(r => r.IsFalseNegative);

        // Group by user's local date (C# server-side grouping for timezone support)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyBreakdown = accuracyRecords
            .GroupBy(r =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(r.DetectedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g =>
            {
                var dayTotal = g.Count();
                var dayFP = g.Count(r => r.IsFalsePositive);
                var dayFN = g.Count(r => r.IsFalseNegative);
                var dayCorrect = dayTotal - dayFP - dayFN;

                return new DailyAccuracy
                {
                    Date = g.Key,
                    TotalDetections = dayTotal,
                    FalsePositiveCount = dayFP,
                    FalseNegativeCount = dayFN,
                    FalsePositivePercentage = dayTotal > 0 ? (dayFP / (double)dayTotal * 100.0) : 0,
                    FalseNegativePercentage = dayTotal > 0 ? (dayFN / (double)dayTotal * 100.0) : 0,
                    Accuracy = dayTotal > 0 ? (dayCorrect / (double)dayTotal * 100.0) : 0
                };
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new DetectionAccuracyStats
        {
            DailyBreakdown = dailyBreakdown,
            TotalFalsePositives = totalFalsePositives,
            TotalFalseNegatives = totalFalseNegatives,
            TotalDetections = totalDetections,
            FalsePositivePercentage = totalDetections > 0
                ? (totalFalsePositives / (double)totalDetections * 100.0)
                : 0,
            FalseNegativePercentage = totalDetections > 0
                ? (totalFalseNegatives / (double)totalDetections * 100.0)
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
                DailyAverages = [],
                AverageMs = 0,
                MedianMs = 0,
                P95Ms = 0,
                TotalActions = 0
            };
        }

        // Calculate daily averages (group by user's local date)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyAverages = responseTimes
            .GroupBy(rt =>
            {
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

        // Fetch all detection results with JSON in date range
        var allDetections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.DetectionSource != "manual") // Exclude manual reviews
            .Where(dr => dr.CheckResultsJson != null) // Only rows with individual check data
            .Select(dr => new
            {
                dr.MessageId,
                dr.CheckResultsJson,
                dr.IsSpam
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Use DetectionAccuracyView for FP/FN lookups (eliminates expensive correlated sub-queries)
        var accuracyLookup = await context.DetectionAccuracy
            .AsNoTracking()
            .Where(v => v.DetectedAt >= startDate && v.DetectedAt <= endDate)
            .Where(v => v.IsFalsePositive || v.IsFalseNegative) // Only need records with corrections
            .Select(v => new { v.MessageId, v.IsFalsePositive, v.IsFalseNegative })
            .ToDictionaryAsync(v => v.MessageId, cancellationToken);

        // Parse JSON and aggregate per-algorithm stats
        var algorithmStats = new Dictionary<string, AlgorithmStatsAccumulator>();

        foreach (var detection in allDetections)
        {
            var checks = ParseCheckResults(detection.CheckResultsJson);
            accuracyLookup.TryGetValue(detection.MessageId, out var accuracy);
            var isFalsePositive = accuracy?.IsFalsePositive ?? false;
            var isFalseNegative = accuracy?.IsFalseNegative ?? false;

            foreach (var check in checks)
            {
                var checkName = check.CheckName.ToString();

                if (!algorithmStats.TryGetValue(checkName, out var stats))
                {
                    stats = new AlgorithmStatsAccumulator();
                    algorithmStats[checkName] = stats;
                }
                stats.TotalChecks++;

                if (check.Result == CheckResultType.Spam)
                {
                    stats.SpamVotes++;
                    stats.SpamConfidences.Add(check.Confidence);

                    if (isFalsePositive)
                    {
                        stats.ContributedToFPs++;
                    }
                }
                else if (check.Result == CheckResultType.Clean && isFalseNegative)
                {
                    stats.ContributedToFNs++;
                }
            }
        }

        // Convert to result list
        var result = algorithmStats.Select(kvp => new DetectionMethodStats
        {
            MethodName = kvp.Key,
            TotalChecks = kvp.Value.TotalChecks,
            SpamDetected = kvp.Value.SpamVotes,
            SpamPercentage = kvp.Value.TotalChecks > 0
                ? (kvp.Value.SpamVotes / (double)kvp.Value.TotalChecks * 100.0)
                : 0,
            AverageSpamConfidence = kvp.Value.SpamConfidences.Count > 0
                ? kvp.Value.SpamConfidences.Average()
                : null,
            ContributedToFalsePositives = kvp.Value.ContributedToFPs,
            ContributedToFalseNegatives = kvp.Value.ContributedToFNs
        }).OrderByDescending(m => m.TotalChecks).ToList();

        return result;
    }

    private List<CheckResult> ParseCheckResults(string? json)
    {
        return CheckResultsSerializer.Deserialize(json ?? string.Empty);
    }

    private class AlgorithmStatsAccumulator
    {
        public int TotalChecks { get; set; }
        public int SpamVotes { get; set; }
        public List<double> SpamConfidences { get; set; } = [];
        public int ContributedToFPs { get; set; }
        public int ContributedToFNs { get; set; }
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
            .GroupBy(dr =>
            {
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

    public async Task<WelcomeStatsSummary> GetWelcomeStatsSummaryAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Fetch all welcome responses in date range (UTC filter)
        var responses = await context.WelcomeResponses
            .Where(wr => wr.CreatedAt >= startDate && wr.CreatedAt <= endDate)
            .Select(wr => new { wr.Response, wr.CreatedAt, wr.RespondedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalJoins = responses.Count;

        if (totalJoins == 0)
        {
            return new WelcomeStatsSummary
            {
                TotalJoins = 0,
                TotalAccepted = 0,
                TotalDenied = 0,
                TotalTimedOut = 0,
                TotalLeft = 0,
                AcceptanceRate = 0,
                TimeoutRate = 0,
                AverageMinutesToAccept = 0
            };
        }

        var totalAccepted = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Accepted);
        var totalDenied = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Denied);
        var totalTimedOut = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Timeout);
        var totalLeft = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Left);

        // Calculate average time to accept (only for accepted responses)
        var acceptedResponses = responses.Where(r => r.Response == Data.Models.WelcomeResponseType.Accepted).ToList();
        var avgMinutes = acceptedResponses.Any()
            ? acceptedResponses.Average(r => (r.RespondedAt - r.CreatedAt).TotalMinutes)
            : 0;

        return new WelcomeStatsSummary
        {
            TotalJoins = totalJoins,
            TotalAccepted = totalAccepted,
            TotalDenied = totalDenied,
            TotalTimedOut = totalTimedOut,
            TotalLeft = totalLeft,
            AcceptanceRate = totalJoins > 0 ? (totalAccepted / (double)totalJoins * 100.0) : 0,
            TimeoutRate = totalJoins > 0 ? (totalTimedOut / (double)totalJoins * 100.0) : 0,
            AverageMinutesToAccept = avgMinutes
        };
    }

    public async Task<List<DailyWelcomeJoinTrend>> GetDailyWelcomeJoinTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Fetch welcome responses (UTC filter)
        var responses = await context.WelcomeResponses
            .Where(wr => wr.CreatedAt >= startDate && wr.CreatedAt <= endDate)
            .Select(wr => new { wr.CreatedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Group by user's local date (C# side conversion)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyTrends = responses
            .GroupBy(wr =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(wr.CreatedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .Select(g => new DailyWelcomeJoinTrend
            {
                Date = g.Key,
                JoinCount = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToList();

        return dailyTrends;
    }

    public async Task<WelcomeResponseDistribution> GetWelcomeResponseDistributionAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Fetch welcome responses (UTC filter)
        var responses = await context.WelcomeResponses
            .Where(wr => wr.CreatedAt >= startDate && wr.CreatedAt <= endDate)
            .Select(wr => new { wr.Response })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalResponses = responses.Count;

        if (totalResponses == 0)
        {
            return new WelcomeResponseDistribution
            {
                AcceptedCount = 0,
                DeniedCount = 0,
                TimeoutCount = 0,
                LeftCount = 0,
                TotalResponses = 0,
                AcceptedPercentage = 0,
                DeniedPercentage = 0,
                TimeoutPercentage = 0,
                LeftPercentage = 0
            };
        }

        var acceptedCount = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Accepted);
        var deniedCount = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Denied);
        var timeoutCount = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Timeout);
        var leftCount = responses.Count(r => r.Response == Data.Models.WelcomeResponseType.Left);

        return new WelcomeResponseDistribution
        {
            AcceptedCount = acceptedCount,
            DeniedCount = deniedCount,
            TimeoutCount = timeoutCount,
            LeftCount = leftCount,
            TotalResponses = totalResponses,
            AcceptedPercentage = totalResponses > 0 ? (acceptedCount / (double)totalResponses * 100.0) : 0,
            DeniedPercentage = totalResponses > 0 ? (deniedCount / (double)totalResponses * 100.0) : 0,
            TimeoutPercentage = totalResponses > 0 ? (timeoutCount / (double)totalResponses * 100.0) : 0,
            LeftPercentage = totalResponses > 0 ? (leftCount / (double)totalResponses * 100.0) : 0
        };
    }

    public async Task<List<ChatWelcomeStats>> GetChatWelcomeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Join welcome_responses with managed_chats to get chat names
        var chatStats = await (
            from wr in context.WelcomeResponses
            where wr.CreatedAt >= startDate && wr.CreatedAt <= endDate
            join mc in context.ManagedChats on wr.ChatId equals mc.ChatId into chatGroup
            from mc in chatGroup.DefaultIfEmpty()
            select new
            {
                wr.ChatId,
                ChatName = mc != null ? mc.ChatName : "Unknown Chat",
                wr.Response
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Group by chat and calculate stats
        var result = chatStats
            .GroupBy(cs => new { cs.ChatId, cs.ChatName })
            .Select(g =>
            {
                var totalJoins = g.Count();
                var acceptedCount = g.Count(cs => cs.Response == Data.Models.WelcomeResponseType.Accepted);
                var deniedCount = g.Count(cs => cs.Response == Data.Models.WelcomeResponseType.Denied);
                var timeoutCount = g.Count(cs => cs.Response == Data.Models.WelcomeResponseType.Timeout);
                var leftCount = g.Count(cs => cs.Response == Data.Models.WelcomeResponseType.Left);

                return new ChatWelcomeStats
                {
                    ChatId = g.Key.ChatId,
                    ChatName = g.Key.ChatName,
                    TotalJoins = totalJoins,
                    AcceptedCount = acceptedCount,
                    DeniedCount = deniedCount,
                    TimeoutCount = timeoutCount,
                    LeftCount = leftCount,
                    AcceptanceRate = totalJoins > 0 ? (acceptedCount / (double)totalJoins * 100.0) : 0,
                    TimeoutRate = totalJoins > 0 ? (timeoutCount / (double)totalJoins * 100.0) : 0
                };
            })
            .OrderByDescending(cs => cs.TotalJoins)
            .ToList();

        return result;
    }

    public async Task<List<AlgorithmPerformanceStats>> GetAlgorithmPerformanceStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Query check_results_json JSONB column to extract ProcessingTimeMs for each algorithm
            // Uses CTE to extract and cast JSONB values once, then aggregates for better performance
            // GIN index on check_results_json enables efficient JSONB path queries
            // CheckName stored as integer enum value (normalized by migration 20251126022902)
            // Phase 5: Using EF Core SqlQuery with keyless entity type for type-safe query results
            _logger.LogDebug("Fetching algorithm performance stats from {StartDate} to {EndDate}", startDate, endDate);

            // Execute raw SQL using SqlQuery (type-safe with keyless entity configuration)
            var rawResults = await context.Database
                .SqlQuery<DataModels.RawAlgorithmPerformanceStatsDto>($@"
                    WITH extracted_checks AS (
                        SELECT
                            (check_elem->>'CheckName')::int AS check_name_enum,
                            (check_elem->>'ProcessingTimeMs')::float AS processing_time_ms
                        FROM detection_results dr,
                             jsonb_array_elements(dr.check_results_json->'Checks') AS check_elem
                        WHERE dr.detected_at >= {startDate.UtcDateTime}
                          AND dr.detected_at <= {endDate.UtcDateTime}
                          AND (check_elem->>'ProcessingTimeMs')::float > 0
                    )
                    SELECT
                        check_name_enum AS CheckNameEnum,
                        COUNT(*)::int AS TotalExecutions,
                        AVG(processing_time_ms) AS AverageMs,
                        PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY processing_time_ms) AS P95Ms,
                        MAX(processing_time_ms) AS MaxMs,
                        MIN(processing_time_ms) AS MinMs,
                        (AVG(processing_time_ms) * COUNT(*)) AS TotalTimeContribution
                    FROM extracted_checks
                    GROUP BY check_name_enum
                    ORDER BY TotalTimeContribution DESC
                ")
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Retrieved {Count} raw algorithm performance results", rawResults.Count);

            // Map DTO to UI model using standard repository pattern (Phase 5)
            // Convert integer enum values to string names in C# for compile-time safety
            var results = rawResults.Select(dto =>
            {
                var raw = dto.ToModel();
                return new AlgorithmPerformanceStats
                {
                    CheckName = ((ContentDetection.Constants.CheckName)raw.CheckNameEnum).ToString(),
                    TotalExecutions = raw.TotalExecutions,
                    AverageMs = raw.AverageMs,
                    P95Ms = raw.P95Ms,
                    MaxMs = raw.MaxMs,
                    MinMs = raw.MinMs,
                    TotalTimeContribution = raw.TotalTimeContribution
                };
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch algorithm performance stats from {StartDate} to {EndDate}", startDate, endDate);
            throw;
        }
    }

    public async Task<DailySpamSummary> GetDailySpamSummaryAsync(
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        // Calculate today and yesterday boundaries in user's timezone
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var todayLocal = DateOnly.FromDateTime(nowInUserTz);
        var yesterdayLocal = todayLocal.AddDays(-1);

        // Convert local dates to UTC boundaries for database query
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.ToDateTime(TimeOnly.MinValue), timeZone);
        var yesterdayStartUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayLocal.ToDateTime(TimeOnly.MinValue), timeZone);
        var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);

        // Fetch 2 days of data and let GetDailyDetectionTrendsAsync handle the grouping
        var trends = await GetDailyDetectionTrendsAsync(
            new DateTimeOffset(yesterdayStartUtc),
            new DateTimeOffset(tomorrowStartUtc),
            timeZoneId,
            cancellationToken);

        // Find today's and yesterday's data from the result
        var todayData = trends.FirstOrDefault(t => t.Date == todayLocal);
        var yesterdayData = trends.FirstOrDefault(t => t.Date == yesterdayLocal);

        // Build summary
        var todaySpam = todayData?.SpamCount ?? 0;
        var todayHam = todayData?.HamCount ?? 0;
        var todayTotal = todaySpam + todayHam;

        var summary = new DailySpamSummary
        {
            TodaySpamCount = todaySpam,
            TodayHamCount = todayHam,
            TodayTotalDetections = todayTotal,
            TodaySpamRate = todayTotal > 0 ? (todaySpam / (double)todayTotal * 100.0) : 0
        };

        // Add yesterday comparison if data exists
        if (yesterdayData is not null)
        {
            var yesterdaySpam = yesterdayData.SpamCount;
            var yesterdayHam = yesterdayData.HamCount;
            var yesterdayTotal = yesterdaySpam + yesterdayHam;

            summary.YesterdaySpamCount = yesterdaySpam;
            summary.YesterdayHamCount = yesterdayHam;
            summary.YesterdayTotalDetections = yesterdayTotal;
            summary.YesterdaySpamRate = yesterdayTotal > 0 ? (yesterdaySpam / (double)yesterdayTotal * 100.0) : 0;

            // Calculate changes
            summary.SpamCountChange = todaySpam - yesterdaySpam;
            summary.SpamRateChange = summary.TodaySpamRate - summary.YesterdaySpamRate;
        }

        return summary;
    }

    public async Task<SpamTrendComparison> GetSpamTrendComparisonAsync(
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var nowInUserTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var todayLocal = DateOnly.FromDateTime(nowInUserTz);

        // Calculate period boundaries in user's timezone
        // Week: This week = last 7 days including today, Last week = 7 days before that
        var thisWeekStart = todayLocal.AddDays(-6);
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var lastWeekEnd = thisWeekStart.AddDays(-1);

        // Month: Current calendar month vs previous calendar month
        var thisMonthStart = new DateOnly(nowInUserTz.Year, nowInUserTz.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthEnd = thisMonthStart.AddDays(-1);

        // Year: Year-to-date vs same period last year (apples-to-apples comparison)
        // e.g., Jan 1-25, 2026 vs Jan 1-25, 2025
        var thisYearStart = new DateOnly(nowInUserTz.Year, 1, 1);
        var lastYearStart = new DateOnly(nowInUserTz.Year - 1, 1, 1);
        var lastYearEnd = todayLocal.AddYears(-1);

        // Fetch all spam counts in one query covering max range needed (last year start to today)
        var minDate = lastYearStart;
        var maxDate = todayLocal.AddDays(1); // Include today

        var minDateUtc = TimeZoneInfo.ConvertTimeToUtc(minDate.ToDateTime(TimeOnly.MinValue), timeZone);
        var maxDateUtc = TimeZoneInfo.ConvertTimeToUtc(maxDate.ToDateTime(TimeOnly.MinValue), timeZone);

        // Fetch detection results for the full period
        // Note: AsNoTracking not needed since we're projecting to value types (DateTimeOffset)
        var detections = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= minDateUtc && dr.DetectedAt < maxDateUtc)
            .Where(dr => dr.IsSpam)
            .Select(dr => dr.DetectedAt)
            .ToListAsync(cancellationToken);

        // Group by user's local date
        var spamByDate = detections
            .GroupBy(detectedAt =>
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(detectedAt.UtcDateTime, timeZone);
                return DateOnly.FromDateTime(localTime);
            })
            .ToDictionary(g => g.Key, g => g.Count());

        // Helper to sum spam in a date range
        int SumSpamInRange(DateOnly start, DateOnly end)
        {
            return spamByDate
                .Where(kvp => kvp.Key >= start && kvp.Key <= end)
                .Sum(kvp => kvp.Value);
        }

        // Calculate all period sums
        var thisWeekSpam = SumSpamInRange(thisWeekStart, todayLocal);
        var lastWeekSpam = SumSpamInRange(lastWeekStart, lastWeekEnd);
        var thisMonthSpam = SumSpamInRange(thisMonthStart, todayLocal);
        var lastMonthSpam = SumSpamInRange(lastMonthStart, lastMonthEnd);
        var thisYearSpam = SumSpamInRange(thisYearStart, todayLocal);
        var lastYearSpam = SumSpamInRange(lastYearStart, lastYearEnd);

        // Calculate days in each period
        var daysInThisWeek = (todayLocal.DayNumber - thisWeekStart.DayNumber) + 1;
        var daysInLastWeek = 7;
        var daysInThisMonth = todayLocal.Day;
        var daysInLastMonth = DateTime.DaysInMonth(lastMonthStart.Year, lastMonthStart.Month);
        var daysInThisYear = todayLocal.DayOfYear;
        var daysInLastYear = (lastYearEnd.DayNumber - lastYearStart.DayNumber) + 1;

        // Calculate percentage changes (only when previous period > 0 to avoid division by zero)
        double? CalculatePercentChange(int current, int previous)
        {
            if (previous == 0) return null;
            return ((current - previous) / (double)previous) * 100.0;
        }

        return new SpamTrendComparison
        {
            // Week data (use 0 if no data, not null)
            ThisWeekSpamCount = thisWeekSpam,
            LastWeekSpamCount = lastWeekSpam,
            DaysInThisWeek = daysInThisWeek,
            DaysInLastWeek = daysInLastWeek,
            WeekOverWeekChange = CalculatePercentChange(thisWeekSpam, lastWeekSpam),

            // Month data
            ThisMonthSpamCount = thisMonthSpam,
            LastMonthSpamCount = lastMonthSpam,
            DaysInThisMonth = daysInThisMonth,
            DaysInLastMonth = daysInLastMonth,
            MonthOverMonthChange = CalculatePercentChange(thisMonthSpam, lastMonthSpam),

            // Year data (now compares same period, e.g., Jan 1-25 vs Jan 1-25)
            ThisYearSpamCount = thisYearSpam,
            LastYearSpamCount = lastYearSpam,
            DaysInThisYear = daysInThisYear,
            DaysInLastYear = daysInLastYear,
            YearOverYearChange = CalculatePercentChange(thisYearSpam, lastYearSpam)
        };
    }
}
