using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Dashboard page initial load.
/// Bundles all data needed to render the page in a single HTTP call.
/// Implements IApiResponse for unified error handling.
/// </summary>
public record DashboardPageResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public bool IsFirstRun { get; init; }
    public DashboardStats? Stats { get; init; }
    public DetectionStats? DetectionStats { get; init; }
    public int PendingReportsCount { get; init; }
    public int PendingRecommendationsCount { get; init; }
    public int ActiveBansCount { get; init; }
    public int TrustedUsersCount { get; init; }
    public List<RecentActivityItem>? RecentActivity { get; init; }

    public static DashboardPageResponse Ok(
        bool isFirstRun,
        DashboardStats stats,
        DetectionStats? detectionStats,
        int pendingReportsCount,
        int pendingRecommendationsCount,
        int activeBansCount,
        int trustedUsersCount,
        List<RecentActivityItem> recentActivity) => new()
    {
        Success = true,
        IsFirstRun = isFirstRun,
        Stats = stats,
        DetectionStats = detectionStats,
        PendingReportsCount = pendingReportsCount,
        PendingRecommendationsCount = pendingRecommendationsCount,
        ActiveBansCount = activeBansCount,
        TrustedUsersCount = trustedUsersCount,
        RecentActivity = recentActivity
    };

    public static DashboardPageResponse Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
