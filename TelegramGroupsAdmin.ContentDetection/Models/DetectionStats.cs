namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Detection statistics for UI display
/// </summary>
public class DetectionStats
{
    public int TotalDetections { get; set; }
    public int SpamDetected { get; set; }
    public double SpamPercentage { get; set; }
    public double AverageConfidence { get; set; }
    public int Last24hDetections { get; set; }
    public int Last24hSpam { get; set; }
    public double Last24hSpamPercentage { get; set; }
}
