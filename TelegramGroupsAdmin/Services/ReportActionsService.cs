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
    private readonly ModerationActionService _moderationService;
    private readonly TelegramBotClientFactory _botFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<ReportActionsService> _logger;

    public ReportActionsService(
        IReportsRepository reportsRepository,
        MessageHistoryRepository messageRepository,
        ModerationActionService moderationService,
        TelegramBotClientFactory botFactory,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<ReportActionsService> logger)
    {
        _reportsRepository = reportsRepository;
        _messageRepository = messageRepository;
        _moderationService = moderationService;
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

        var message = await _messageRepository.GetMessageAsync(report.MessageId);
        if (message == null)
        {
            throw new InvalidOperationException($"Message {report.MessageId} not found");
        }

        var botClient = _botFactory.GetOrCreate(_telegramOptions.BotToken);

        // Execute spam + ban action via ModerationActionService
        var result = await _moderationService.MarkAsSpamAndBanAsync(
            botClient: botClient,
            messageId: report.MessageId,
            userId: message.UserId,
            chatId: report.ChatId,
            executorId: reviewerId,
            reason: $"Report #{reportId} - spam/abuse",
            cancellationToken: default);

        if (!result.Success)
        {
            _logger.LogError("Spam action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute spam action: {result.ErrorMessage}");
        }

        // Update report status
        await _reportsRepository.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            reviewerId,
            "spam",
            $"User banned from {result.ChatsAffected} chats, message deleted");

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

        var botClient = _botFactory.GetOrCreate(_telegramOptions.BotToken);

        // Execute ban action via ModerationActionService
        var result = await _moderationService.BanUserAsync(
            botClient: botClient,
            userId: message.UserId,
            messageId: report.MessageId,
            executorId: reviewerId,
            reason: $"Report #{reportId} - spam/abuse",
            cancellationToken: default);

        if (!result.Success)
        {
            _logger.LogError("Ban action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute ban action: {result.ErrorMessage}");
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
            $"User banned from {result.ChatsAffected} chats");

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

        // Execute warn action via ModerationActionService
        var result = await _moderationService.WarnUserAsync(
            userId: message.UserId,
            messageId: report.MessageId,
            executorId: reviewerId,
            reason: $"Report #{reportId} - inappropriate behavior");

        if (!result.Success)
        {
            _logger.LogError("Warn action failed for report {ReportId}: {Error}", reportId, result.ErrorMessage);
            throw new InvalidOperationException($"Failed to execute warn action: {result.ErrorMessage}");
        }

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
