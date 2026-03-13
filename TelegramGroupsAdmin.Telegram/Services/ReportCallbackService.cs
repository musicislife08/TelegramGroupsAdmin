using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.ReportActions;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Thin adapter for handling report moderation callback queries from inline buttons in DMs.
/// Parses callback data, delegates to <see cref="IReportActionsService"/>, and updates the DM message.
/// Callback format: rev:{contextId}:{actionInt}
/// </summary>
/// <remarks>
/// Registered as Singleton - creates scopes internally for scoped services.
/// </remarks>
public sealed class ReportCallbackService(
    ILogger<ReportCallbackService> logger,
    IServiceScopeFactory scopeFactory,
    IReportActionsService reportActionsService) : IReportCallbackService
{
    public bool CanHandle(string callbackData)
    {
        return callbackData.StartsWith(CallbackConstants.ReviewActionPrefix);
    }

    public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken = default)
    {
        var data = callbackQuery.Data;
        if (string.IsNullOrEmpty(data))
        {
            logger.LogWarning("Review callback received with null/empty data");
            return;
        }

        // Parse: rev:{contextId}:{action}
        var payload = data[CallbackConstants.ReviewActionPrefix.Length..];
        var parts = payload.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var contextId) ||
            !int.TryParse(parts[1], out var actionInt))
        {
            logger.LogWarning("Invalid review callback format: {Data}", data);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var callbackContextRepo = scope.ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();
        var dmService = scope.ServiceProvider.GetRequiredService<IBotDmService>();

        // Look up callback context from database
        var context = await callbackContextRepo.GetByIdAsync(contextId, cancellationToken);
        if (context == null)
        {
            logger.LogWarning("Review callback context {ContextId} not found (button expired)", contextId);
            await UpdateMessageWithResultAsync(callbackQuery, "Button expired - please use web UI", dmService, cancellationToken);
            return;
        }

        var reviewId = context.ReportId;
        var reportType = context.ReportType;
        var executorUser = callbackQuery.From;

        logger.LogInformation(
            "Review callback: Type={ReportType}, Action={ActionInt}, ReviewId={ReviewId}, ChatId={ChatId}, UserId={UserId}, Executor={Executor}",
            reportType, actionInt, reviewId, context.ChatId, context.UserId, executorUser.ToLogInfo());

        // Create executor actor
        var executor = Actor.FromTelegramUser(
            executorUser.Id,
            executorUser.Username,
            executorUser.FirstName,
            executorUser.LastName);

        // Route to unified service
        var result = await RouteToServiceAsync(reportType, reviewId, actionInt, executor, cancellationToken);

        // Update DM message to show result (removes buttons)
        await UpdateMessageWithResultAsync(callbackQuery, result.Message, dmService, cancellationToken);

        // Delete callback context (the service handles report-level cleanup)
        await callbackContextRepo.DeleteAsync(contextId, cancellationToken);
    }

    private async Task<ReviewActionResult> RouteToServiceAsync(
        ReportType reportType, long reviewId, int actionInt, Actor executor, CancellationToken ct)
    {
        return reportType switch
        {
            ReportType.ContentReport => await RouteContentReportAsync(reviewId, actionInt, executor, ct),
            ReportType.ImpersonationAlert => await RouteImpersonationAsync(reviewId, actionInt, executor, ct),
            ReportType.ExamFailure => await RouteExamAsync(reviewId, actionInt, executor, ct),
            ReportType.ProfileScanAlert => await RouteProfileScanAsync(reviewId, actionInt, executor, ct),
            _ => new ReviewActionResult(false, $"Unknown review type: {reportType}")
        };
    }

    private async Task<ReviewActionResult> RouteContentReportAsync(
        long reviewId, int actionInt, Actor executor, CancellationToken ct)
    {
        if (actionInt < 0 || actionInt > (int)ReportAction.Dismiss)
            return new ReviewActionResult(false, "Invalid action");

        return (ReportAction)actionInt switch
        {
            ReportAction.Spam => await reportActionsService.HandleContentSpamAsync(reviewId, executor, ct),
            ReportAction.Ban => await reportActionsService.HandleContentBanAsync(reviewId, executor, ct),
            ReportAction.Warn => await reportActionsService.HandleContentWarnAsync(reviewId, executor, ct),
            ReportAction.Dismiss => await reportActionsService.HandleContentDismissAsync(reviewId, executor, ct: ct),
            _ => new ReviewActionResult(false, "Unknown action")
        };
    }

    private async Task<ReviewActionResult> RouteImpersonationAsync(
        long reviewId, int actionInt, Actor executor, CancellationToken ct)
    {
        if (actionInt < 0 || actionInt > (int)ImpersonationAction.Trust)
            return new ReviewActionResult(false, "Invalid action");

        return (ImpersonationAction)actionInt switch
        {
            ImpersonationAction.Confirm => await reportActionsService.HandleImpersonationConfirmAsync(reviewId, executor, ct),
            ImpersonationAction.Dismiss => await reportActionsService.HandleImpersonationDismissAsync(reviewId, executor, ct),
            ImpersonationAction.Trust => await reportActionsService.HandleImpersonationTrustAsync(reviewId, executor, ct),
            _ => new ReviewActionResult(false, "Unknown action")
        };
    }

    private async Task<ReviewActionResult> RouteExamAsync(
        long reviewId, int actionInt, Actor executor, CancellationToken ct)
    {
        if (actionInt < 0 || actionInt > (int)ExamAction.DenyAndBan)
            return new ReviewActionResult(false, "Invalid action");

        return (ExamAction)actionInt switch
        {
            ExamAction.Approve => await reportActionsService.HandleExamApproveAsync(reviewId, executor, ct),
            ExamAction.Deny => await reportActionsService.HandleExamDenyAsync(reviewId, executor, ct),
            ExamAction.DenyAndBan => await reportActionsService.HandleExamDenyAndBanAsync(reviewId, executor, ct),
            _ => new ReviewActionResult(false, "Unknown action")
        };
    }

    private async Task<ReviewActionResult> RouteProfileScanAsync(
        long reviewId, int actionInt, Actor executor, CancellationToken ct)
    {
        if (actionInt < 0 || actionInt > (int)ProfileScanAction.Kick)
            return new ReviewActionResult(false, "Invalid action");

        return (ProfileScanAction)actionInt switch
        {
            ProfileScanAction.Allow => await reportActionsService.HandleProfileScanAllowAsync(reviewId, executor, ct),
            ProfileScanAction.Ban => await reportActionsService.HandleProfileScanBanAsync(reviewId, executor, ct),
            ProfileScanAction.Kick => await reportActionsService.HandleProfileScanKickAsync(reviewId, executor, ct),
            _ => new ReviewActionResult(false, "Unknown action")
        };
    }

    private async Task UpdateMessageWithResultAsync(
        CallbackQuery callbackQuery,
        string resultMessage,
        IBotDmService dmService,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message == null)
            return;

        try
        {
            var originalCaption = callbackQuery.Message.Caption ?? callbackQuery.Message.Text ?? "";
            var updatedText = $"{originalCaption}\n\n{resultMessage}";

            if (callbackQuery.Message.Photo != null || callbackQuery.Message.Video != null)
            {
                await dmService.EditDmCaptionAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    updatedText,
                    replyMarkup: null,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await dmService.EditDmTextAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    updatedText,
                    replyMarkup: null,
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update DM message after review action");
        }
    }
}
