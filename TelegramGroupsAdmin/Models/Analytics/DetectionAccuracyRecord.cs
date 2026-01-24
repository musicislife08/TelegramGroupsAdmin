namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Individual detection record with pre-computed FP/FN flags from the detection_accuracy view.
/// Different from DetectionAccuracyStats which provides aggregated statistics.
/// </summary>
public class DetectionAccuracyRecord
{
    /// <summary>
    /// Detection result ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Message ID this detection relates to
    /// </summary>
    public long MessageId { get; set; }

    /// <summary>
    /// Full timestamp of the detection (for timezone-aware grouping)
    /// </summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>
    /// Date of the detection (UTC)
    /// </summary>
    public DateOnly DetectionDate { get; set; }

    /// <summary>
    /// Original automated classification (true = spam, false = ham)
    /// </summary>
    public bool OriginalClassification { get; set; }

    /// <summary>
    /// True if this was a false positive (detected as spam but manually corrected to ham)
    /// </summary>
    public bool IsFalsePositive { get; set; }

    /// <summary>
    /// True if this was a false negative (detected as ham but manually corrected to spam)
    /// </summary>
    public bool IsFalseNegative { get; set; }

    /// <summary>
    /// True if this detection was correct (not FP and not FN)
    /// </summary>
    public bool IsCorrect => !IsFalsePositive && !IsFalseNegative;
}
