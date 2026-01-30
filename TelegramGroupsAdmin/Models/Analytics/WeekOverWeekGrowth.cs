namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Week-over-week growth metrics for message trends (UX-2.1)
/// </summary>
public record WeekOverWeekGrowth
{
    /// <summary>
    /// Percentage change in message volume compared to previous week
    /// </summary>
    public double MessageGrowthPercent { get; init; }

    /// <summary>
    /// Percentage change in unique users compared to previous week
    /// </summary>
    public double UserGrowthPercent { get; init; }

    /// <summary>
    /// Percentage change in spam percentage compared to previous week
    /// </summary>
    public double SpamGrowthPercent { get; init; }

    /// <summary>
    /// Whether there is enough data for a previous period comparison (date range >= 14 days)
    /// </summary>
    public bool HasPreviousPeriod { get; init; }
}
