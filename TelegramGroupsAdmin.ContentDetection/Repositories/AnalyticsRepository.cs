using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

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

        // Get false positive and false negative message IDs using helper methods
        var fpMessageIds = await GetFalsePositiveMessageIdsAsync(context, startDate, endDate, cancellationToken);
        var fnMessageIds = await GetFalseNegativeMessageIdsAsync(context, startDate, endDate, cancellationToken);

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

        // Group by user's local date (C# server-side grouping)
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var dailyBreakdownMapped = detections
            .GroupBy(dr =>
            {
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
            TotalFalsePositives = fpMessageIds.Count,
            TotalFalseNegatives = fnMessageIds.Count,
            TotalDetections = totalDetections,
            FalsePositivePercentage = totalDetections > 0
                ? (fpMessageIds.Count / (double)totalDetections * 100.0)
                : 0,
            FalseNegativePercentage = totalDetections > 0
                ? (fnMessageIds.Count / (double)totalDetections * 100.0)
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

        // Get false positive/negative message IDs using helper methods
        var fpSet = await GetFalsePositiveMessageIdsAsync(context, startDate, endDate, cancellationToken);
        var fnSet = await GetFalseNegativeMessageIdsAsync(context, startDate, endDate, cancellationToken);

        // Parse JSON and aggregate per-algorithm stats
        var algorithmStats = new Dictionary<string, AlgorithmStatsAccumulator>();

        foreach (var detection in allDetections)
        {
            var checks = ParseCheckResults(detection.CheckResultsJson);
            var isFalsePositive = fpSet.Contains(detection.MessageId);
            var isFalseNegative = fnSet.Contains(detection.MessageId);

            foreach (var check in checks)
            {
                var checkName = check.CheckName.ToString();

                if (!algorithmStats.ContainsKey(checkName))
                {
                    algorithmStats[checkName] = new AlgorithmStatsAccumulator();
                }

                var stats = algorithmStats[checkName];
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
            // Execute raw SQL with integer CheckNameEnum, then map to enum names in C# for compile-time safety
            _logger.LogDebug("Fetching algorithm performance stats from {StartDate} to {EndDate}", startDate, endDate);

            // Execute raw SQL and map manually (EF Core SqlQuery requires configured entity types)
            var sql = @"
                WITH extracted_checks AS (
                    SELECT
                        (check_elem->>'CheckName')::int AS check_name_enum,
                        (check_elem->>'ProcessingTimeMs')::float AS processing_time_ms
                    FROM detection_results dr,
                         jsonb_array_elements(dr.check_results_json->'Checks') AS check_elem
                    WHERE dr.detected_at >= @p0
                      AND dr.detected_at <= @p1
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
                ORDER BY TotalTimeContribution DESC";

            var connection = context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new Npgsql.NpgsqlParameter("p0", startDate.UtcDateTime));
            command.Parameters.Add(new Npgsql.NpgsqlParameter("p1", endDate.UtcDateTime));

            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            var rawResults = new List<RawAlgorithmPerformanceStats>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rawResults.Add(new RawAlgorithmPerformanceStats
                {
                    CheckNameEnum = reader.GetInt32(0),
                    TotalExecutions = reader.GetInt32(1),
                    AverageMs = reader.GetDouble(2),
                    P95Ms = reader.GetDouble(3),
                    MaxMs = reader.GetDouble(4),
                    MinMs = reader.GetDouble(5),
                    TotalTimeContribution = reader.GetDouble(6)
                });
            }

            _logger.LogDebug("Retrieved {Count} raw algorithm performance results", rawResults.Count);

            // Map integer enum values to string names in C# (compile-time safe!)
            var results = rawResults.Select(r => new AlgorithmPerformanceStats
            {
                CheckName = ((ContentDetection.Constants.CheckName)r.CheckNameEnum).ToString(),
                TotalExecutions = r.TotalExecutions,
                AverageMs = r.AverageMs,
                P95Ms = r.P95Ms,
                MaxMs = r.MaxMs,
                MinMs = r.MinMs,
                TotalTimeContribution = r.TotalTimeContribution
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch algorithm performance stats from {StartDate} to {EndDate}", startDate, endDate);
            throw;
        }
    }

    /// <summary>
    /// Get message IDs for false positives (system said spam → user corrected to ham)
    /// </summary>
    private static async Task<HashSet<long>> GetFalsePositiveMessageIdsAsync(
        AppDbContext context,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken)
    {
        var messageIds = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => dr.IsSpam && dr.DetectionSource != "manual")
            .Where(dr => context.DetectionResults.Any(correction =>
                correction.MessageId == dr.MessageId &&
                correction.DetectionSource == "manual" &&
                !correction.IsSpam &&
                correction.DetectedAt > dr.DetectedAt))
            .Select(dr => dr.MessageId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return messageIds.ToHashSet();
    }

    /// <summary>
    /// Get message IDs for false negatives (system said ham → user corrected to spam)
    /// </summary>
    private static async Task<HashSet<long>> GetFalseNegativeMessageIdsAsync(
        AppDbContext context,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken)
    {
        var messageIds = await context.DetectionResults
            .Where(dr => dr.DetectedAt >= startDate && dr.DetectedAt <= endDate)
            .Where(dr => !dr.IsSpam && dr.DetectionSource != "manual")
            .Where(dr => context.DetectionResults.Any(correction =>
                correction.MessageId == dr.MessageId &&
                correction.DetectionSource == "manual" &&
                correction.IsSpam &&
                correction.DetectedAt > dr.DetectedAt))
            .Select(dr => dr.MessageId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return messageIds.ToHashSet();
    }
}
