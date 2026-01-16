using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for handling admin actions on reports
/// </summary>
public class ReportActionsService : IReportActionsService
{
    private readonly IReportsRepository _reportsRepository;
    private readonly IMessageHistoryRepository _messageRepository;
    private readonly IModerationOrchestrator _moderationService;
    private readonly IAuditService _auditService;
    private readonly IBotMessageService _botMessageService;
    private readonly IReviewCallbackContextRepository _callbackContextRepo;
    private readonly ILogger<ReportActionsService> _logger;

    public ReportActionsService(
        IReportsRepository reportsRepository,
        IMessageHistoryRepository messageRepository,
        IModerationOrchestrator moderationService,
        IAuditService auditService,
        IBotMessageService botMessageService,
        IReviewCallbackContextRepository callbackContextRepo,
        ILogger<ReportActionsService> logger)
    {
        _reportsRepository = reportsRepository;
        _messageRepository = messageRepository;
        _moderationService = moderationService;
        _auditService = auditService;
        _botMessageService = botMessageService;
        _callbackContextRepo = callbackContextRepo;
        _logger = logger;
    }

    public async Task HandleSpamActionAsync(long reportId, string reviewerId, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Create executor actor from web user
        var executor = Actor.FromWebUser(reviewerId);

        // Execute spam + ban action via ModerationActionService
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            messageId: report.MessageId,
            userId: message.UserId,
            chatId: report.ChatId,
            executor: executor,
            reason: $"Report #{reportId} - spam/abuse",
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Spam action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute spam action: {result.ErrorMessage}");
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            DataModels.ReportStatus.Reviewed,
            reviewerId,
            "spam",
            $"User banned from {result.ChatsAffected} chats, message deleted",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"Marked as spam (report #{reportId}, affected {result.ChatsAffected} chats)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReviewIdAsync(reportId, cancellationToken);
    }

    public async Task HandleBanActionAsync(long reportId, string reviewerId, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Create executor actor from web user
        var executor = Actor.FromWebUser(reviewerId);

        // Execute ban action via ModerationActionService
        var result = await _moderationService.BanUserAsync(
            userId: message.UserId,
            messageId: report.MessageId,
            executor: executor,
            reason: $"Report #{reportId} - spam/abuse",
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Ban action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute ban action: {result.ErrorMessage}");
        }

        // Delete the message with tracked deletion
        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                report.ChatId,
                report.MessageId,
                deletionSource: "ban_action",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} (may already be deleted)",
                report.MessageId);
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            DataModels.ReportStatus.Reviewed,
            reviewerId,
            "ban",
            $"User banned from {result.ChatsAffected} chats",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"Banned user (report #{reportId}, affected {result.ChatsAffected} chats)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReviewIdAsync(reportId, cancellationToken);
    }

    public async Task HandleWarnActionAsync(long reportId, string reviewerId, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Create executor actor from web user
        var executor = Actor.FromWebUser(reviewerId);

        // Execute warn action via ModerationActionService
        var result = await _moderationService.WarnUserAsync(
            userId: message.UserId,
            messageId: report.MessageId,
            executor: executor,
            reason: $"Report #{reportId} - inappropriate behavior",
            chatId: message.ChatId,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Warn action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute warn action: {result.ErrorMessage}");
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            DataModels.ReportStatus.Reviewed,
            reviewerId,
            "warn",
            $"User {message.UserId} warned",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"Warned user (report #{reportId}, {result.WarningCount} warnings total)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReviewIdAsync(reportId, cancellationToken);
    }

    public async Task HandleDismissActionAsync(long reportId, string reviewerId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            DataModels.ReportStatus.Dismissed,
            reviewerId,
            "dismiss",
            reason ?? "No action needed",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            null,
            $"Dismissed report #{reportId} ({reason ?? "no action taken"})",
            cancellationToken);

        // Reply to original REPORTED message (not /report command) - for dismiss only
        await SendDismissReplyAsync(report, cancellationToken);

        // Delete the /report command message (cleanup)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReviewIdAsync(reportId, cancellationToken);

        _logger.LogInformation(
            "Dismissed report {ReportId} (reason: {Reason})",
            reportId,
            reason ?? "none");
    }

    /// <summary>
    /// Send dismiss notification as a reply to the original REPORTED message.
    /// Only used for dismiss action - other actions have visible outcomes.
    /// </summary>
    private async Task SendDismissReplyAsync(Report report, CancellationToken cancellationToken)
    {
        try
        {
            // Reply to the original REPORTED message (not /report command)
            await _botMessageService.SendAndSaveMessageAsync(
                report.ChatId,
                "✓ This message was reviewed and no action was taken",
                parseMode: ParseMode.Markdown,
                replyParameters: new global::Telegram.Bot.Types.ReplyParameters
                {
                    MessageId = report.MessageId
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Original message might be deleted - that's okay
            _logger.LogDebug(ex,
                "Could not reply to reported message {MessageId} (may be deleted)",
                report.MessageId);
        }
    }

    /// <summary>
    /// Delete the /report command message after review (cleanup).
    /// </summary>
    private async Task DeleteReportCommandMessageAsync(Report report, CancellationToken cancellationToken)
    {
        if (!report.ReportCommandMessageId.HasValue)
            return; // Web UI reports don't have a command message

        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                report.ChatId,
                report.ReportCommandMessageId.Value,
                deletionSource: "report_reviewed",
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Deleted /report command message {MessageId} in chat {ChatId}",
                report.ReportCommandMessageId.Value, report.ChatId);
        }
        catch (Exception ex)
        {
            // Message might already be deleted - that's okay
            _logger.LogDebug(ex,
                "Could not delete /report command message {MessageId} (may already be deleted)",
                report.ReportCommandMessageId);
        }
    }
}
