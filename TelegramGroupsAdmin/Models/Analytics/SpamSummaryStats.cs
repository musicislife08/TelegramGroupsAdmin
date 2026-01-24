namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Spam detection summary statistics for analytics dashboard.
/// Analytics-specific type (operational code uses ContentDetection.Models.DetectionStats)
/// </summary>
public class SpamSummaryStats
{
    public int TotalDetections { get; set; }
    public int SpamDetected { get; set; }
    public double SpamPercentage { get; set; }
    public double AverageConfidence { get; set; }
    public int Last24hDetections { get; set; }
    public int Last24hSpam { get; set; }
    public double Last24hSpamPercentage { get; set; }
}
