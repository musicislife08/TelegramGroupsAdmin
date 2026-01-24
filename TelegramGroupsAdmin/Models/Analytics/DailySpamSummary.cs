namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Daily spam summary for dashboard display.
/// Compares today's spam activity to yesterday for quick trend visibility.
/// Philosophy: Dashboard = quick daily snapshot, Analytics page = deep dives.
/// </summary>
public class DailySpamSummary
{
    #region Today's Data (always present)

    /// <summary>
    /// Number of spam detections today (in user's timezone)
    /// </summary>
    public int TodaySpamCount { get; set; }

    /// <summary>
    /// Number of clean (ham) detections today
    /// </summary>
    public int TodayHamCount { get; set; }

    /// <summary>
    /// Total detections today (spam + ham)
    /// </summary>
    public int TodayTotalDetections { get; set; }

    /// <summary>
    /// Spam rate today as percentage (0-100)
    /// </summary>
    public double TodaySpamRate { get; set; }

    #endregion

    #region Yesterday's Data (nullable - may not have data)

    /// <summary>
    /// Number of spam detections yesterday (null if no data)
    /// </summary>
    public int? YesterdaySpamCount { get; set; }

    /// <summary>
    /// Number of clean detections yesterday (null if no data)
    /// </summary>
    public int? YesterdayHamCount { get; set; }

    /// <summary>
    /// Total detections yesterday (null if no data)
    /// </summary>
    public int? YesterdayTotalDetections { get; set; }

    /// <summary>
    /// Spam rate yesterday as percentage (null if no data)
    /// </summary>
    public double? YesterdaySpamRate { get; set; }

    #endregion

    #region Change Metrics (computed from today vs yesterday)

    /// <summary>
    /// Change in spam count (Today - Yesterday). Positive = more spam today.
    /// Null if no yesterday data.
    /// </summary>
    public int? SpamCountChange { get; set; }

    /// <summary>
    /// Change in spam rate (percentage points). Positive = higher rate today.
    /// Null if no yesterday data.
    /// </summary>
    public double? SpamRateChange { get; set; }

    #endregion

    #region Computed Properties

    /// <summary>
    /// True if we have yesterday's data for comparison
    /// </summary>
    public bool HasYesterdayData => YesterdaySpamCount.HasValue;

    /// <summary>
    /// True if spam is improving (less spam today than yesterday)
    /// </summary>
    public bool IsImproving => SpamCountChange.HasValue && SpamCountChange.Value < 0;

    /// <summary>
    /// True if spam is worsening (more spam today than yesterday)
    /// </summary>
    public bool IsWorsening => SpamCountChange.HasValue && SpamCountChange.Value > 0;

    #endregion
}
