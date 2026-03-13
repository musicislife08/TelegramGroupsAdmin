using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles profile scan alert actions (ban, kick, allow).
/// Fetches alert, executes moderation, atomically updates status, and auto-closes sibling alerts.
/// </summary>
internal sealed class ProfileScanHandler(
    IReportsRepository reportsRepository,
    IBotModerationService moderationService,
    IWelcomeResponsesRepository welcomeResponsesRepository,
    IWelcomeAdmissionHandler welcomeAdmissionHandler,
    IReportCallbackContextRepository callbackContextRepo,
    ILogger<ProfileScanHandler> logger) : IProfileScanHandler
{
    public async Task<ReviewActionResult> BanAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var alert = fetch.Value!;

        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = alert.User,
                Executor = executor,
                Reason = $"Profile scan alert #{alertId} confirmed \u2014 score {alert.Score:F1}",
                Chat = alert.Chat
            },
            ct);

        if (!result.Success)
            return new ReviewActionResult(false, $"Ban failed: {result.ErrorMessage}");

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Reviewed, executor, "ban",
            $"Banned after profile scan review (affected {result.ChatsAffected} chats)",
            async () =>
            {
                var current = await reportsRepository.GetProfileScanAlertAsync(alertId, ct);
                return current?.ReviewedAt.HasValue == true
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Profile scan alert {AlertId}: User {User} banned by {Executor}",
            alertId, alert.User.ToLogInfo(), executor.DisplayName);

        await CleanupSiblingAlertsAsync(alert, "Ban", ct);
        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true,
            $"User banned from {result.ChatsAffected} chat(s)",
            ActionName: "Ban");
    }

    public async Task<ReviewActionResult> KickAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var alert = fetch.Value!;

        if (alert.Chat.Id != 0)
        {
            var result = await moderationService.KickUserFromChatAsync(
                new KickIntent
                {
                    User = alert.User,
                    Chat = alert.Chat,
                    Executor = executor,
                    Reason = $"Profile scan alert #{alertId} \u2014 kicked after review",
                    RevokeMessages = false
                },
                ct);

            if (!result.Success)
                return new ReviewActionResult(false, $"Kick failed: {result.ErrorMessage}");
        }

        var notes = alert.Chat.Id == 0
            ? "Kick not applicable (global alert)"
            : "Kicked from chat after review";

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Reviewed, executor, "kick", notes,
            async () =>
            {
                var current = await reportsRepository.GetProfileScanAlertAsync(alertId, ct);
                return current?.ReviewedAt.HasValue == true
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
        if (statusResult != null) return statusResult;

        logger.LogInformation("Profile scan alert {AlertId}: User {User} kicked by {Executor}",
            alertId, alert.User.ToLogInfo(), executor.DisplayName);

        await CleanupSiblingAlertsAsync(alert, "Kick", ct);
        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        var message = alert.Chat.Id == 0
            ? "Alert resolved (no chat to kick from)"
            : "User kicked from chat";
        return new ReviewActionResult(true, message, ActionName: "Kick");
    }

    public async Task<ReviewActionResult> AllowAsync(long alertId, Actor executor, CancellationToken ct)
    {
        var fetch = await FetchAlertAsync(alertId, ct);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var alert = fetch.Value!;

        // If user was kicked by welcome timeout, just close the report
        var welcomeResponse = await welcomeResponsesRepository.GetByUserAndChatAsync(
            alert.User.Id, alert.Chat.Id, ct);

        string message;
        if (welcomeResponse is { Response: Models.WelcomeResponseType.Timeout })
        {
            logger.LogDebug("Profile scan Allow for timed-out user {User} \u2014 closing report without admission",
                alert.User.ToLogDebug());
            message = "User already left \u2014 alert dismissed";
        }
        else
        {
            // Clear the profile gate and attempt admission
            var admissionResult = await welcomeAdmissionHandler.TryAdmitUserAsync(
                alert.User, alert.Chat, executor,
                $"Profile scan alert #{alertId} allowed by admin",
                ct);

            logger.LogInformation("Profile scan alert {AlertId}: User {User} allowed by {Executor} (admission: {Result})",
                alertId, alert.User.ToLogInfo(), executor.DisplayName, admissionResult);

            message = admissionResult == AdmissionResult.Admitted
                ? "User allowed \u2014 permissions restored"
                : "User allowed \u2014 awaiting welcome gate completion";
        }

        var statusResult = await ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, alertId, ReportStatus.Dismissed, executor, "allow",
            "User allowed after profile scan review",
            async () =>
            {
                var current = await reportsRepository.GetProfileScanAlertAsync(alertId, ct);
                return current?.ReviewedAt.HasValue == true
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedByEmail, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Alert {alertId} could not be updated");
            },
            ct);
        if (statusResult != null) return statusResult;

        await CleanupSiblingAlertsAsync(alert, "Allow", ct);
        await callbackContextRepo.DeleteByReportIdAsync(alertId, ct);

        return new ReviewActionResult(true, message, ActionName: "Allow");
    }

    private async Task<FetchResult<ProfileScanAlertRecord>> FetchAlertAsync(long alertId, CancellationToken ct)
    {
        var alert = await reportsRepository.GetProfileScanAlertAsync(alertId, ct);
        if (alert == null)
            return FetchResult<ProfileScanAlertRecord>.Fail($"Profile scan alert {alertId} not found");

        var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
            alert.ReviewedByEmail, alert.ActionTaken, alert.ReviewedAt);
        if (alreadyHandled != null)
            return FetchResult<ProfileScanAlertRecord>.Handled(alreadyHandled.Message);

        return FetchResult<ProfileScanAlertRecord>.Ok(alert);
    }

    private async Task CleanupSiblingAlertsAsync(ProfileScanAlertRecord alert, string actionName, CancellationToken ct)
    {
        var siblingAlerts = await reportsRepository.GetPendingProfileScanAlertsForUserAsync(alert.User.Id, ct);
        if (siblingAlerts.Count == 0) return;

        var note = $"Auto-resolved: user {actionName.ToLowerInvariant()} via profile scan alert #{alert.Id}";

        foreach (var sibling in siblingAlerts)
        {
            await reportsRepository.TryUpdateStatusAsync(
                sibling.Id, ReportStatus.Reviewed,
                Actor.ProfileScan.GetDisplayText(),
                $"Auto-{actionName}",
                note, ct);

            logger.LogDebug("Auto-closed sibling profile scan alert #{SiblingId} for {User}",
                sibling.Id, alert.User.ToLogDebug());
        }

        logger.LogInformation("Profile scan cleanup: auto-closed {Count} sibling alert(s) for {User}",
            siblingAlerts.Count, alert.User.ToLogInfo());
    }
}
