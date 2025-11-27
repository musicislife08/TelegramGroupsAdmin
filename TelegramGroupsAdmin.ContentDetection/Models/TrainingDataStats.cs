namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Training data statistics for UI display
/// Used by TrainingData.razor to show spam/ham balance
/// </summary>
public class TrainingDataStats
{
    public int TotalSamples { get; set; }
    public int SpamSamples { get; set; }
    public int HamSamples { get; set; }
    public double SpamPercentage { get; set; }
    public Dictionary<string, int> SamplesBySource { get; set; } = new();
}
