using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for message analytics and statistics
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public interface IMessageStatsService
{
    /// <summary>
    /// Get overall message history statistics
    /// </summary>
    Task<UiModels.HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get spam detection statistics
    /// </summary>
    Task<UiModels.DetectionStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent spam detections with actor information
    /// </summary>
    Task<List<UiModels.DetectionResultRecord>> GetRecentDetectionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get message trends over a date range with timezone conversion
    /// </summary>
    Task<UiModels.MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default);
}
