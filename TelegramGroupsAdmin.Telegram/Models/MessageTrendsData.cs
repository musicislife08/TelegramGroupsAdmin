namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message trends analytics data (UX-2, UX-2.1)
/// </summary>
public record MessageTrendsData
{
    // Existing metrics (UX-2)
    public int TotalMessages { get; init; }
    public double DailyAverage { get; init; }
    public int UniqueUsers { get; init; }
    public double SpamPercentage { get; init; }
    public List<DailyVolumeData> DailyVolume { get; init; } = [];
    public List<DailyVolumeData> DailySpam { get; init; } = [];
    public List<DailyVolumeData> DailyHam { get; init; } = [];
    public List<ChatVolumeData> PerChatVolume { get; init; } = [];

    // New metrics (UX-2.1)
    public PeakActivitySummary? PeakActivity { get; init; }
    public SpamSeasonalitySummary? SpamSeasonality { get; init; }
    public WeekOverWeekGrowth? WeekOverWeekGrowth { get; init; }
    public List<TopActiveUser> TopActiveUsers { get; init; } = [];
    public TrustedUserBreakdown? TrustedUserBreakdown { get; init; }
    public List<DailyActiveUsersData> DailyActiveUsers { get; init; } = [];
}
