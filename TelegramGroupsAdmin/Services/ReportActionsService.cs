using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for handling admin actions on reports
/// </summary>
public class ReportActionsService : IReportActionsService
{
    private readonly IReportsRepository _reportsRepository;
    private readonly IMessageHistoryRepository _messageRepository;
    private readonly IBotModerationService _moderationService;
    private readonly IAuditService _auditService;
    private readonly IBotMessageService _botMessageService;
    private readonly IReportCallbackContextRepository _callbackContextRepo;
    private readonly ILogger<ReportActionsService> _logger;

    public ReportActionsService(
        IReportsRepository reportsRepository,
        IMessageHistoryRepository messageRepository,
        IBotModerationService moderationService,
        IAuditService auditService,
        IBotMessageService botMessageService,
        IReportCallbackContextRepository callbackContextRepo,
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

    public async Task HandleSpamActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Execute spam + ban action via ModerationActionService
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = message.User,
                Chat = message.Chat,
                MessageId = report.MessageId,
                Executor = executor,
                Reason = $"Report #{reportId} - spam/abuse"
            },
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Spam action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute spam action: {result.ErrorMessage}");
        }

        // Update report status
        await _reportsRepository.UpdateStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executor.GetDisplayText(),
            "spam",
            $"User banned from {result.ChatsAffected} chats, message deleted",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            executor,
            Actor.FromUserIdentity(message.User),
            $"Marked as spam (report #{reportId}, affected {result.ChatsAffected} chats)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReportIdAsync(reportId, cancellationToken);
    }

    public async Task HandleBanActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Execute ban action via ModerationActionService
        var result = await _moderationService.BanUserAsync(
            new BanIntent
            {
                User = message.User,
                Executor = executor,
                Reason = $"Report #{reportId} - spam/abuse",
                MessageId = report.MessageId
            },
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Ban action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute ban action: {result.ErrorMessage}");
        }

        // Delete the message with tracked deletion
        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                report.Chat.Id,
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
        await _reportsRepository.UpdateStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executor.GetDisplayText(),
            "ban",
            $"User banned from {result.ChatsAffected} chats",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            executor,
            Actor.FromUserIdentity(message.User),
            $"Banned user (report #{reportId}, affected {result.ChatsAffected} chats)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReportIdAsync(reportId, cancellationToken);
    }

    public async Task HandleWarnActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var message = await _messageRepository.GetMessageAsync(report.MessageId, cancellationToken);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        // Execute warn action via ModerationActionService
        var result = await _moderationService.WarnUserAsync(
            new WarnIntent
            {
                User = message.User,
                Chat = message.Chat,
                Executor = executor,
                Reason = $"Report #{reportId} - inappropriate behavior",
                MessageId = report.MessageId
            },
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Warn action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute warn action: {result.ErrorMessage}");
        }

        // Update report status
        await _reportsRepository.UpdateStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executor.GetDisplayText(),
            "warn",
            $"User {message.User.DisplayName} warned",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            executor,
            Actor.FromUserIdentity(message.User),
            $"Warned user (report #{reportId}, {result.WarningCount} warnings total)",
            cancellationToken);

        // Delete the /report command message (cleanup - no reply needed, action is visible)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReportIdAsync(reportId, cancellationToken);
    }

    public async Task HandleDismissActionAsync(long reportId, Actor executor, string? reason = null, CancellationToken cancellationToken = default)
    {
        var report = await _reportsRepository.GetContentReportAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        // Update report status
        await _reportsRepository.UpdateStatusAsync(
            reportId,
            ReportStatus.Dismissed,
            executor.GetDisplayText(),
            "dismiss",
            reason ?? "No action needed",
            cancellationToken);

        // Create audit log entry
        await _auditService.LogEventAsync(
            AuditEventType.ReportReviewed,
            executor,
            null,
            $"Dismissed report #{reportId} ({reason ?? "no action taken"})",
            cancellationToken);

        // Reply to original REPORTED message (not /report command) - for dismiss only
        await SendDismissReplyAsync(report, cancellationToken);

        // Delete the /report command message (cleanup)
        await DeleteReportCommandMessageAsync(report, cancellationToken);

        // Cleanup stale DM callback contexts (report handled via web UI)
        await _callbackContextRepo.DeleteByReportIdAsync(reportId, cancellationToken);

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
                report.Chat.Id,
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
                report.Chat.Id,
                report.ReportCommandMessageId.Value,
                deletionSource: "report_reviewed",
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Deleted /report command message {MessageId} in chat {ChatId}",
                report.ReportCommandMessageId.Value, report.Chat.Id);
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
