using TelegramGroupsAdmin.Models.Analytics;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for message analytics and statistics queries (backs <see cref="IMessageStatsService"/>)
/// </summary>
public interface IMessageStatsRepository
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
    Task<(int Total, int Spam)> GetCuratedTrainingCountsAsync(CancellationToken cancellationToken = default);
}
