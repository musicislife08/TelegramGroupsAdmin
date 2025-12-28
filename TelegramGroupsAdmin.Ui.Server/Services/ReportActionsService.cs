using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Service for handling admin actions on reports
/// </summary>
public class ReportActionsService : IReportActionsService
{
    private readonly IReportsRepository _reportsRepository;
    private readonly IMessageHistoryRepository _messageRepository;
    private readonly ModerationOrchestrator _moderationService;
    private readonly IAuditService _auditService;
    private readonly IBotMessageService _botMessageService;
    private readonly ILogger<ReportActionsService> _logger;

    public ReportActionsService(
        IReportsRepository reportsRepository,
        IMessageHistoryRepository messageRepository,
        ModerationOrchestrator moderationService,
        IAuditService auditService,
        IBotMessageService botMessageService,
        ILogger<ReportActionsService> logger)
    {
        _reportsRepository = reportsRepository;
        _messageRepository = messageRepository;
        _moderationService = moderationService;
        _auditService = auditService;
        _botMessageService = botMessageService;
        _logger = logger;
    }

    public async Task HandleSpamActionAsync(long reportId, string reviewerId)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId);
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
            cancellationToken: CancellationToken.None);

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
            $"User banned from {result.ChatsAffected} chats, message deleted");

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"spam:report#{reportId}:chats{result.ChatsAffected}");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"✅ Report reviewed: User banned from {result.ChatsAffected} chats, message deleted as spam");
    }

    public async Task HandleBanActionAsync(long reportId, string reviewerId)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId);
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
            cancellationToken: CancellationToken.None);

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
                deletionSource: "ban_action");
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
            $"User banned from {result.ChatsAffected} chats");

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"ban:report#{reportId}:chats{result.ChatsAffected}");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"✅ Report reviewed: User banned from {result.ChatsAffected} chats");
    }

    public async Task HandleWarnActionAsync(long reportId, string reviewerId)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId);
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
            reason: $"Report #{reportId} - inappropriate behavior");

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
            $"User {message.UserId} warned");

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            Actor.FromTelegramUser(message.UserId),
            $"warn:report#{reportId}:warnings{result.WarningCount}");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"✅ Report reviewed: Warning issued to user");
    }

    public async Task HandleDismissActionAsync(long reportId, string reviewerId, string? reason = null)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId);
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
            reason ?? "No action needed");

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            Actor.FromWebUser(reviewerId),
            null,
            $"dismiss:report#{reportId}:{reason ?? "no_action"}");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"ℹ️ Report reviewed: No violation found{(reason != null ? $" ({reason})" : "")}");

        _logger.LogInformation(
            "Dismissed report {ReportId} (reason: {Reason})",
            reportId,
            reason ?? "none");
    }

    private async Task SendReportReplyAsync(Report report, string message)
    {
        try
        {
            // Phase 2.6: For web UI reports, reply to the reported message itself
            // For Telegram /report command, reply to the command message
            // This ensures all reports get visible feedback in the chat
            var replyToMessageId = report.ReportCommandMessageId ?? report.MessageId;

            // Use BotMessageService to save bot response to database
            await _botMessageService.SendAndSaveMessageAsync(
                report.ChatId,
                message,
                parseMode: ParseMode.Markdown,
                replyParameters: new global::Telegram.Bot.Types.ReplyParameters
                {
                    MessageId = replyToMessageId
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send reply to message {MessageId} in chat {ChatId}",
                report.ReportCommandMessageId ?? report.MessageId,
                report.ChatId);
        }
    }
}
