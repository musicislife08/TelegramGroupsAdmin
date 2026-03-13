namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Average response time for a single day
/// </summary>
public class DailyResponseTime
{
    public DateOnly Date { get; set; }
    public double AverageMs { get; set; }
    public int ActionCount { get; set; }
}
