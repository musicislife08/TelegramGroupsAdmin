using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Models.Analytics;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Service for message analytics and statistics
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public interface IMessageStatsService
{
    /// <summary>
    /// Get overall message history statistics
    /// </summary>
    Task<HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get spam detection statistics
    /// </summary>
    Task<SpamSummaryStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent spam detections with actor information
    /// </summary>
    Task<List<RecentDetection>> GetRecentDetectionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get message trends over a date range with timezone conversion
    /// </summary>
    Task<MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count messages whose current verdict is curated training data (explicit labels plus implicit spam),
    /// and how many of those are spam
    /// </summary>
    Task<(int Total, int Spam)> GetCuratedTrainingCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Veto statistics for messages flagged by detection checks but overridden by the AI veto, since <paramref name="since"/>.
    /// </summary>
    Task<OpenAIVetoAnalytics> GetOpenAIVetoAnalyticsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent messages flagged as spam but vetoed by the AI, for manual inspection.
    /// </summary>
    Task<List<VetoedMessage>> GetRecentVetoedMessagesAsync(int limit = 50, CancellationToken cancellationToken = default);
}
