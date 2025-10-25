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

public record DailyVolumeData
{
    public DateOnly Date { get; init; }
    public int Count { get; init; }
}

public record ChatVolumeData
{
    public string ChatName { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>
/// User message info for cross-chat ban cleanup (FEATURE-4.23)
/// </summary>
public record UserMessageInfo
{
    public long MessageId { get; init; }
    public long ChatId { get; init; }
}
