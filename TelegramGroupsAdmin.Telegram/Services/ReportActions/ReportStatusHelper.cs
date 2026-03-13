using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Shared helper for atomic report status updates and already-handled formatting.
/// Eliminates duplicated update/format logic across all 4 report handlers.
/// </summary>
internal static class ReportStatusHelper
{
    internal static async Task<ReviewActionResult?> TryUpdateStatusAsync(
        IReportsRepository reportsRepository,
        long reportId, ReportStatus status, Actor executor,
        string actionTaken, string notes,
        Func<Task<ReviewActionResult?>> onRaceLost,
        CancellationToken ct)
    {
        var updated = await reportsRepository.TryUpdateStatusAsync(
            reportId, status, executor.GetDisplayText(), actionTaken, notes, ct);
        return updated ? null : await onRaceLost();
    }

    internal static ReviewActionResult? CheckAlreadyHandled(
        string? reviewedBy, string? action, DateTimeOffset? reviewedAt)
    {
        if (!reviewedAt.HasValue) return null;
        return FormatAlreadyHandled(reviewedBy, action, reviewedAt);
    }

    internal static ReviewActionResult FormatAlreadyHandled(
        string? handledBy, string? action, DateTimeOffset? reviewedAt)
    {
        var by = handledBy ?? "another admin";
        var act = action ?? "unknown";
        var time = reviewedAt?.UtcDateTime.ToString("g") ?? "unknown";
        return new ReviewActionResult(false,
            $"Already handled by {by} ({act}) at {time} UTC", IsAlreadyHandled: true);
    }
}
