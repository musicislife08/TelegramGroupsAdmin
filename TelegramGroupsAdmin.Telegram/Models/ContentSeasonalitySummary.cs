namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Summary of content detection activity patterns across different time scales (UX-2.1)
/// </summary>
public record ContentSeasonalitySummary
{
    /// <summary>
    /// Peak hour range(s) for spam activity (e.g., "7am-11am, 5pm-8pm" or "3am")
    /// Always populated regardless of date range
    /// </summary>
    public string HourlyPattern { get; init; } = string.Empty;

    /// <summary>
    /// Peak day range for spam activity (e.g., "Mon-Wed" or "Wednesday")
    /// Null if date range is less than 7 days
    /// </summary>
    public string? WeeklyPattern { get; init; }

    /// <summary>
    /// Peak month range for spam activity (e.g., "Nov-Dec" or "November")
    /// Null if date range is less than 60 days or doesn't span multiple months
    /// </summary>
    public string? MonthlyPattern { get; init; }

    /// <summary>
    /// Whether weekly pattern data is available (date range >= 7 days)
    /// </summary>
    public bool HasWeeklyData { get; init; }

    /// <summary>
    /// Whether monthly pattern data is available (date range >= 60 days and spans multiple months)
    /// </summary>
    public bool HasMonthlyData { get; init; }
}
