using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Services;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;

/// <summary>
/// Aggregate endpoint for the Dashboard page.
/// Returns all data needed to render the page in a single HTTP call.
/// </summary>
public static class DashboardEndpoints
{
    private const int MaxRecentActivityItems = 10;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pages")
            .RequireAuthorization();

        // GET /api/pages/dashboard - Full page data for Dashboard
        group.MapGet("/dashboard", async (
            [FromServices] IAuthService authService,
            [FromServices] IMessageStatsService messageStatsService,
            [FromServices] IReportsRepository reportsRepo,
            [FromServices] IUserActionsRepository userActionsRepo,
            [FromServices] ITelegramUserRepository telegramUserRepo,
            [FromServices] IThresholdRecommendationsRepository thresholdRepo,
            CancellationToken ct) =>
        {
            // Load all dashboard data in parallel for fast loading
            var firstRunTask = authService.IsFirstRunAsync();
            var statsTask = messageStatsService.GetStatsAsync();
            var detectionStatsTask = messageStatsService.GetDetectionStatsAsync();
            var pendingReportsTask = reportsRepo.GetPendingCountAsync();
            var recentActionsTask = userActionsRepo.GetRecentAsync(MaxRecentActivityItems);
            var recommendationsTask = thresholdRepo.GetPendingCountAsync();
            var trustedUsersTask = telegramUserRepo.GetTrustedUsersAsync();
            var bannedUsersTask = telegramUserRepo.GetBannedUsersAsync();

            await Task.WhenAll(
                firstRunTask,
                statsTask,
                detectionStatsTask,
                pendingReportsTask,
                recentActionsTask,
                recommendationsTask,
                trustedUsersTask,
                bannedUsersTask);

            var isFirstRun = await firstRunTask;
            var stats = await statsTask;
            var detectionStats = await detectionStatsTask;
            var pendingReportsCount = await pendingReportsTask;
            var recentActions = await recentActionsTask;
            var pendingRecommendationsCount = await recommendationsTask;
            var trustedUsers = await trustedUsersTask;
            var bannedUsers = await bannedUsersTask;

            // Map to response DTOs
            var dashboardStats = new DashboardStats(
                stats.TotalMessages,
                stats.UniqueUsers,
                stats.PhotoCount,
                stats.OldestTimestamp,
                stats.NewestTimestamp
            );

            var recentActivityItems = recentActions
                .Select(a => new RecentActivityItem(
                    a.Id,
                    a.ActionType,
                    a.TargetDisplayName,
                    a.IssuedBy?.DisplayName ?? "System",
                    a.IssuedAt))
                .ToList();

            return Results.Ok(DashboardPageResponse.Ok(
                isFirstRun,
                dashboardStats,
                detectionStats,
                pendingReportsCount,
                pendingRecommendationsCount,
                bannedUsers.Count,
                trustedUsers.Count,
                recentActivityItems
            ));
        });

        return endpoints;
    }
}
