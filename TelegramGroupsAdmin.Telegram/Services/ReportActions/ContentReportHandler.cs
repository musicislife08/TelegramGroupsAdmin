using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.ReportActions;

/// <summary>
/// Handles content report actions (spam, ban, warn, dismiss).
/// Fetches report + message, executes moderation, atomically updates status, audits, and cleans up.
/// </summary>
internal sealed class ContentReportHandler(
    IReportsRepository reportsRepository,
    IMessageHistoryRepository messageRepository,
    IBotModerationService moderationService,
    IAuditService auditService,
    IBotMessageService botMessageService,
    IReportCallbackContextRepository callbackContextRepo,
    ILogger<ContentReportHandler> logger) : IContentReportHandler
{
    public async Task<ReviewActionResult> SpamAsync(long reportId, Actor executor, CancellationToken ct)
    {
        var (report, message, error) = await FetchReportAndMessageAsync(reportId, ct);
        if (error != null) return error;

        var result = await moderationService.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = message!.User,
                Chat = message.Chat,
                MessageId = report!.MessageId,
                Executor = executor,
                Reason = $"Report #{reportId} - spam/abuse"
            },
            ct);

        if (!result.Success)
        {
            logger.LogError("Spam action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            return new ReviewActionResult(false, $"Spam action failed: {result.ErrorMessage}");
        }

        var statusResult = await UpdateStatusAtomicallyAsync(
            reportId, ReportStatus.Reviewed, executor, "spam",
            $"User banned from {result.ChatsAffected} chats, message deleted", ct);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(message.User),
            $"Marked as spam (report #{reportId}, affected {result.ChatsAffected} chats)", ct);

        await CleanupContentReportAsync(report, actionName: "Spam", ct);

        return new ReviewActionResult(true,
            $"Marked as spam - user banned from {result.ChatsAffected} chat(s)",
            ActionName: "Spam");
    }

    public async Task<ReviewActionResult> BanAsync(long reportId, Actor executor, CancellationToken ct)
    {
        var (report, message, error) = await FetchReportAndMessageAsync(reportId, ct);
        if (error != null) return error;

        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = message!.User,
                Executor = executor,
                Reason = $"Report #{reportId} - spam/abuse",
                MessageId = report!.MessageId,
                Chat = message.Chat
            },
            ct);

        if (!result.Success)
        {
            logger.LogError("Ban action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            return new ReviewActionResult(false, $"Ban failed: {result.ErrorMessage}");
        }

        // Delete the offending message (best-effort)
        try
        {
            await botMessageService.DeleteAndMarkMessageAsync(
                report.Chat.Id, report.MessageId,
                deletionSource: "ban_action", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete message {MessageId} (may already be deleted)", report.MessageId);
        }

        var statusResult = await UpdateStatusAtomicallyAsync(
            reportId, ReportStatus.Reviewed, executor, "ban",
            $"User banned from {result.ChatsAffected} chats", ct);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(message.User),
            $"Banned user (report #{reportId}, affected {result.ChatsAffected} chats)", ct);

        await CleanupContentReportAsync(report, actionName: "Ban", ct);

        return new ReviewActionResult(true,
            $"User banned from {result.ChatsAffected} chat(s)",
            ActionName: "Ban");
    }

    public async Task<ReviewActionResult> WarnAsync(long reportId, Actor executor, CancellationToken ct)
    {
        var (report, message, error) = await FetchReportAndMessageAsync(reportId, ct);
        if (error != null) return error;

        var result = await moderationService.WarnUserAsync(
            new WarnIntent
            {
                User = message!.User,
                Chat = message.Chat,
                Executor = executor,
                Reason = $"Report #{reportId} - inappropriate behavior",
                MessageId = report!.MessageId
            },
            ct);

        if (!result.Success)
        {
            logger.LogError("Warn action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            return new ReviewActionResult(false, $"Warning failed: {result.ErrorMessage}");
        }

        var statusResult = await UpdateStatusAtomicallyAsync(
            reportId, ReportStatus.Reviewed, executor, "warn",
            $"User {message.User.DisplayName} warned", ct);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, Actor.FromUserIdentity(message.User),
            $"Warned user (report #{reportId}, {result.WarningCount} warnings total)", ct);

        await CleanupContentReportAsync(report, actionName: "Warn", ct);

        return new ReviewActionResult(true,
            $"Warning issued (warning #{result.WarningCount})",
            ActionName: "Warn");
    }

    public async Task<ReviewActionResult> DismissAsync(long reportId, Actor executor, string? reason, CancellationToken ct)
    {
        var report = await reportsRepository.GetContentReportAsync(reportId, ct);
        if (report == null)
            return new ReviewActionResult(false, $"Report {reportId} not found");

        var alreadyHandled = CheckAlreadyHandled(report);
        if (alreadyHandled != null) return alreadyHandled;

        var statusResult = await UpdateStatusAtomicallyAsync(
            reportId, ReportStatus.Dismissed, executor, "dismiss",
            reason ?? "No action needed", ct);
        if (statusResult != null) return statusResult;

        await auditService.LogEventAsync(
            AuditEventType.ReportReviewed, executor, null,
            $"Dismissed report #{reportId} ({reason ?? "no action taken"})", ct);

        await CleanupContentReportAsync(report, actionName: "Dismiss", ct);

        logger.LogInformation("Dismissed report {ReportId} (reason: {Reason})", reportId, reason ?? "none");

        return new ReviewActionResult(true, "Report dismissed", ActionName: "Dismiss");
    }

    private async Task<(Report? Report, MessageRecord? Message, ReviewActionResult? Error)> FetchReportAndMessageAsync(
        long reportId, CancellationToken ct)
    {
        var report = await reportsRepository.GetContentReportAsync(reportId, ct);
        if (report == null)
            return (null, null, new ReviewActionResult(false, $"Report {reportId} not found"));

        var alreadyHandled = CheckAlreadyHandled(report);
        if (alreadyHandled != null)
            return (null, null, alreadyHandled);

        var message = await messageRepository.GetMessageAsync(report.MessageId, report.Chat.Id, ct);
        if (message == null)
            return (null, null, new ReviewActionResult(false, $"Message {report.MessageId} not found"));

        return (report, message, null);
    }

    private static ReviewActionResult? CheckAlreadyHandled(Report report)
    {
        if (report.Status == ReportStatus.Pending) return null;

        var handledBy = report.ReviewedBy ?? "another admin";
        var action = report.ActionTaken ?? "unknown";
        var time = report.ReviewedAt?.UtcDateTime.ToString("g") ?? "unknown time";
        return new ReviewActionResult(false,
            $"Already handled by {handledBy} ({action}) at {time} UTC");
    }

    private async Task<ReviewActionResult?> UpdateStatusAtomicallyAsync(
        long reportId, ReportStatus status, Actor executor, string actionTaken, string notes, CancellationToken ct)
    {
        var updated = await reportsRepository.TryUpdateStatusAsync(
            reportId, status, executor.GetDisplayText(), actionTaken, notes, ct);

        if (updated) return null;

        // Race condition: re-fetch to get attribution
        var current = await reportsRepository.GetContentReportAsync(reportId, ct);
        if (current != null)
        {
            var result = CheckAlreadyHandled(current);
            if (result != null) return result;
        }

        return new ReviewActionResult(false, $"Report {reportId} could not be updated");
    }

    private async Task CleanupContentReportAsync(Report report, string actionName, CancellationToken ct)
    {
        // Delete /report command message
        if (report.ReportCommandMessageId.HasValue)
        {
            try
            {
                await botMessageService.DeleteAndMarkMessageAsync(
                    report.Chat.Id, report.ReportCommandMessageId.Value,
                    deletionSource: "report_reviewed", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not delete /report command message {MessageId} (may already be deleted)",
                    report.ReportCommandMessageId);
            }
        }

        // Dismiss only: reply to original reported message
        if (actionName == "Dismiss")
        {
            try
            {
                await botMessageService.SendAndSaveMessageAsync(
                    report.Chat.Id,
                    "\u2713 This message was reviewed and no action was taken",
                    parseMode: ParseMode.Markdown,
                    replyParameters: new ReplyParameters { MessageId = report.MessageId },
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not reply to reported message {MessageId} (may be deleted)", report.MessageId);
            }
        }

        // Cleanup stale DM callback contexts
        await callbackContextRepo.DeleteByReportIdAsync(report.Id, ct);
    }
}
