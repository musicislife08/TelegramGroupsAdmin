using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for analytics queries across detection results and user actions.
/// Phase 5: Performance metrics for testing validation
/// </summary>
public interface IAnalyticsRepository
{
    /// <summary>
    /// Get false positive statistics for the specified date range.
    /// False positive = detection_results marked as spam, then corrected to ham via manual review
    /// </summary>
    Task<FalsePositiveStats> GetFalsePositiveStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get response time statistics (detection → action latency).
    /// Measures time between spam detection and moderation action (ban/warn/delete)
    /// </summary>
    Task<ResponseTimeStats> GetResponseTimeStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare effectiveness of all detection methods (9 spam algorithms).
    /// Returns stats grouped by detection_method: count, spam%, avg confidence
    /// </summary>
    Task<List<DetectionMethodStats>> GetDetectionMethodComparisonAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get daily detection trends for charting.
    /// Daily breakdown of spam vs ham detections
    /// </summary>
    Task<List<DailyDetectionTrend>> GetDailyDetectionTrendsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);
}
