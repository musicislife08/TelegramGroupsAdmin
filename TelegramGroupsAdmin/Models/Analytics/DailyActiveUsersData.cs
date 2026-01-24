namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Daily active users count for engagement trends (UX-2.1)
/// </summary>
public record DailyActiveUsersData
{
    /// <summary>
    /// Date (in user's timezone)
    /// </summary>
    public DateOnly Date { get; init; }

    /// <summary>
    /// Number of unique users who posted on this date
    /// </summary>
    public int UniqueUsers { get; init; }
}
