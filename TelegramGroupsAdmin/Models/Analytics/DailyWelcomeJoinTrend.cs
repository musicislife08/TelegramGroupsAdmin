namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Daily join trend for charting (date in user's timezone)
/// </summary>
public class DailyWelcomeJoinTrend
{
    /// <summary>Date in user's local timezone</summary>
    public DateOnly Date { get; set; }

    /// <summary>Number of users who joined on this date</summary>
    public int JoinCount { get; set; }
}
