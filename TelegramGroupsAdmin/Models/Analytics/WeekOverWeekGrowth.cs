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
    /// Percentage change in daily message average compared to previous week.
    /// Calculated as: ((currentWeekMessages/7) - (previousWeekMessages/7)) / (previousWeekMessages/7) * 100.
    /// Independent from MessageGrowthPercent.
    /// </summary>
    public double DailyAverageGrowthPercent { get; init; }

    /// <summary>
    /// Whether there is enough data for a previous period comparison (14+ days of data exist relative to endDate)
    /// </summary>
    public bool HasPreviousPeriod { get; init; }
}
