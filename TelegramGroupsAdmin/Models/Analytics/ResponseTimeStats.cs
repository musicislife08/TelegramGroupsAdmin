namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Statistics about response time from spam detection to moderation action.
/// Phase 5: Analytics for testing validation
/// </summary>
public class ResponseTimeStats
{
    /// <summary>
    /// Daily average response times
    /// </summary>
    public List<DailyResponseTime> DailyAverages { get; set; } = [];

    /// <summary>
    /// Overall average response time in milliseconds
    /// </summary>
    public double AverageMs { get; set; }

    /// <summary>
    /// Median response time in milliseconds (50th percentile)
    /// </summary>
    public double MedianMs { get; set; }

    /// <summary>
    /// 95th percentile response time in milliseconds
    /// </summary>
    public double P95Ms { get; set; }

    /// <summary>
    /// Total actions measured
    /// </summary>
    public int TotalActions { get; set; }
}

/// <summary>
/// Average response time for a single day
/// </summary>
public class DailyResponseTime
{
    public DateOnly Date { get; set; }
    public double AverageMs { get; set; }
    public int ActionCount { get; set; }
}
