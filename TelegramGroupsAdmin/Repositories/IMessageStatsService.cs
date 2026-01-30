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
}
