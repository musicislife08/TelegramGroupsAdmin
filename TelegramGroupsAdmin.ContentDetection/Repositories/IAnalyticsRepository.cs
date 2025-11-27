using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for analytics queries across detection results and user actions.
/// Phase 5: Performance metrics for testing validation
/// </summary>
public interface IAnalyticsRepository
{
    /// <summary>
    /// Get detection accuracy statistics (false positives + false negatives) for the specified date range.
    /// False positive = spam → ham correction, False negative = ham → spam correction
    /// </summary>
    Task<DetectionAccuracyStats> GetDetectionAccuracyStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get response time statistics (detection → action latency).
    /// Measures time between spam detection and moderation action (ban/warn/delete)
    /// </summary>
    Task<ResponseTimeStats> GetResponseTimeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare effectiveness of all detection methods (9 spam algorithms).
    /// Returns stats grouped by detection_method: count, spam%, avg confidence
    /// </summary>
    Task<List<DetectionMethodStats>> GetDetectionMethodComparisonAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get daily detection trends for charting.
    /// Daily breakdown of spam vs ham detections
    /// </summary>
    Task<List<DailyDetectionTrend>> GetDailyDetectionTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get welcome system summary statistics (total joins, acceptance rate, avg time to accept, timeout rate).
    /// </summary>
    Task<WelcomeStatsSummary> GetWelcomeStatsSummaryAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get daily welcome join trends for charting (date in user's timezone, join count per day).
    /// </summary>
    Task<List<DailyWelcomeJoinTrend>> GetDailyWelcomeJoinTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get welcome response distribution (Accepted/Denied/Timeout/Left counts and percentages).
    /// </summary>
    Task<WelcomeResponseDistribution> GetWelcomeResponseDistributionAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get per-chat welcome statistics breakdown (joins, acceptance rate, timeout rate by chat).
    /// </summary>
    Task<List<ChatWelcomeStats>> GetChatWelcomeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get algorithm performance timing statistics from check_results_json JSONB column.
    /// ML-5: Per-algorithm execution time metrics (average, P95, min, max, total contribution)
    /// </summary>
    Task<List<AlgorithmPerformanceStats>> GetAlgorithmPerformanceStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);
}
