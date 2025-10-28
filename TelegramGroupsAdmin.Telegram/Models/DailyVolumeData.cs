namespace TelegramGroupsAdmin.Telegram.Models;

public record DailyVolumeData
{
    public DateOnly Date { get; init; }
    public int Count { get; init; }
}
