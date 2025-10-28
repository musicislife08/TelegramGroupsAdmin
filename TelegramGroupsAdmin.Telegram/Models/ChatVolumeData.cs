namespace TelegramGroupsAdmin.Telegram.Models;

public record ChatVolumeData
{
    public string ChatName { get; init; } = "";
    public int Count { get; init; }
}
