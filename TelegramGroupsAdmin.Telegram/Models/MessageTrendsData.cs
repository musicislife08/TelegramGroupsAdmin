namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message trends analytics data (UX-2)
/// </summary>
public record MessageTrendsData
{
    public int TotalMessages { get; init; }
    public double DailyAverage { get; init; }
    public int UniqueUsers { get; init; }
    public double SpamPercentage { get; init; }
    public List<DailyVolumeData> DailyVolume { get; init; } = new();
    public List<DailyVolumeData> DailySpam { get; init; } = new();
    public List<DailyVolumeData> DailyHam { get; init; } = new();
    public List<ChatVolumeData> PerChatVolume { get; init; } = new();
}
