namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Effectiveness statistics for a single spam detection method.
/// Phase 5: Analytics for comparing the 9 detection algorithms
/// </summary>
public class DetectionMethodStats
{
    /// <summary>
    /// Detection method name (e.g., "stop_words", "cas", "openai", etc.)
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Total checks performed by this method
    /// </summary>
    public int TotalChecks { get; set; }

    /// <summary>
    /// Number of times this method detected spam
    /// </summary>
    public int SpamDetected { get; set; }

    /// <summary>
    /// Spam detection rate as percentage (0-100)
    /// </summary>
    public double SpamPercentage { get; set; }

    /// <summary>
    /// Average confidence score for this method (0-100)
    /// </summary>
    public double AverageConfidence { get; set; }

    /// <summary>
    /// Number of false positives (spam → ham corrections)
    /// </summary>
    public int FalsePositives { get; set; }

    /// <summary>
    /// False positive rate as percentage (0-100)
    /// </summary>
    public double FalsePositiveRate { get; set; }

    /// <summary>
    /// Number of false negatives (ham → spam corrections)
    /// </summary>
    public int FalseNegatives { get; set; }

    /// <summary>
    /// False negative rate as percentage (0-100)
    /// </summary>
    public double FalseNegativeRate { get; set; }
}
