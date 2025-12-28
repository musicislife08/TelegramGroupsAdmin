namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Message statistics for the dashboard.
/// </summary>
public record DashboardStats(
    int TotalMessages,
    int UniqueUsers,
    int PhotoCount,
    DateTimeOffset? OldestTimestamp,
    DateTimeOffset? NewestTimestamp
);
