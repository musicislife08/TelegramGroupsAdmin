using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands;

/// <summary>
/// Handles callback queries for report moderation buttons in DMs.
/// Callback format: rpt:{actionInt}:{reportId}:{chatId}:{userId}
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
/// Same pattern as BanCallbackHandler.
/// </remarks>
public class ReportCallbackHandler : IReportCallbackHandler
{
    private readonly ILogger<ReportCallbackHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public ReportCallbackHandler(
        ILogger<ReportCallbackHandler> logger,
        IServiceScopeFactory scopeFactory,
        ITelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _botClientFactory = botClientFactory;
    }

    public bool CanHandle(string callbackData)
    {
        return callbackData.StartsWith(CallbackConstants.ReportActionPrefix);
    }

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Report callback received with null/empty data");
            return;
        }

        // Parse: rpt:{actionInt}:{reportId}:{chatId}:{userId}
        var parts = data[CallbackConstants.ReportActionPrefix.Length..].Split(':');
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], out var actionInt) ||
            !long.TryParse(parts[1], out var reportId) ||
            !long.TryParse(parts[2], out var chatId) ||
            !long.TryParse(parts[3], out var userId))
        {
            _logger.LogWarning("Invalid report callback format: {Data}", data);
            return;
        }

        var action = (ReportAction)actionInt;
        var executorUser = callbackQuery.From;

        _logger.LogInformation(
            "Report callback: Action={Action}, ReportId={ReportId}, ChatId={ChatId}, UserId={UserId}, Executor={Executor}",
            action, reportId, chatId, userId, executorUser.ToLogInfo());

        using var scope = _scopeFactory.CreateScope();
        var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var moderationService = scope.ServiceProvider.GetRequiredService<ModerationOrchestrator>();

        // Check report exists and is still pending
        var report = await reportsRepo.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            _logger.LogWarning("Report {ReportId} not found", reportId);
            await UpdateMessageWithResultAsync(callbackQuery, "Report not found", cancellationToken);
            return;
        }

        if (report.Status != ReportStatus.Pending)
        {
            _logger.LogInformation("Report {ReportId} already handled (status: {Status})", reportId, report.Status);
            await UpdateMessageWithResultAsync(callbackQuery, $"Report already {report.Status.ToString().ToLower()}", cancellationToken);
            return;
        }

        // Get target user info for logging
        var targetUser = await userRepo.GetByTelegramIdAsync(userId, cancellationToken);

        // Create executor actor
        var executor = Core.Models.Actor.FromTelegramUser(
            executorUser.Id,
            executorUser.Username,
            executorUser.FirstName,
            executorUser.LastName);

        // Execute action based on type
        string resultMessage;
        bool actionSucceeded = false;
        try
        {
            resultMessage = action switch
            {
                ReportAction.Spam => await HandleSpamActionAsync(
                    moderationService, reportsRepo, reportId, chatId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.Warn => await HandleWarnActionAsync(
                    moderationService, reportsRepo, reportId, chatId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.TempBan => await HandleTempBanActionAsync(
                    moderationService, reportsRepo, reportId, chatId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.Dismiss => await HandleDismissActionAsync(
                    reportsRepo, reportId, executor, cancellationToken),
                _ => "Unknown action"
            };
            actionSucceeded = !resultMessage.Contains("failed", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute report action {Action} for report {ReportId}", action, reportId);
            resultMessage = $"Action failed: {ex.Message}";
        }

        // Update DM message to show result (removes buttons)
        await UpdateMessageWithResultAsync(callbackQuery, resultMessage, cancellationToken);

        // Cleanup: Delete /report command and send dismiss reply if applicable
        if (actionSucceeded)
        {
            await CleanupAfterReviewAsync(report, action, cancellationToken);
        }
    }

    /// <summary>
    /// Cleanup after report review: delete /report command message and send dismiss notification
    /// </summary>
    private async Task CleanupAfterReviewAsync(
        ContentDetection.Models.Report report,
        ReportAction action,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        // 1. Delete the /report command message (for all actions)
        if (report.ReportCommandMessageId.HasValue)
        {
            try
            {
                await operations.DeleteMessageAsync(
                    report.ChatId,
                    report.ReportCommandMessageId.Value,
                    cancellationToken);

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

        // 2. On dismiss only: Reply to original reported message
        if (action == ReportAction.Dismiss)
        {
            try
            {
                await operations.SendMessageAsync(
                    chatId: report.ChatId,
                    text: "✓ This message was reviewed and no action was taken",
                    replyParameters: new ReplyParameters { MessageId = report.MessageId },
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Sent dismiss notification as reply to message {MessageId} in chat {ChatId}",
                    report.MessageId, report.ChatId);
            }
            catch (Exception ex)
            {
                // Original message might be deleted - that's okay
                _logger.LogDebug(ex,
                    "Could not reply to reported message {MessageId} (may be deleted)",
                    report.MessageId);
            }
        }
    }

    private async Task<string> HandleSpamActionAsync(
        ModerationOrchestrator moderationService,
        IReportsRepository reportsRepo,
        long reportId,
        long chatId,
        long userId,
        int messageId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Ban user globally
        var result = await moderationService.BanUserAsync(
            userId,
            messageId,
            executor,
            "Marked as spam via report review",
            cancellationToken);

        var executorName = executor.DisplayName ?? "Admin";

        // Update report status
        await reportsRepo.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executorName,
            "Marked as spam, user banned",
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} banned as spam by {Executor}",
                reportId, targetUser.ToLogInfo(userId), executorName);
            return $"Marked as spam - user banned from {result.ChatsAffected} chat(s)";
        }

        return $"Ban failed: {result.ErrorMessage}";
    }

    private async Task<string> HandleWarnActionAsync(
        ModerationOrchestrator moderationService,
        IReportsRepository reportsRepo,
        long reportId,
        long chatId,
        long userId,
        int messageId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var result = await moderationService.WarnUserAsync(
            userId,
            messageId,
            executor,
            "Warning issued via report review",
            chatId,
            cancellationToken);

        var executorName = executor.DisplayName ?? "Admin";

        // Update report status
        await reportsRepo.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executorName,
            $"Warning issued (count: {result.WarningCount})",
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} warned by {Executor} (count: {Count})",
                reportId, targetUser.ToLogInfo(userId), executorName, result.WarningCount);
            return $"Warning issued (warning #{result.WarningCount})";
        }

        return $"Warning failed: {result.ErrorMessage}";
    }

    private async Task<string> HandleTempBanActionAsync(
        ModerationOrchestrator moderationService,
        IReportsRepository reportsRepo,
        long reportId,
        long chatId,
        long userId,
        int messageId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Use default temp ban duration (1 hour)
        var duration = CommandConstants.DefaultTempBanDuration;

        var result = await moderationService.TempBanUserAsync(
            userId,
            messageId,
            executor,
            "Temp banned via report review",
            duration,
            cancellationToken);

        var executorName = executor.DisplayName ?? "Admin";

        // Update report status
        await reportsRepo.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executorName,
            $"Temp banned for {TimeSpanUtilities.FormatDuration(duration)}",
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} temp banned for {Duration} by {Executor}",
                reportId, targetUser.ToLogInfo(userId), duration, executorName);
            return $"Temp banned for {TimeSpanUtilities.FormatDuration(duration)} from {result.ChatsAffected} chat(s)";
        }

        return $"Temp ban failed: {result.ErrorMessage}";
    }

    private async Task<string> HandleDismissActionAsync(
        IReportsRepository reportsRepo,
        long reportId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var executorName = executor.DisplayName ?? "Admin";

        // Just update report status to dismissed (no moderation action)
        await reportsRepo.UpdateReportStatusAsync(
            reportId,
            ReportStatus.Reviewed,
            executorName,
            "Dismissed",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Report {ReportId} dismissed by {Executor}",
            reportId, executorName);

        return "Report dismissed";
    }

    private async Task UpdateMessageWithResultAsync(
        CallbackQuery callbackQuery,
        string resultMessage,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message == null)
            return;

        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            // Get original message text and append result
            var originalCaption = callbackQuery.Message.Caption ?? callbackQuery.Message.Text ?? "";
            var updatedText = $"{originalCaption}\n\n{resultMessage}";

            // Edit message to remove buttons and show result
            if (callbackQuery.Message.Photo != null || callbackQuery.Message.Video != null)
            {
                // For media messages, edit caption
                await operations.EditMessageCaptionAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    updatedText,
                    replyMarkup: null, // Remove inline keyboard
                    cancellationToken: cancellationToken);
            }
            else
            {
                // For text messages, edit text
                await operations.EditMessageTextAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    updatedText,
                    replyMarkup: null, // Remove inline keyboard
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update DM message after report action");
        }
    }
}
