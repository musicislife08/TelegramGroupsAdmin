using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Telegram;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for handling admin actions on reports
/// </summary>
public class ReportActionsService : IReportActionsService
{
    private readonly IReportsRepository _reportsRepository;
    private readonly MessageHistoryRepository _messageRepository;
    private readonly IUserActionsRepository _userActionsRepository;
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<ReportActionsService> _logger;

    public ReportActionsService(
        IReportsRepository reportsRepository,
        MessageHistoryRepository messageRepository,
        IUserActionsRepository userActionsRepository,
        TelegramBotClientFactory botFactory,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<ReportActionsService> logger)
    {
        _reportsRepository = reportsRepository;
        _messageRepository = messageRepository;
        _userActionsRepository = userActionsRepository;
        _botFactory = botFactory;
        _telegramOptions = telegramOptions.Value;
        _logger = logger;
    }

    public async Task HandleSpamActionAsync(long reportId, string reviewerId)
    {
        var report = await _reportsRepository.GetByIdAsync(reportId);
        if (report == null)
        {
            throw new InvalidOperationException($"Report {reportId} not found");
        }

        var botClient = _botFactory.GetOrCreate(_telegramOptions.BotToken);

        // Delete the reported message
        try
        {
            await botClient.DeleteMessage(
                chatId: report.ChatId,
                messageId: report.MessageId);

            // Mark message as deleted in database
            await _messageRepository.MarkMessageAsDeletedAsync(
                report.MessageId,
                "spam_action");

            _logger.LogInformation(
                "Deleted message {MessageId} in chat {ChatId} via spam action on report {ReportId}",
                report.MessageId,
                report.ChatId,
                reportId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} in chat {ChatId} (may already be deleted)",
                report.MessageId,
                report.ChatId);
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewerId,
            "spam",
            "Message deleted as spam");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            "✅ Report reviewed: Message deleted as spam");
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

        var botClient = _botFactory.GetOrCreate(_telegramOptions.BotToken);

        // Ban user in the chat
        try
        {
            await botClient.BanChatMember(
                chatId: report.ChatId,
                userId: message.UserId);

            // Record ban action (all bans are global)
            var banAction = new UserActionRecord(
                Id: 0,
                UserId: message.UserId,
                ActionType: UserActionType.Ban,
                MessageId: report.MessageId,
                IssuedBy: $"admin_{reviewerId}",
                IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpiresAt: null, // Permanent ban
                Reason: $"Report #{reportId} - spam/abuse"
            );

            await _userActionsRepository.InsertAsync(banAction);

            _logger.LogInformation(
                "Banned user {UserId} in chat {ChatId} via report {ReportId}",
                message.UserId,
                report.ChatId,
                reportId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to ban user {UserId} in chat {ChatId}",
                message.UserId,
                report.ChatId);
            throw;
        }

        // Delete the message
        try
        {
            await botClient.DeleteMessage(
                chatId: report.ChatId,
                messageId: report.MessageId);

            await _messageRepository.MarkMessageAsDeletedAsync(
                report.MessageId,
                "ban_action");
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
            ReportStatus.Reviewed,
            reviewerId,
            "ban",
            $"User {message.UserId} banned");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"✅ Report reviewed: User banned from chat");
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

        // Record warn action (all warnings are global)
        var warnAction = new UserActionRecord(
            Id: 0,
            UserId: message.UserId,
            ActionType: UserActionType.Warn,
            MessageId: report.MessageId,
            IssuedBy: $"admin_{reviewerId}",
            IssuedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAt: null,
            Reason: $"Report #{reportId} - inappropriate behavior"
        );

        await _userActionsRepository.InsertAsync(warnAction);

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewerId,
            "warn",
            $"User {message.UserId} warned");

        // Reply to original /report command
        await SendReportReplyAsync(
            report,
            $"✅ Report reviewed: Warning issued to user");

        _logger.LogInformation(
            "Warned user {UserId} via report {ReportId}",
            message.UserId,
            reportId);
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
            ReportStatus.Dismissed,
            reviewerId,
            "dismiss",
            reason ?? "No action needed");

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
        var botClient = _botFactory.GetOrCreate(_telegramOptions.BotToken);

        try
        {
            // Phase 2.6: For web UI reports, reply to the reported message itself
            // For Telegram /report command, reply to the command message
            // This ensures all reports get visible feedback in the chat
            var replyToMessageId = report.ReportCommandMessageId ?? report.MessageId;

            await botClient.SendMessage(
                chatId: report.ChatId,
                text: message,
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
