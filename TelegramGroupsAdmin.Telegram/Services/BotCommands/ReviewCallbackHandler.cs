using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
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
/// Handles callback queries for review moderation buttons in DMs.
/// Routes to type-specific handlers based on ReviewType.
/// Callback format: rev:{contextId}:{actionInt} (or legacy rpt:{contextId}:{actionInt})
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
/// </remarks>
public class ReviewCallbackHandler : IReviewCallbackHandler
{
    private readonly ILogger<ReviewCallbackHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public ReviewCallbackHandler(
        ILogger<ReviewCallbackHandler> logger,
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
        var callbackContextRepo = scope.ServiceProvider.GetRequiredService<IReviewCallbackContextRepository>();
        var reviewsRepo = scope.ServiceProvider.GetRequiredService<IReviewsRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var moderationService = scope.ServiceProvider.GetRequiredService<IModerationOrchestrator>();

        // Look up callback context from database
        var context = await callbackContextRepo.GetByIdAsync(contextId, cancellationToken);
        if (context == null)
        {
            _logger.LogWarning("Review callback context {ContextId} not found (button expired)", contextId);
            await UpdateMessageWithResultAsync(callbackQuery, "Button expired - please use web UI", cancellationToken);
            return;
        }

        var reviewId = context.ReviewId;
        var reviewType = context.ReviewType;
        var chatId = context.ChatId;
        var userId = context.UserId;
        var executorUser = callbackQuery.From;

        _logger.LogInformation(
            "Review callback: Type={ReviewType}, Action={ActionInt}, ReviewId={ReviewId}, ChatId={ChatId}, UserId={UserId}, Executor={Executor}",
            reviewType, actionInt, reviewId, chatId, userId, executorUser.ToLogInfo());

        // Get review and check status
        var review = await reviewsRepo.GetByIdAsync(reviewId, cancellationToken);
        if (review == null)
        {
            _logger.LogWarning("Review {ReviewId} not found", reviewId);
            await UpdateMessageWithResultAsync(callbackQuery, "Review not found", cancellationToken);
            await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
            return;
        }

        if (review.Status != ReportStatus.Pending)
        {
            _logger.LogInformation("Review {ReviewId} already handled (status: {Status})", reviewId, review.Status);
            await UpdateMessageWithResultAsync(callbackQuery,
                FormatAlreadyHandledMessage(review.ReviewedBy, review.ActionTaken, review.ReviewedAt), cancellationToken);
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
            result = reviewType switch
            {
                ReviewType.Report => await HandleReportActionAsync(
                    moderationService, reviewsRepo, review, userId, actionInt, executor, targetUser, cancellationToken),
                ReviewType.ImpersonationAlert => await HandleImpersonationActionAsync(
                    moderationService, reviewsRepo, review, userId, actionInt, executor, targetUser, cancellationToken),
                ReviewType.ExamFailure => await HandleExamActionAsync(
                    moderationService, reviewsRepo, review, chatId, userId, actionInt, executor, targetUser, cancellationToken),
                _ => new ReviewActionResult(Success: false, Message: $"Unknown review type: {reviewType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute review action {Action} for review {ReviewId} (type: {ReviewType})",
                actionInt, reviewId, reviewType);
            result = new ReviewActionResult(Success: false, Message: $"Action failed: {ex.Message}");
        }

        // Atomically update review status (race condition protection)
        if (result.Success)
        {
            var executorName = executor.DisplayName ?? "Admin";
            var updated = await reviewsRepo.TryUpdateStatusAsync(
                reviewId,
                ReportStatus.Reviewed,
                executorName,
                result.ActionName ?? "Unknown",
                result.Message,
                cancellationToken);

            if (!updated)
            {
                // Race condition: another admin/web user handled it first
                var currentReview = await reviewsRepo.GetByIdAsync(reviewId, cancellationToken);
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
            await CleanupAfterReviewAsync(review, reviewType, result.ActionName, cancellationToken);
        }
    }

    #region Report Actions

    private async Task<ReviewActionResult> HandleReportActionAsync(
        IModerationOrchestrator moderationService,
        IReviewsRepository reviewsRepo,
        Review review,
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
                moderationService, review, userId, executor, targetUser, cancellationToken),
            ReportAction.Warn => await HandleWarnActionAsync(
                moderationService, review, userId, executor, targetUser, cancellationToken),
            ReportAction.TempBan => await HandleTempBanActionAsync(
                moderationService, review, userId, executor, targetUser, cancellationToken),
            ReportAction.Dismiss => HandleDismissAction(review.Id, executor),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleSpamActionAsync(
        IModerationOrchestrator moderationService,
        Review review,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = review.MessageId ?? 0;

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
                review.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Marked as spam - user banned from {result.ChatsAffected} chat(s)",
                ActionName: "Spam");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleWarnActionAsync(
        IModerationOrchestrator moderationService,
        Review review,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = review.MessageId ?? 0;
        var chatId = review.ChatId;

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
                review.Id, targetUser.ToLogInfo(userId), executor.DisplayName, result.WarningCount);
            return new ReviewActionResult(
                Success: true,
                Message: $"Warning issued (warning #{result.WarningCount})",
                ActionName: "Warn");
        }

        return new ReviewActionResult(Success: false, Message: $"Warning failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleTempBanActionAsync(
        IModerationOrchestrator moderationService,
        Review review,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        var messageId = review.MessageId ?? 0;
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
                review.Id, targetUser.ToLogInfo(userId), duration, executor.DisplayName);
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
        IReviewsRepository reviewsRepo,
        Review review,
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
                moderationService, review, userId, executor, targetUser, cancellationToken),
            ImpersonationAction.FalsePositive => HandleFalsePositiveAction(review.Id, executor),
            ImpersonationAction.Whitelist => await HandleWhitelistActionAsync(
                reviewsRepo, review, userId, executor, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleConfirmScamAsync(
        IModerationOrchestrator moderationService,
        Review review,
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
                review.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
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
        IReviewsRepository reviewsRepo,
        Review review,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Mark user as trusted to prevent future impersonation alerts
        // TODO: Implement whitelist logic when TelegramUserRepository.MarkAsTrustedAsync is available
        _logger.LogInformation(
            "Impersonation review {ReviewId}: User {UserId} whitelisted by {Executor}",
            review.Id, userId, executor.DisplayName);

        return new ReviewActionResult(
            Success: true,
            Message: "User whitelisted for impersonation checks",
            ActionName: "Whitelist");
    }

    #endregion

    #region Exam Actions

    private async Task<ReviewActionResult> HandleExamActionAsync(
        IModerationOrchestrator moderationService,
        IReviewsRepository reviewsRepo,
        Review review,
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
                review, chatId, userId, executor, cancellationToken),
            ExamAction.Deny => await HandleExamDenyAsync(
                review, chatId, userId, executor, cancellationToken),
            ExamAction.DenyAndBan => await HandleExamDenyAndBanAsync(
                moderationService, review, chatId, userId, executor, targetUser, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleExamApproveAsync(
        Review review,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Restore user permissions
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            await operations.RestrictChatMemberAsync(
                chatId,
                userId,
                new ChatPermissions
                {
                    CanSendMessages = true,
                    CanSendAudios = true,
                    CanSendDocuments = true,
                    CanSendPhotos = true,
                    CanSendVideos = true,
                    CanSendVideoNotes = true,
                    CanSendVoiceNotes = true,
                    CanSendPolls = true,
                    CanSendOtherMessages = true,
                    CanAddWebPagePreviews = true,
                    CanChangeInfo = false,
                    CanInviteUsers = true,
                    CanPinMessages = false,
                    CanManageTopics = false
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Exam review {ReviewId}: User {UserId} approved by {Executor}, permissions restored",
                review.Id, userId, executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User approved - permissions restored",
                ActionName: "Approve");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore permissions for user {UserId} in chat {ChatId}", userId, chatId);
            return new ReviewActionResult(Success: false, Message: $"Failed to restore permissions: {ex.Message}");
        }
    }

    private async Task<ReviewActionResult> HandleExamDenyAsync(
        Review review,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Kick user from chat (they can rejoin)
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            await operations.BanChatMemberAsync(chatId, userId, cancellationToken: cancellationToken);
            // Immediately unban to allow rejoin (kick behavior)
            await operations.UnbanChatMemberAsync(chatId, userId, onlyIfBanned: true, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Exam review {ReviewId}: User {UserId} denied (kicked) by {Executor}",
                review.Id, userId, executor.DisplayName);

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
        Review review,
        long chatId,
        long userId,
        Core.Models.Actor executor,
        TelegramUser? targetUser,
        CancellationToken cancellationToken)
    {
        // Ban user from this chat (prevents repeat join spam)
        var result = await moderationService.BanUserAsync(
            userId,
            0,
            executor,
            "Exam failed - banned to prevent repeat join spam",
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Exam review {ReviewId}: User {User} denied and banned by {Executor}",
                review.Id, targetUser.ToLogInfo(userId), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"User denied and banned from {result.ChatsAffected} chat(s)",
                ActionName: "DenyAndBan");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    #endregion

    #region Helpers

    private async Task CleanupAfterReviewAsync(
        Review review,
        ReviewType reviewType,
        string? actionName,
        CancellationToken cancellationToken)
    {
        // Type-specific cleanup
        if (reviewType == ReviewType.Report)
        {
            await CleanupAfterReportReviewAsync(review, actionName, cancellationToken);
        }
        // Other types don't have specific cleanup yet
    }

    private async Task CleanupAfterReportReviewAsync(
        Review review,
        string? actionName,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        // 1. Delete the /report command message (for all actions)
        if (review.ReportCommandMessageId.HasValue)
        {
            try
            {
                await operations.DeleteMessageAsync(
                    review.ChatId,
                    review.ReportCommandMessageId.Value,
                    cancellationToken);

                _logger.LogDebug(
                    "Deleted /report command message {MessageId} in chat {ChatId}",
                    review.ReportCommandMessageId.Value, review.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Could not delete /report command message {MessageId} (may already be deleted)",
                    review.ReportCommandMessageId);
            }
        }

        // 2. On dismiss only: Reply to original reported message
        if (actionName == "Dismiss" && review.MessageId.HasValue)
        {
            try
            {
                await operations.SendMessageAsync(
                    chatId: review.ChatId,
                    text: "✓ This message was reviewed and no action was taken",
                    replyParameters: new ReplyParameters { MessageId = review.MessageId.Value },
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Sent dismiss notification as reply to message {MessageId} in chat {ChatId}",
                    review.MessageId, review.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Could not reply to reported message {MessageId} (may be deleted)",
                    review.MessageId);
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
