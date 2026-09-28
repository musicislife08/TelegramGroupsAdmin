using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Service for message analytics and statistics.
/// Queries live in <see cref="IMessageStatsRepository"/> and, for AI veto analytics,
/// <see cref="IDetectionResultsRepository"/>; this service delegates to them.
/// </summary>
public class MessageStatsService(IMessageStatsRepository repository, IDetectionResultsRepository detectionResults) : IMessageStatsService
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

    public Task<OpenAIVetoAnalytics> GetOpenAIVetoAnalyticsAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        => detectionResults.GetOpenAIVetoAnalyticsAsync(since, cancellationToken);

    public Task<List<VetoedMessage>> GetRecentVetoedMessagesAsync(int limit = 50, CancellationToken cancellationToken = default)
        => detectionResults.GetRecentVetoedMessagesAsync(limit, cancellationToken);
}
