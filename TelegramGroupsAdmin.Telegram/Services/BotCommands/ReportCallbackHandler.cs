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
/// Callback format: rpt:{contextId} (context stored in database for short IDs)
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

        // Parse: rpt:{contextId}:{action}
        var payload = data[CallbackConstants.ReportActionPrefix.Length..];
        var parts = payload.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var contextId) ||
            !int.TryParse(parts[1], out var actionInt))
        {
            _logger.LogWarning("Invalid report callback format: {Data}", data);
            return;
        }

        // Validate enum value before cast (defense-in-depth against crafted callbacks)
        if (actionInt < 0 || actionInt > (int)ReportAction.Dismiss)
        {
            _logger.LogWarning("Invalid report action value: {ActionInt}", actionInt);
            return;
        }

        var action = (ReportAction)actionInt;

        using var scope = _scopeFactory.CreateScope();
        var callbackContextRepo = scope.ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();
        var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var moderationService = scope.ServiceProvider.GetRequiredService<IModerationOrchestrator>();

        // Look up callback context from database
        var context = await callbackContextRepo.GetByIdAsync(contextId, cancellationToken);
        if (context == null)
        {
            _logger.LogWarning("Report callback context {ContextId} not found (button expired)", contextId);
            await UpdateMessageWithResultAsync(callbackQuery, "Button expired - please use web UI", cancellationToken);
            return;
        }

        var reportId = context.ReportId;
        var chatId = context.ChatId;
        var userId = context.UserId;
        var executorUser = callbackQuery.From;

        _logger.LogInformation(
            "Report callback: Action={Action}, ReportId={ReportId}, ChatId={ChatId}, UserId={UserId}, Executor={Executor}",
            action, reportId, chatId, userId, executorUser.ToLogInfo());

        // Check report exists and is still pending
        var report = await reportsRepo.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            _logger.LogWarning("Report {ReportId} not found", reportId);
            await UpdateMessageWithResultAsync(callbackQuery, "Report not found", cancellationToken);
            await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
            return;
        }

        if (report.Status != ReportStatus.Pending)
        {
            _logger.LogInformation("Report {ReportId} already handled (status: {Status})", reportId, report.Status);
            await UpdateMessageWithResultAsync(callbackQuery, $"Report already {report.Status.ToString().ToLower()}", cancellationToken);
            await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
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
        var executorName = executor.DisplayName ?? "Admin";
        ReportActionResult result;
        try
        {
            result = action switch
            {
                ReportAction.Spam => await HandleSpamActionAsync(
                    moderationService, reportId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.Warn => await HandleWarnActionAsync(
                    moderationService, reportId, chatId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.TempBan => await HandleTempBanActionAsync(
                    moderationService, reportId, userId, report.MessageId, executor, targetUser, cancellationToken),
                ReportAction.Dismiss => HandleDismissAction(reportId, executor),
                _ => new ReportActionResult(Success: false, Message: "Unknown action")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute report action {Action} for report {ReportId}", action, reportId);
            result = new ReportActionResult(Success: false, Message: $"Action failed: {ex.Message}");
        }

        // Atomically update report status (race condition protection)
        if (result.Success)
        {
            var updated = await reportsRepo.TryUpdateReportStatusAsync(
                reportId,
                ReportStatus.Reviewed,
                executorName,
                action.ToString(),
                result.Message,
                cancellationToken);

            if (!updated)
            {
                // Race condition: another admin/web user handled it first
                var currentReport = await reportsRepo.GetByIdAsync(reportId, cancellationToken);
                var handledMessage = $"Already handled by {currentReport?.ReviewedBy ?? "another admin"} " +
                                     $"({currentReport?.ActionTaken ?? "unknown"}) " +
                                     $"at {currentReport?.ReviewedAt?.ToString("g") ?? "unknown time"}";

                await UpdateMessageWithResultAsync(callbackQuery, handledMessage, cancellationToken);
                await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
                return;
            }
        }

        // Update DM message to show result (removes buttons)
        await UpdateMessageWithResultAsync(callbackQuery, result.Message, cancellationToken);

        // Always delete the callback context after handling (success or failure)
        await callbackContextRepo.DeleteAsync(contextId, cancellationToken);

        // Cleanup: Delete /report command and send dismiss reply if applicable
        if (result.Success)
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

    private async Task<ReportActionResult> HandleSpamActionAsync(
        IModerationOrchestrator moderationService,
        long reportId,
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

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} banned as spam by {Executor}",
                reportId, targetUser.ToLogInfo(userId), executorName);
            return new ReportActionResult(Success: true, Message: $"Marked as spam - user banned from {result.ChatsAffected} chat(s)");
        }

        return new ReportActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private async Task<ReportActionResult> HandleWarnActionAsync(
        IModerationOrchestrator moderationService,
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

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} warned by {Executor} (count: {Count})",
                reportId, targetUser.ToLogInfo(userId), executorName, result.WarningCount);
            return new ReportActionResult(Success: true, Message: $"Warning issued (warning #{result.WarningCount})");
        }

        return new ReportActionResult(Success: false, Message: $"Warning failed: {result.ErrorMessage}");
    }

    private async Task<ReportActionResult> HandleTempBanActionAsync(
        IModerationOrchestrator moderationService,
        long reportId,
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

        if (result.Success)
        {
            _logger.LogInformation(
                "Report {ReportId}: User {User} temp banned for {Duration} by {Executor}",
                reportId, targetUser.ToLogInfo(userId), duration, executorName);
            return new ReportActionResult(Success: true, Message: $"Temp banned for {TimeSpanUtilities.FormatDuration(duration)} from {result.ChatsAffected} chat(s)");
        }

        return new ReportActionResult(Success: false, Message: $"Temp ban failed: {result.ErrorMessage}");
    }

    private ReportActionResult HandleDismissAction(long reportId, Core.Models.Actor executor)
    {
        var executorName = executor.DisplayName ?? "Admin";

        _logger.LogInformation(
            "Report {ReportId} dismissed by {Executor}",
            reportId, executorName);

        // No moderation action needed - status update handled by TryUpdateReportStatusAsync
        return new ReportActionResult(Success: true, Message: "Report dismissed");
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
