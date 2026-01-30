namespace TelegramGroupsAdmin.Models.Analytics;

public record DailyVolumeData
{
    public DateOnly Date { get; init; }
    public int Count { get; init; }
}
