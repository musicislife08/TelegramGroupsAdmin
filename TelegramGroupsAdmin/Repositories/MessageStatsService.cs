using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Service for message analytics and statistics.
/// Queries live in <see cref="IMessageStatsRepository"/>; this service delegates to it.
/// </summary>
public class MessageStatsService(IMessageStatsRepository repository) : IMessageStatsService
{
    public Task<HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => repository.GetStatsAsync(cancellationToken);

    public Task<SpamSummaryStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default)
        => repository.GetDetectionStatsAsync(cancellationToken);

    public Task<List<RecentDetection>> GetRecentDetectionsAsync(int limit = AnalyticsConstants.DefaultRecentDetectionsLimit, CancellationToken cancellationToken = default)
        => repository.GetRecentDetectionsAsync(limit, cancellationToken);

    public Task<MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string timeZoneId,
        CancellationToken cancellationToken = default)
        => repository.GetMessageTrendsAsync(chatIds, startDate, endDate, timeZoneId, cancellationToken);

    public Task<(int Total, int Spam)> GetCuratedTrainingCountsAsync(CancellationToken ct = default)
        => repository.GetCuratedTrainingCountsAsync(ct);
}
