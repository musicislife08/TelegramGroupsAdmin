using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Application-level service for handling report moderation callback queries from inline buttons in DMs.
/// Orchestrates the report review workflow, calling bot services for Telegram operations
/// and routing to type-specific handlers based on ReportType.
/// Callback format: rev:{contextId}:{actionInt}
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
/// </remarks>
public class ReportCallbackService : IReportCallbackService
{
    private readonly ILogger<ReportCallbackService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ReportCallbackService(
        ILogger<ReportCallbackService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public bool CanHandle(string callbackData)
    {
        return callbackData.StartsWith(CallbackConstants.ReviewActionPrefix);
    }

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Review callback received with null/empty data");
            return;
        }

        // Parse: rev:{contextId}:{action}
        var payload = data[CallbackConstants.ReviewActionPrefix.Length..];
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
        var moderationService = scope.ServiceProvider.GetRequiredService<IBotModerationService>();
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

        // Get target user info and build identity for threading through handler chain
        var targetUser = await userRepo.GetByTelegramIdAsync(userId, cancellationToken);
        var userIdentity = targetUser != null
            ? UserIdentity.From(targetUser)
            : UserIdentity.FromId(userId);

        // Build chat identity from DB (avoids Telegram API call)
        var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        var chatIdentity = await ChatIdentity.FromAsync(chatId, managedChatsRepo, cancellationToken);

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
                    moderationService, report, userIdentity, chatIdentity, actionInt, executor, cancellationToken),
                ReportType.ImpersonationAlert => await HandleImpersonationActionAsync(
                    moderationService, report, userIdentity, actionInt, executor, cancellationToken),
                ReportType.ExamFailure => await HandleExamActionAsync(
                    examFlowService, report, userIdentity, chatIdentity, actionInt, executor, cancellationToken),
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
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        int actionInt,
        Core.Models.Actor executor,
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
                moderationService, report, user, chat, executor, cancellationToken),
            ReportAction.Ban => await HandleBanActionAsync(
                moderationService, report, user, chat, executor, cancellationToken),
            ReportAction.Warn => await HandleWarnActionAsync(
                moderationService, report, user, chat, executor, cancellationToken),
            ReportAction.Dismiss => HandleDismissAction(report.Id, executor),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleSpamActionAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        if (!report.MessageId.HasValue)
            return new ReviewActionResult(Success: false, Message: "Cannot mark as spam: no message ID");

        var result = await moderationService.MarkAsSpamAndBanAsync(
            new SpamBanIntent
            {
                User = user,
                Chat = chat,
                Executor = executor,
                Reason = "Marked as spam via report review",
                MessageId = report.MessageId.Value
            },
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} marked as spam by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Marked as spam - user banned from {result.ChatsAffected} chat(s)",
                ActionName: "Spam");
        }

        return new ReviewActionResult(Success: false, Message: $"Spam action failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleBanActionAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Delete the offending message first (if present)
        if (report.MessageId.HasValue)
        {
            await moderationService.DeleteMessageAsync(
                new DeleteMessageIntent
                {
                    User = user,
                    Chat = chat,
                    Executor = executor,
                    Reason = "Deleted via report review (ban)",
                    MessageId = report.MessageId.Value
                },
                cancellationToken);
        }

        // Ban user globally
        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = user,
                Executor = executor,
                Reason = "Banned via report review",
                MessageId = report.MessageId
            },
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} banned by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"User banned from {result.ChatsAffected} chat(s)",
                ActionName: "Ban");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private async Task<ReviewActionResult> HandleWarnActionAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var result = await moderationService.WarnUserAsync(
            new WarnIntent
            {
                User = user,
                Chat = chat,
                Executor = executor,
                Reason = "Warning issued via report review",
                MessageId = report.MessageId
            },
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Review {ReviewId}: User {User} warned by {Executor} (count: {Count})",
                report.Id, user.ToLogInfo(), executor.DisplayName, result.WarningCount);
            return new ReviewActionResult(
                Success: true,
                Message: $"Warning issued (warning #{result.WarningCount})",
                ActionName: "Warn");
        }

        return new ReviewActionResult(Success: false, Message: $"Warning failed: {result.ErrorMessage}");
    }

    private ReviewActionResult HandleDismissAction(long reviewId, Core.Models.Actor executor)
    {
        _logger.LogInformation("Review {ReviewId} dismissed by {Executor}", reviewId, executor.DisplayName);
        return new ReviewActionResult(Success: true, Message: "Report dismissed", ActionName: "Dismiss");
    }

    #endregion

    #region Impersonation Actions

    private async Task<ReviewActionResult> HandleImpersonationActionAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        int actionInt,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Validate action enum
        if (actionInt < 0 || actionInt > (int)ImpersonationAction.Trust)
        {
            _logger.LogWarning("Invalid impersonation action value: {ActionInt}", actionInt);
            return new ReviewActionResult(Success: false, Message: "Invalid action");
        }

        var action = (ImpersonationAction)actionInt;

        return action switch
        {
            ImpersonationAction.Confirm => await HandleConfirmAsync(
                moderationService, report, user, executor, cancellationToken),
            ImpersonationAction.Dismiss => HandleImpersonationDismissAction(report.Id, executor),
            ImpersonationAction.Trust => await HandleTrustActionAsync(
                moderationService, report, user, executor, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleConfirmAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        // Ban the impersonator globally
        var result = await moderationService.BanUserAsync(
            new BanIntent
            {
                User = user,
                Executor = executor,
                Reason = "Confirmed impersonation scam"
            },
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Impersonation review {ReviewId}: User {User} banned as scammer by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: $"Confirmed scam - user banned from {result.ChatsAffected} chat(s)",
                ActionName: "Confirm");
        }

        return new ReviewActionResult(Success: false, Message: $"Ban failed: {result.ErrorMessage}");
    }

    private ReviewActionResult HandleImpersonationDismissAction(long reviewId, Core.Models.Actor executor)
    {
        _logger.LogInformation(
            "Impersonation review {ReviewId} dismissed by {Executor}",
            reviewId, executor.DisplayName);
        return new ReviewActionResult(
            Success: true,
            Message: "Alert dismissed",
            ActionName: "Dismiss");
    }

    private async Task<ReviewActionResult> HandleTrustActionAsync(
        IBotModerationService moderationService,
        ReportBase report,
        UserIdentity user,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var result = await moderationService.TrustUserAsync(
            new TrustIntent
            {
                User = user,
                Executor = executor,
                Reason = $"Trusted after impersonation review #{report.Id}"
            },
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Impersonation review {ReviewId}: User {User} trusted by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);
            return new ReviewActionResult(
                Success: true,
                Message: "User trusted - future impersonation alerts suppressed",
                ActionName: "Trust");
        }

        return new ReviewActionResult(Success: false, Message: $"Trust failed: {result.ErrorMessage}");
    }

    #endregion

    #region Exam Actions

    private async Task<ReviewActionResult> HandleExamActionAsync(
        IExamFlowService examFlowService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        int actionInt,
        Core.Models.Actor executor,
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
                examFlowService, report, user, chat, executor, cancellationToken),
            ExamAction.Deny => await HandleExamDenyAsync(
                examFlowService, report, user, chat, executor, cancellationToken),
            ExamAction.DenyAndBan => await HandleExamDenyAndBanAsync(
                examFlowService, report, user, chat, executor, cancellationToken),
            _ => new ReviewActionResult(Success: false, Message: "Unknown action")
        };
    }

    private async Task<ReviewActionResult> HandleExamApproveAsync(
        IExamFlowService examFlowService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var result = await examFlowService.ApproveExamFailureAsync(
            user,
            chat,
            report.Id,
            executor,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Exam review {ReviewId}: User {User} approved by {Executor}, permissions restored",
                report.Id, user.ToLogInfo(), executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User approved - permissions restored, teaser deleted",
                ActionName: "Approve");
        }

        return new ReviewActionResult(Success: false, Message: result.ErrorMessage ?? "Approval failed");
    }

    private async Task<ReviewActionResult> HandleExamDenyAsync(
        IExamFlowService examFlowService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var result = await examFlowService.DenyExamFailureAsync(
            user,
            chat,
            executor,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Exam review {ReviewId}: User {User} denied (kicked) by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User denied - kicked from chat, teaser deleted",
                ActionName: "Deny");
        }

        return new ReviewActionResult(Success: false, Message: result.ErrorMessage ?? "Denial failed");
    }

    private async Task<ReviewActionResult> HandleExamDenyAndBanAsync(
        IExamFlowService examFlowService,
        ReportBase report,
        UserIdentity user,
        ChatIdentity chat,
        Core.Models.Actor executor,
        CancellationToken cancellationToken)
    {
        var result = await examFlowService.DenyAndBanExamFailureAsync(
            user,
            chat,
            executor,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Exam review {ReviewId}: User {User} denied and banned by {Executor}",
                report.Id, user.ToLogInfo(), executor.DisplayName);

            return new ReviewActionResult(
                Success: true,
                Message: "User denied and banned, teaser deleted",
                ActionName: "DenyAndBan");
        }

        return new ReviewActionResult(Success: false, Message: result.ErrorMessage ?? "Ban failed");
    }

    #endregion

    #region Helpers

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
        await using var scope = _scopeFactory.CreateAsyncScope();
        var messageService = scope.ServiceProvider.GetRequiredService<IBotMessageService>();

        // 1. Delete the /report command message (for all actions)
        if (report.ReportCommandMessageId.HasValue)
        {
            try
            {
                await messageService.DeleteAndMarkMessageAsync(
                    report.ChatId,
                    report.ReportCommandMessageId.Value,
                    "report_cleanup",
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
                await messageService.SendAndSaveMessageAsync(
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dmService = scope.ServiceProvider.GetRequiredService<IBotDmService>();

        try
        {
            // Get original message text and append result
            var originalCaption = callbackQuery.Message.Caption ?? callbackQuery.Message.Text ?? "";
            var updatedText = $"{originalCaption}\n\n{resultMessage}";

            // Edit message to remove buttons and show result
            if (callbackQuery.Message.Photo != null || callbackQuery.Message.Video != null)
            {
                // For media messages, edit caption
                await dmService.EditDmCaptionAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    updatedText,
                    replyMarkup: null, // Remove inline keyboard
                    cancellationToken: cancellationToken);
            }
            else
            {
                // For text messages, edit text
                await dmService.EditDmTextAsync(
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
