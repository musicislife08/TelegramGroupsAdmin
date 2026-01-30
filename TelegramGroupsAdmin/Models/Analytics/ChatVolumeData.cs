namespace TelegramGroupsAdmin.Models.Analytics;

public record ChatVolumeData
{
    public string ChatName { get; init; } = "";
    public int Count { get; init; }
}
