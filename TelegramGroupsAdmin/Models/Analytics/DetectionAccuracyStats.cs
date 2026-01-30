namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Comprehensive accuracy statistics including both false positives and false negatives.
/// Phase 5: Analytics for complete detection performance tracking (BUG-1 fix)
/// </summary>
public class DetectionAccuracyStats
{
    /// <summary>
    /// Daily breakdown of accuracy metrics
    /// </summary>
    public List<DailyAccuracy> DailyBreakdown { get; set; } = [];

    /// <summary>
    /// Total false positives in the period (system said spam, was actually ham)
    /// </summary>
    public int TotalFalsePositives { get; set; }

    /// <summary>
    /// Total false negatives in the period (system missed spam)
    /// </summary>
    public int TotalFalseNegatives { get; set; }

    /// <summary>
    /// Total detections in the period (excludes manual reviews)
    /// </summary>
    public int TotalDetections { get; set; }

    /// <summary>
    /// Total correctly classified messages
    /// </summary>
    public int TotalCorrect => TotalDetections - TotalFalsePositives - TotalFalseNegatives;

    /// <summary>
    /// False positive rate as percentage (0-100)
    /// </summary>
    public double FalsePositivePercentage { get; set; }

    /// <summary>
    /// False negative rate as percentage (0-100)
    /// </summary>
    public double FalseNegativePercentage { get; set; }

    /// <summary>
    /// Overall accuracy as percentage (0-100)
    /// </summary>
    public double OverallAccuracy => TotalDetections > 0
        ? (TotalCorrect / (double)TotalDetections * 100.0)
        : 0;
}

/// <summary>
/// Accuracy metrics for a single day
/// </summary>
public class DailyAccuracy
{
    public DateOnly Date { get; set; }
    public int TotalDetections { get; set; }
    public int FalsePositiveCount { get; set; }
    public int FalseNegativeCount { get; set; }
    public double FalsePositivePercentage { get; set; }
    public double FalseNegativePercentage { get; set; }
    public double Accuracy { get; set; }
}
