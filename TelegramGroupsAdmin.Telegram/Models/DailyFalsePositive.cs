namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// False positive count for a single day
/// </summary>
public class DailyFalsePositive
{
    public DateOnly Date { get; set; }
    public int FalsePositiveCount { get; set; }
    public int TotalDetections { get; set; }
    public double Percentage { get; set; }
}
