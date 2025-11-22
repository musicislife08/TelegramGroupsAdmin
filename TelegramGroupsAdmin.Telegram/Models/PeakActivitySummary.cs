namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Summary of peak activity times for message volume (UX-2.1)
/// </summary>
public record PeakActivitySummary
{
    /// <summary>
    /// Peak hour range(s) for message activity (e.g., "7am-11am, 5pm-8pm" or "3pm")
    /// </summary>
    public string PeakHourRange { get; init; } = string.Empty;

    /// <summary>
    /// Peak day range for message activity (e.g., "Mon-Wed" or "Wednesday")
    /// Null if date range is less than 7 days
    /// </summary>
    public string? PeakDayRange { get; init; }

    /// <summary>
    /// Whether the selected date range has enough data for weekly patterns (>= 7 days)
    /// </summary>
    public bool HasEnoughDataForWeekly { get; init; }
}
