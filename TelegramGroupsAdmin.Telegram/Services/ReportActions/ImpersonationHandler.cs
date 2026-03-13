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
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var alert = fetch.Value!;

        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = alert.SuspectedUser,
                Executor = executor,
                Reason = $"Impersonation alert #{alertId} confirmed - impersonating {alert.TargetUser.DisplayName}",
                Chat = alert.Chat
            },
            ct);

        if (!result.Success)
            return new ReviewActionResult(false, $"Ban failed: {result.ErrorMessage}");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Reviewed, executor, "confirm",
            $"Confirmed impersonation, banned from {result.ChatsAffected} chats",
            async () =>
            {
                var current = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.Verdict?.ToString(), current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
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
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Dismissed, executor, "dismiss",
            "Dismissed as false positive",
            async () =>
            {
                var current = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.Verdict?.ToString(), current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Impersonation alert {AlertId} dismissed by {Executor}",
            alertId, executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true, "Alert dismissed", ActionName: "Dismiss");
    }

    public async Task<ReviewActionResult> TrustAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var alert = fetch.Value!;

        var result = await moderationService.TrustUserAsync(
            new TrustIntent
            {
                User = alert.SuspectedUser,
                Executor = executor,
                Reason = $"Trusted after impersonation review #{alertId}"
            },
            ct);

        if (!result.Success)
            return new ReviewActionResult(false, $"Trust failed: {result.ErrorMessage}");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Dismissed, executor, "trust",
            "User trusted - not impersonation",
            async () =>
            {
                var current = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.Verdict?.ToString(), current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Impersonation alert {AlertId}: User {User} trusted by {Executor}",
            alertId, alert.SuspectedUser.ToLogInfo(), executor.DisplayName);

        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true,
            "User trusted - future impersonation alerts suppressed",
            ActionName: "Trust");
    }

    private async Task<FetchResult<ImpersonationAlertRecord>> FetchAlertAsync(long alertId, CancellationToken ct)
    {
        var alert = await reportsRepository.GetImpersonationAlertAsync(alertId, ct);
        if (alert == null)
            return FetchResult<ImpersonationAlertRecord>.Fail($"Impersonation alert {alertId} not found");

        var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
            alert.ReviewedByEmail, alert.Verdict?.ToString(), alert.ReviewedAt);
        if (alreadyHandled != null)
            return FetchResult<ImpersonationAlertRecord>.Handled(alreadyHandled.Message);

        return FetchResult<ImpersonationAlertRecord>.Ok(alert);
    }
}
