namespace TelegramGroupsAdmin.Models.Analytics;

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
