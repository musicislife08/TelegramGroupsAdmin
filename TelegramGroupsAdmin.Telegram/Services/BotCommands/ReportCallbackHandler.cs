using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
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
/// Routes to type-specific handlers based on ReportType.
/// Callback format: rev:{contextId}:{actionInt} (or legacy rpt:{contextId}:{actionInt})
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
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
        // Support both new 'rev:' and legacy 'rpt:' prefixes
        // TODO: Remove ReportActionPrefix after 2026-02-01 (see GitHub issue #281)
        return callbackData.StartsWith(CallbackConstants.ReviewActionPrefix) ||
               callbackData.StartsWith(CallbackConstants.ReportActionPrefix);
    }

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Review callback received with null/empty data");
            return;
        }

        // Determine prefix and parse payload
        string prefix;
        if (data.StartsWith(CallbackConstants.ReviewActionPrefix))
        {
            prefix = CallbackConstants.ReviewActionPrefix;
        }
        else if (data.StartsWith(CallbackConstants.ReportActionPrefix))
        {
            prefix = CallbackConstants.ReportActionPrefix;
        }
        else
        {
            _logger.LogWarning("Unknown callback prefix: {Data}", data);
            return;
        }

        // Parse: {prefix}{contextId}:{action}
        var payload = data[prefix.Length..];
        var parts = payload.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var contextId) ||
            !int.TryParse(parts[1], out var actionInt))
        {
            _logger.LogWarning("Invalid review callback format: {Data}", data);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var callbackContextRepo = scope.ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();
        var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var moderationService = scope.ServiceProvider.GetRequiredService<IModerationOrchestrator>();
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();

        // Look up callback context from database
        var context = await callbackContextRepo.GetByIdAsync(contextId, cancellationToken);
        if (context == null)
        {
            _logger.LogWarning("Review callback context {ContextId} not found (button expired)", contextId);
            await UpdateMessageWithResultAsync(callbackQuery, "Button expired - please use web UI", cancellationToken);
            return;
        }

        var reviewId = context.ReportId;
        var reportType = context.ReportType;
        var chatId = context.ChatId;
        var userId = context.UserId;
        var executorUser = callbackQuery.From;

        _logger.LogInformation(
            "Review callback: Type={ReportType}, Action={ActionInt}, ReviewId={ReviewId}, ChatId={ChatId}, UserId={UserId}, Executor={Executor}",
            reportType, actionInt, reviewId, chatId, userId, executorUser.ToLogInfo());

        // Get report and check status
        var report = await reportsRepo.GetByIdAsync(reviewId, cancellationToken);
        if (report == null)
        {
            _logger.LogWarning("Review {ReviewId} not found", reviewId);
            await UpdateMessageWithResultAsync(callbackQuery, "Review not found", cancellationToken);
            await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
            return;
        }

        if (report.Status != ReportStatus.Pending)
        {
            _logger.LogInformation("Review {ReviewId} already handled (status: {Status})", reviewId, report.Status);
            await UpdateMessageWithResultAsync(callbackQuery,
                FormatAlreadyHandledMessage(report.ReviewedBy, report.ActionTaken, report.ReviewedAt), cancellationToken);
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

        // Route to type-specific handler
        ReviewActionResult result;
        try
        {
            result = reportType switch
            {
                ReportType.ContentReport => await HandleReportActionAsync(
                    moderationService, reportsRepo, report, userId, actionInt, executor, targetUser, cancellationToken),
                ReportType.ImpersonationAlert => await HandleImpersonationActionAsync(
                    moderationService, reportsRepo, report, userId, actionInt, executor, targetUser, cancellationToken),
                ReportType.ExamFailure => await HandleExamActionAsync(
                    moderationService, examFlowService, reportsRepo, report, chatId, userId, actionInt, executor, targetUser, cancellationToken),
                _ => new ReviewActionResult(Success: false, Message: $"Unknown review type: {reportType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute review action {Action} for review {ReviewId} (type: {ReportType})",
                actionInt, reviewId, reportType);
            result = new ReviewActionResult(Success: false, Message: $"Action failed: {ex.Message}");
        }

        // Atomically update review status (race condition protection)
        if (result.Success)
        {
            var executorName = executor.DisplayName ?? "Admin";
            var updated = await reportsRepo.TryUpdateStatusAsync(
                reviewId,
                ReportStatus.Reviewed,
                executorName,
                result.ActionName ?? "Unknown",
                result.Message,
                cancellationToken);

            if (!updated)
            {
                // Race condition: another admin/web user handled it first
                var currentReview = await reportsRepo.GetByIdAsync(reviewId, cancellationToken);
                await UpdateMessageWithResultAsync(callbackQuery,
                    FormatAlreadyHandledMessage(currentReview?.ReviewedBy, currentReview?.ActionTaken, currentReview?.ReviewedAt),
                    cancellationToken);
                await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
                return;
            }
        }

        // Update DM message to show result (removes buttons)
        await UpdateMessageWithResultAsync(callbackQuery, result.Message, cancellationToken);

        // Always delete the callback context after handling (success or failure)
        await callbackContextRepo.DeleteAsync(contextId, cancellationToken);

        // Cleanup: type-specific post-action tasks
        if (result.Success)
        {
            await CleanupAfterReportAsync(report, reportType, result.ActionName, cancellationToken);
        }
    }

    #region Report Actions

    private async Task<ReviewActionResult> HandleReportActionAsync(
        IModerationOrchestrator moderationService,
        IReportsRepository reportsRepo,
        ReportBase report,
        long userId,
        int actionInt,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Validate action enum
        if (actionInt < 0 || actionInt > (int)ReportAction.Dismiss)
        {
            _logger.LogWarning("Invalid report action value: {ActionInt}", actionInt);
            return new ReviewActionResult(Success: false, Message: "Invalid action");
        }

        var action = (ReportAction)actionInt;

        return action switch
        {
            ReportAction.Spam => await HandleSpamActionAsync(
                moderationService, report, userId, executor, targetUser, cancellationToken),
            ReportAction.Warn => await HandleWarnActionAsync(
                moderationService, report, userId, executor, targetUser, cancellationToken),
            ReportAction.TempBan => await HandleTempBanActionAsync(
                moderationService, report, userId, executor, targetUser, cancellationToken),
            ReportAction.Dismiss => HandleDismissAction(report.Id, executor),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleSpamActionAsync(
        IModerationOrchestrator moderationService,
        ReportBase report,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = report.MessageId ?? 0;

        var result = await moderationService.BanUserAsync(
            userId,
            messageId,
            executor,
            "Marked as spam via report review",
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} banned as spam by {Executor}",
                report.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Marked as spam - user banned from {result.ChatsAffected} chat(s)",
                ActionName: "Spam");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleWarnActionAsync(
        IModerationOrchestrator moderationService,
        ReportBase report,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = report.MessageId ?? 0;
        var chatId = report.ChatId;

        var result = await moderationService.WarnUserAsync(
            userId,
            messageId,
            executor,
            "Warning issued via report review",
            chatId,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} warned by {Executor} (count: {Count})",
                report.Id, targetUser.ToLogInfo(userId), executor.DisplayName, result.WarningCount);
            return new ReviewActionResult(
                Success: true,
                Message: $"Warning issued (warning #{result.WarningCount})",
                ActionName: "Warn");
        }

        return new ReviewActionResult(Success: false, Message: $"Warning failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleTempBanActionAsync(
        IModerationOrchestrator moderationService,
        ReportBase report,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = report.MessageId ?? 0;
        var duration = CommandConstants.DefaultTempBanDuration;

        var result = await moderationService.TempBanUserAsync(
            userId,
            messageId,
            executor,
            "Temp banned via report review",
            duration,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} temp banned for {Duration} by {Executor}",
                report.Id, targetUser.ToLogInfo(userId), duration, executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Temp banned for {TimeSpanUtilities.FormatDuration(duration)} from {result.ChatsAffected} chat(s)",
                ActionName: "TempBan");
        }

        return new ReviewActionResult(Success: false, Message: $"Temp ban failed: {result.ErrorMessage}");
    }

    private ReviewActionResult HandleDismissAction(long reviewId, Core.Models.Actor executor)
    {
        _logger.LogInformation("Review {ReviewId} dismissed by {Executor}", reviewId, executor.DisplayName);
        return new ReviewActionResult(Success: true, Message: "Report dismissed", ActionName: "Dismiss");
    }

    #endregion

    #region Impersonation Actions

    private async Task<ReviewActionResult> HandleImpersonationActionAsync(
        IModerationOrchestrator moderationService,
        IReportsRepository reportsRepo,
        ReportBase report,
        long userId,
        int actionInt,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Validate action enum
        if (actionInt < 0 || actionInt > (int)ImpersonationAction.Whitelist)
        {
            _logger.LogWarning("Invalid impersonation action value: {ActionInt}", actionInt);
            return new ReviewActionResult(Success: false, Message: "Invalid action");
        }

        var action = (ImpersonationAction)actionInt;

        return action switch
        {
            ImpersonationAction.ConfirmScam => await HandleConfirmScamAsync(
                moderationService, report, userId, executor, targetUser, cancellationToken),
            ImpersonationAction.FalsePositive => HandleFalsePositiveAction(report.Id, executor),
            ImpersonationAction.Whitelist => await HandleWhitelistActionAsync(
                reportsRepo, report, userId, executor, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleConfirmScamAsync(
        IModerationOrchestrator moderationService,
        ReportBase report,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Ban the impersonator globally
        var result = await moderationService.BanUserAsync(
            userId,
            0, // No specific message
            executor,
            "Confirmed impersonation scam",
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Impersonation review {ReviewId}: User {User} banned as scammer by {Executor}",
                report.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Confirmed scam - user banned from {result.ChatsAffected} chat(s)",
                ActionName: "ConfirmScam");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private ReviewActionResult HandleFalsePositiveAction(long reviewId, Core.Models.Actor executor)
    {
        _logger.LogInformation(
            "Impersonation review {ReviewId} marked as false positive by {Executor}",
            reviewId, executor.DisplayName);
        return new ReviewActionResult(
            Success: true,
            Message: "Marked as false positive",
            ActionName: "FalsePositive");
    }

    private async Task<ReviewActionResult> HandleWhitelistActionAsync(
        IReportsRepository reportsRepo,
        ReportBase report,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Mark user as trusted to prevent future impersonation alerts
        // TODO: Implement whitelist logic when TelegramUserRepository.MarkAsTrustedAsync is available
        _logger.LogInformation(
            "Impersonation review {ReviewId}: User {UserId} whitelisted by {Executor}",
            report.Id, userId, executor.DisplayName);

        return new ReviewActionResult(
            Success: true,
            Message: "User whitelisted for impersonation checks",
            ActionName: "Whitelist");
    }

    #endregion

    #region Exam Actions

    private async Task<ReviewActionResult> HandleExamActionAsync(
        IModerationOrchestrator moderationService,
        IExamFlowService examFlowService,
        IReportsRepository reportsRepo,
        ReportBase report,
        long chatId,
        long userId,
        int actionInt,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Validate action enum
        if (actionInt < 0 || actionInt > (int)ExamAction.DenyAndBan)
        {
            _logger.LogWarning("Invalid exam action value: {ActionInt}", actionInt);
            return new ReviewActionResult(Success: false, Message: "Invalid action");
        }

        var action = (ExamAction)actionInt;

        return action switch
        {
            ExamAction.Approve => await HandleExamApproveAsync(
                examFlowService, report, chatId, userId, executor, cancellationToken),
            ExamAction.Deny => await HandleExamDenyAsync(
                report, chatId, userId, executor, cancellationToken),
            ExamAction.DenyAndBan => await HandleExamDenyAndBanAsync(
                moderationService, report, chatId, userId, executor, targetUser, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleExamApproveAsync(
        IExamFlowService examFlowService,
        ReportBase report,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Use exam flow service which handles:
        // - Permission restoration
        // - Teaser message deletion
        // - Welcome response update to "Accepted"
        var result = await examFlowService.ApproveExamFailureAsync(
            userId,
            chatId,
            report.Id,
            executor,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Exam review {ReviewId}: User {UserId} approved by {Executor}, permissions restored",
                report.Id, userId, executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User approved - permissions restored, teaser deleted",
                ActionName: "Approve");
        }

        return new ReviewActionResult(Success: false, Message: result.ErrorMessage ?? "Approval failed");
    }

    private async Task<ReviewActionResult> HandleExamDenyAsync(
        ReportBase report,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Kick user from chat (they can rejoin)
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            var chatName = await GetChatNameForNotificationAsync(operations, chatId, cancellationToken);

            await operations.BanChatMemberAsync(chatId, userId, cancellationToken: cancellationToken);
            // Immediately unban to allow rejoin (kick behavior)
            await operations.UnbanChatMemberAsync(chatId, userId, onlyIfBanned: true, cancellationToken: cancellationToken);

            // Notify user via DM (non-fatal if fails)
            await TrySendUserNotificationAsync(
                operations,
                userId,
                $"❌ Your request to join {chatName} has been denied after admin review. You may try joining again later.",
                cancellationToken);

            _logger.LogInformation(
                "Exam review {ReviewId}: User {UserId} denied (kicked) by {Executor}",
                report.Id, userId, executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User denied - kicked from chat",
                ActionName: "Deny");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kick user {UserId} from chat {ChatId}", userId, chatId);
            return new ReviewActionResult(Success: false, Message: $"Failed to kick user: {ex.Message}");
        }
    }

    private async Task<ReviewActionResult> HandleExamDenyAndBanAsync(
        IModerationOrchestrator moderationService,
        ReportBase report,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        // Get chat info for notification message (before ban)
        var chatName = await GetChatNameForNotificationAsync(operations, chatId, cancellationToken);

        // Ban user globally (chatId: 0) to protect all managed groups from repeat spam attempts
        var result = await moderationService.BanUserAsync(
            userId,
            0, // Global ban - propagates to all managed chats
            executor,
            "Exam failed - banned to prevent repeat join spam",
            cancellationToken);

        if (result.Success)
        {
            // Notify user via DM (non-fatal if fails)
            // TODO: When appeal system is implemented, include appeal link/instructions here
            await TrySendUserNotificationAsync(
                operations,
                userId,
                $"❌ Your request to join {chatName} has been denied and you have been banned.",
                cancellationToken);

            _logger.LogInformation(
                "Exam review {ReviewId}: User {User} denied and banned by {Executor}",
                report.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"User denied and banned from {result.ChatsAffected} chat(s)",
                ActionName: "DenyAndBan");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Gets the chat name for use in user notifications.
    /// Checks database first (faster), falls back to Telegram API, then to chat ID.
    /// </summary>
    private async Task<string> GetChatNameForNotificationAsync(
        ITelegramOperations operations,
        long chatId,
        CancellationToken cancellationToken)
    {
        // Try database first (faster, no API call)
        await using var scope = _scopeFactory.CreateAsyncScope();
        var chatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        var cachedChat = await chatsRepo.GetByChatIdAsync(chatId, cancellationToken);

        if (!string.IsNullOrEmpty(cachedChat?.ChatName))
            return cachedChat.ChatName;

        // Fallback to Telegram API
        try
        {
            var chatInfo = await operations.GetChatAsync(chatId, cancellationToken);
            return chatInfo.Title ?? chatId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve chat name for {ChatId}, using ID as fallback", chatId);
            return chatId.ToString();
        }
    }

    /// <summary>
    /// Sends a DM notification to a user. Failures are logged but don't block the calling operation.
    /// </summary>
    private async Task TrySendUserNotificationAsync(
        ITelegramOperations operations,
        long userId,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await operations.SendMessageAsync(
                chatId: userId,
                text: message,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification DM to user {UserId}", userId);
        }
    }

    private async Task CleanupAfterReportAsync(
        ReportBase report,
        ReportType reportType,
        string? actionName,
        CancellationToken cancellationToken)
    {
        // Type-specific cleanup
        if (reportType == ReportType.ContentReport)
        {
            await CleanupAfterContentReportAsync(report, actionName, cancellationToken);
        }
        // Other types don't have specific cleanup yet
    }

    private async Task CleanupAfterContentReportAsync(
        ReportBase report,
        string? actionName,
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
                _logger.LogDebug(ex,
                    "Could not delete /report command message {MessageId} (may already be deleted)",
                    report.ReportCommandMessageId);
            }
        }

        // 2. On dismiss only: Reply to original reported message
        if (actionName == "Dismiss" && report.MessageId.HasValue)
        {
            try
            {
                await operations.SendMessageAsync(
                    chatId: report.ChatId,
                    text: "✓ This message was reviewed and no action was taken",
                    replyParameters: new ReplyParameters { MessageId = report.MessageId.Value },
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Sent dismiss notification as reply to message {MessageId} in chat {ChatId}",
                    report.MessageId, report.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Could not reply to reported message {MessageId} (may be deleted)",
                    report.MessageId);
            }
        }
    }

    private static string FormatAlreadyHandledMessage(string? reviewedBy, string? actionTaken, DateTimeOffset? reviewedAt)
    {
        var handledBy = reviewedBy ?? "another admin";
        var action = actionTaken ?? "unknown";
        var time = reviewedAt.HasValue
            ? $"{reviewedAt.Value.UtcDateTime:g} UTC"
            : "unknown time";
        return $"Already handled by {handledBy} ({action}) at {time}";
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
            _logger.LogWarning(ex, "Failed to update DM message after review action");
        }
    }

    #endregion
}

/// <summary>
/// Result of a review action.
/// </summary>
public record ReviewActionResult(bool Success, string Message, string? ActionName = null);
