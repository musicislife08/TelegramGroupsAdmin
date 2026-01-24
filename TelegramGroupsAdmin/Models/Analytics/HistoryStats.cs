namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// History statistics for UI display
/// </summary>
public record HistoryStats(
    int TotalMessages,
    int UniqueUsers,
    int PhotoCount,
    DateTimeOffset? OldestTimestamp,
    DateTimeOffset? NewestTimestamp
);
