using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Dashboard page initial load.
/// Bundles all data needed to render the page in a single HTTP call.
/// </summary>
public record DashboardPageResponse(
    bool IsFirstRun,
    DashboardStats Stats,
    DetectionStats? DetectionStats,
    int PendingReportsCount,
    int PendingRecommendationsCount,
    int ActiveBansCount,
    int TrustedUsersCount,
    List<RecentActivityItem> RecentActivity
);
