namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Hourly aggregated detection statistics from the hourly_detection_stats view.
/// Can be rolled up to daily stats in C# or used directly for peak hour analysis.
/// </summary>
public class HourlyDetectionStats
{
    /// <summary>
    /// Date of the detections (UTC)
    /// </summary>
    public DateOnly DetectionDate { get; set; }

    /// <summary>
    /// Hour of the day (0-23)
    /// </summary>
    public int DetectionHour { get; set; }

    /// <summary>
    /// Total detections in this hour
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Number of spam detections
    /// </summary>
    public int SpamCount { get; set; }

    /// <summary>
    /// Number of ham (not spam) detections
    /// </summary>
    public int HamCount { get; set; }

    /// <summary>
    /// Number of manual classifications (reviews)
    /// </summary>
    public int ManualCount { get; set; }

    /// <summary>
    /// Average confidence score for this hour (nullable if no detections)
    /// </summary>
    public double? AvgConfidence { get; set; }
}
