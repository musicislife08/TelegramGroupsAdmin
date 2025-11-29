namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Daily detection counts for trend visualization.
/// Phase 5: Analytics charting
/// </summary>
public class DailyDetectionTrend
{
    /// <summary>
    /// Date of the aggregation
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Number of spam detections on this day
    /// </summary>
    public int SpamCount { get; set; }

    /// <summary>
    /// Number of ham (legitimate) detections on this day
    /// </summary>
    public int HamCount { get; set; }

    /// <summary>
    /// Total detections on this day
    /// </summary>
    public int TotalCount => SpamCount + HamCount;

    /// <summary>
    /// Spam rate as percentage (0-100)
    /// </summary>
    public double SpamPercentage => TotalCount > 0 ? (SpamCount / (double)TotalCount * 100.0) : 0;
}
