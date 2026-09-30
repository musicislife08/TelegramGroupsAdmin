using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles content report actions (spam, ban, warn, dismiss, clean).
/// Only Spam and Clean change the message's verdict; Ban and Warn are moderation-only and
/// Dismiss is an acknowledgement that leaves the existing verdict in place.
/// Fetches report + message, executes moderation, atomically updates status, audits, and cleans up.
/// </summary>
internal sealed class ContentReportHandler(
    IReportsRepository reportsRepository,
    IMessageHistoryRepository messageRepository,
    IBotModerationService moderationService,
    IAuditService auditService,
    IBotMessageService botMessageService,
    ITrainingHandler trainingHandler,
    ILogger<ContentReportHandler> logger) : IContentReportHandler
{
    private sealed record ContentFetchData(Report Report, MessageRecord Message);

    /// <summary>
    /// What an action's moderation step produced: either a failure to return as-is, or the status notes,
    /// audit text and success message for the rest of the shared flow.
    /// </summary>
    private sealed record ActionOutcome(ReviewActionResult? Failure, string StatusNotes = "", string AuditText = "", string SuccessMessage = "")
    {
        public static ActionOutcome Failed(string message) => new(new ReviewActionResult(false, message));
    }

    public Task<ReviewActionResult> SpamAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => RunActionAsync(reportId, executor, ReportAction.Spam, "spam", "Spam", async (report, message) =>
        {
            var result = await moderationService.MarkAsSpamAndBanAsync(
                new SpamBanIntent
                {
                    User = message.User,
                    Chat = message.Chat,
                    MessageId = report.MessageId,
                    Executor = executor,
                    Source = VerdictSource.ReviewSpam,
                    Reason = $"Report #{reportId} - spam/abuse"
                },
                cancellationToken);

            if (!result.Success)
            {
                logger.LogError("Spam action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
                return ActionOutcome.Failed($"Spam action failed: {result.ErrorMessage}");
            }

            return new ActionOutcome(null,
                $"User banned from {result.ChatsAffected} chats, message deleted",
                $"Marked as spam (report #{reportId}, affected {result.ChatsAffected} chats)",
                $"Marked as spam - user banned from {result.ChatsAffected} chat(s)");
        }, cancellationToken);

    public Task<ReviewActionResult> BanAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => RunActionAsync(reportId, executor, ReportAction.Ban, "ban", "Ban", async (report, message) =>
        {
            var result = await moderationService.BanUserAsync(
                new BanIntent
                {
                    User = message.User,
                    Executor = executor,
                    Reason = $"Report #{reportId} - spam/abuse",
                    MessageId = report.MessageId,
                    Chat = message.Chat,
                    OriginReportId = reportId
                },
                cancellationToken);

            if (!result.Success)
            {
                logger.LogError("Ban action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
                return ActionOutcome.Failed($"Ban failed: {result.ErrorMessage}");
            }

            // Delete the offending message (best-effort)
            try
            {
                await botMessageService.DeleteAndMarkMessageAsync(
                    report.Chat.Id, report.MessageId,
                    deletionSource: "ban_action", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete message {MessageId} (may already be deleted)", report.MessageId);
            }

            return new ActionOutcome(null,
                $"User banned from {result.ChatsAffected} chats",
                $"Banned user (report #{reportId}, affected {result.ChatsAffected} chats)",
                $"User banned from {result.ChatsAffected} chat(s)");
        }, cancellationToken);

    public Task<ReviewActionResult> WarnAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => RunActionAsync(reportId, executor, ReportAction.Warn, "warn", "Warn", async (report, message) =>
        {
            var result = await moderationService.WarnUserAsync(
                new WarnIntent
                {
                    User = message.User,
                    Chat = message.Chat,
                    Executor = executor,
                    Reason = $"Report #{reportId} - inappropriate behavior",
                    MessageId = report.MessageId,
                    OriginReportId = reportId
                },
                cancellationToken);

            if (!result.Success)
            {
                logger.LogError("Warn action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
                return ActionOutcome.Failed($"Warning failed: {result.ErrorMessage}");
            }

            return new ActionOutcome(null,
                $"User {message.User.DisplayName} warned",
                $"Warned user (report #{reportId}, {result.WarningCount} warnings total)",
                $"Warning issued (warning #{result.WarningCount})");
        }, cancellationToken);

    public async Task<ReviewActionResult> DismissAsync(long reportId, Actor executor, string? reason, CancellationToken cancellationToken)
    {
        var report = await reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
            return new ReviewActionResult(false, $"Report {reportId} not found");

        var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
            report.ReviewedBy, report.ActionTaken, report.ReviewedAt);
        if (alreadyHandled != null) return alreadyHandled;

        var statusResult = await ClaimReportAsync(
            reportId, ReportStatus.Dismissed, executor, "dismiss", reason ?? "No action needed", cancellationToken);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, null,
            $"Dismissed report #{reportId} ({reason ?? "no action taken"})", cancellationToken);

        await CleanupContentReportAsync(report, ReportAction.Dismiss, cancellationToken);

        logger.LogInformation("Dismissed report {ReportId} (reason: {Reason})", reportId, reason ?? "none");

        return new ReviewActionResult(true, "Report dismissed", ActionName: "Dismiss");
    }

    public Task<ReviewActionResult> CleanAsync(long reportId, Actor executor, CancellationToken cancellationToken)
        => RunActionAsync(reportId, executor, ReportAction.Clean, "clean", "Clean", async (report, _) =>
        {
            // The clean decision is the point of this action and runs before the report is claimed,
            // so a failure leaves the report pending for another try.
            await trainingHandler.CreateHamSampleAsync(
                report.MessageId, report.Chat, executor, VerdictSource.ReviewClean,
                $"Report #{reportId} - marked clean", cancellationToken);

            return new ActionOutcome(null, "Message marked clean", $"Marked clean (report #{reportId})", "Marked clean");
        }, cancellationToken);

    /// <summary>
    /// The shared shape of a content-report action: fetch the pending report and its message, run the
    /// action's moderation step, claim the report (a concurrent reviewer wins with attribution), audit
    /// against the reported user, then clean up the report's messages.
    /// </summary>
    private async Task<ReviewActionResult> RunActionAsync(
        long reportId, Actor executor, ReportAction action, string actionKey, string actionName,
        Func<Report, MessageRecord, Task<ActionOutcome>> act, CancellationToken cancellationToken)
    {
        var fetch = await FetchReportAndMessageAsync(reportId, cancellationToken);
        if (!fetch.Success)
            return new ReviewActionResult(false, fetch.ErrorMessage!, IsAlreadyHandled: fetch.Status == FetchStatus.AlreadyHandled);

        var report = fetch.Value!.Report;
        var message = fetch.Value.Message;

        var outcome = await act(report, message);
        if (outcome.Failure != null)
            return outcome.Failure;

        var statusResult = await ClaimReportAsync(
            reportId, ReportStatus.Reviewed, executor, actionKey, outcome.StatusNotes, cancellationToken);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(message.User),
            outcome.AuditText, cancellationToken);

        await CleanupContentReportAsync(report, action, cancellationToken);

        return new ReviewActionResult(true, outcome.SuccessMessage, ActionName: actionName);
    }

    /// <summary>
    /// Atomically moves the report out of Pending. Returns null on success, or the "already handled by"
    /// result when another reviewer claimed it first.
    /// </summary>
    private Task<ReviewActionResult?> ClaimReportAsync(
        long reportId, ReportStatus status, Actor executor, string actionKey, string notes, CancellationToken cancellationToken)
        => ReportStatusHelper.TryUpdateStatusAsync(
            reportsRepository, reportId, status, executor, actionKey, notes,
            async () =>
            {
                var current = await reportsRepository.GetContentReportAsync(reportId, cancellationToken);
                return current != null
                    ? ReportStatusHelper.CheckAlreadyHandled(current.ReviewedBy, current.ActionTaken, current.ReviewedAt)
                    : new ReviewActionResult(false, $"Report {reportId} could not be updated");
            },
            cancellationToken);

    private async Task<FetchResult<ContentFetchData>> FetchReportAndMessageAsync(
        long reportId, CancellationToken cancellationToken)
    {
        var report = await reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
            return FetchResult<ContentFetchData>.Fail($"Report {reportId} not found");

        // Check before expensive message lookup
        var alreadyHandled = ReportStatusHelper.CheckAlreadyHandled(
            report.ReviewedBy, report.ActionTaken, report.ReviewedAt);
        if (alreadyHandled != null)
            return FetchResult<ContentFetchData>.Handled(alreadyHandled.Message);

        var message = await messageRepository.GetMessageAsync(report.MessageId, report.Chat.Id, cancellationToken);
        if (message == null)
            return FetchResult<ContentFetchData>.Fail($"Message {report.MessageId} not found");

        return FetchResult<ContentFetchData>.Ok(new ContentFetchData(report, message));
    }

    private async Task CleanupContentReportAsync(Report report, ReportAction action, CancellationToken cancellationToken)
    {
        // Delete /report command message
        if (report.ReportCommandMessageId.HasValue)
        {
            try
            {
                await botMessageService.DeleteAndMarkMessageAsync(
                    report.Chat.Id, report.ReportCommandMessageId.Value,
                    deletionSource: "report_reviewed", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not delete /report command message {MessageId} (may already be deleted)",
                    report.ReportCommandMessageId);
            }
        }

        // Dismiss and Clean leave the message up: reply to it so the chat sees it was reviewed
        if (action is ReportAction.Dismiss or ReportAction.Clean)
        {
            try
            {
                await botMessageService.SendAndSaveMessageAsync(
                    report.Chat.Id,
                    "\u2713 This message was reviewed and no action was taken",
                    parseMode: ParseMode.None,
                    replyParameters: new ReplyParameters { MessageId = report.MessageId },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not reply to reported message {MessageId} (may be deleted)", report.MessageId);
            }
        }
    }

}
