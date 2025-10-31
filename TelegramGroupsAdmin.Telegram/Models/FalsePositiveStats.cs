namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Statistics about false positive detections (spam → ham corrections).
/// Phase 5: Analytics for testing validation
/// </summary>
public class FalsePositiveStats
{
    /// <summary>
    /// Daily breakdown of false positives
    /// </summary>
    public List<DailyFalsePositive> DailyBreakdown { get; set; } = [];

    /// <summary>
    /// Overall false positive rate as percentage (0-100)
    /// </summary>
    public double OverallPercentage { get; set; }

    /// <summary>
    /// Total false positives in the period
    /// </summary>
    public int TotalFalsePositives { get; set; }

    /// <summary>
    /// Total detections in the period
    /// </summary>
    public int TotalDetections { get; set; }
}
