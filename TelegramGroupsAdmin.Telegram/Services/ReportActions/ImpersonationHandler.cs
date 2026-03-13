using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles impersonation alert actions (confirm/ban, dismiss, trust).
/// Fetches alert, executes moderation, atomically updates status.
/// </summary>
internal sealed class ImpersonationHandler(
    IReportsRepository reportsRepository,
    IBotModerationService moderationService,
    IReportCallbackContextRepository callbackContextRepo,
    ILogger<ImpersonationHandler> logger) : IImpersonationHandler
{
    public async Task<ReviewActionResult> ConfirmAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var (alert, error) = await FetchAlertAsync(alertId, ct);
        if (error != null) return error;

        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = alert!.SuspectedUser,
                Executor = executor,
                Reason = $"Impersonation alert #{alertId} confirmed - impersonating {alert.TargetUser.DisplayName}",
                Chat = alert.Chat
            },
            ct);

        if (!result.Success)
            return new ReviewActionResult(false, $"Ban failed: {result.ErrorMessage}");

        var statusResult = await UpdateStatusAtomicallyAsync(
            alertId, ReportStatus.Reviewed, executor, "confirm",
            $"Confirmed impersonation, banned from {result.ChatsAffected} chats", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Impersonation alert {AlertId}: User {User} banned as scammer by {Executor}",
            alertId, alert.SuspectedUser.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true,
            $"Confirmed scam - user banned from {result.ChatsAffected} chat(s)",
            ActionName: "Confirm");
    }

    public async Task<ReviewActionResult> DismissAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var (alert, error) = await FetchAlertAsync(alertId, ct);
        if (error != null) return error;

        var statusResult = await UpdateStatusAtomicallyAsync(
            alertId, ReportStatus.Dismissed, executor, "dismiss",
            "Dismissed as false positive", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Impersonation alert {AlertId} dismissed by {Executor}",
            alertId, executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true, "Alert dismissed", ActionName: "Dismiss");
    }

    public async Task<ReviewActionResult> TrustAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var (alert, error) = await FetchAlertAsync(alertId, ct);
        if (error != null) return error;

        var result = await moderationService.TrustUserAsync(
            new TrustIntent
            {
                User = alert!.SuspectedUser,
                Executor = executor,
                Reason = $"Trusted after impersonation review #{alertId}"
            },
            ct);

        if (!result.Success)
            return new ReviewActionResult(false, $"Trust failed: {result.ErrorMessage}");

        var statusResult = await UpdateStatusAtomicallyAsync(
            alertId, ReportStatus.Dismissed, executor, "trust",
            "User trusted - not impersonation", ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Impersonation alert {AlertId}: User {User} trusted by {Executor}",
            alertId, alert.SuspectedUser.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true,
            "User trusted - future impersonation alerts suppressed",
            ActionName: "Trust");
    }

    private async Task<(ImpersonationAlertRecord? Alert, ReviewActionResult? Error)> FetchAlertAsync(
        long alertId, CancellationToken ct)
    {
        var alert = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
        if (alert == null)
            return (null, new ReviewActionResult(false, $"Impersonation alert {alertId} not found"));

        if (alert.ReviewedAt.HasValue)
        {
            var handledBy = alert.ReviewedByEmail ?? "another admin";
            var verdict = alert.Verdict?.ToString() ?? "unknown";
            var time = alert.ReviewedAt.Value.UtcDateTime.ToString("g");
            return (null, new ReviewActionResult(false,
                $"Already handled by {handledBy} ({verdict}) at {time} UTC"));
        }

        return (alert, null);
    }

    private async Task<ReviewActionResult?> UpdateStatusAtomicallyAsync(
        long alertId, ReportStatus status, Actor executor, string actionTaken, string notes, CancellationToken ct)
    {
        var updated = await reportsRepository.TryUpdateStatusAsync(
            alertId, status, executor.GetDisplayText(), actionTaken, notes, ct);

        if (updated) return null;

        // Race condition: re-fetch for attribution
        var current = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
        if (current?.ReviewedAt.HasValue == true)
        {
            var handledBy = current.ReviewedByEmail ?? "another admin";
            var verdict = current.Verdict?.ToString() ?? "unknown";
            var time = current.ReviewedAt.Value.UtcDateTime.ToString("g");
            return new ReviewActionResult(false,
                $"Already handled by {handledBy} ({verdict}) at {time} UTC");
        }

        return new ReviewActionResult(false, $"Alert {alertId} could not be updated");
    }
}
