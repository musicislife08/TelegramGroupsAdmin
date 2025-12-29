using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Extensions;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Actions;

/// <summary>
/// Focused action endpoints for message operations.
/// These endpoints do one thing: CRUD operations and specific actions.
/// Route pattern: /api/messages/{action}
///
/// For page data (aggregate endpoints), see PagesEndpoints.cs
/// </summary>
public static class MessagesEndpoints
{
    public static IEndpointRouteBuilder MapMessagesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/messages")
            .RequireAuthorization();

        // GET /api/messages/chats - Get accessible chats for sidebar
        group.MapGet("/chats", async (
            [FromServices] IManagedChatsRepository chatsRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();

            var chats = await chatsRepo.GetUserAccessibleChatsAsync(userId, permissionLevel, includeDeleted: false, ct);

            return Results.Ok(chats.ToChatSummaries());
        });

        // POST /api/messages/{messageId}/delete - Delete message
        group.MapPost("/{messageId:long}/delete", async (
            long messageId,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] ModerationOrchestrator moderationOrchestrator,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            var executor = Actor.FromWebUser(userId);
            var result = await moderationOrchestrator.DeleteMessageAsync(
                messageId,
                message!.ChatId,
                message.UserId,
                executor,
                reason: "Deleted via web UI",
                ct);

            return result.Success
                ? Results.Ok(MessageActionResponse.Ok(messageDeleted: result.MessageDeleted))
                : Results.BadRequest(MessageActionResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/{messageId}/spam - Mark as spam and ban
        group.MapPost("/{messageId:long}/spam", async (
            long messageId,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] ModerationOrchestrator moderationOrchestrator,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            var executor = Actor.FromWebUser(userId);
            var result = await moderationOrchestrator.MarkAsSpamAndBanAsync(
                messageId,
                message!.UserId,
                message.ChatId,
                executor,
                reason: "Marked as spam via web UI",
                telegramMessage: null,
                ct);

            return result.Success
                ? Results.Ok(MessageActionResponse.Ok(messageDeleted: result.MessageDeleted, chatsAffected: result.ChatsAffected))
                : Results.BadRequest(MessageActionResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/{messageId}/ham - Mark as not spam (unban + restore trust)
        group.MapPost("/{messageId:long}/ham", async (
            long messageId,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] ModerationOrchestrator moderationOrchestrator,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            var executor = Actor.FromWebUser(userId);
            var result = await moderationOrchestrator.UnbanUserAsync(
                message!.UserId,
                executor,
                reason: "Marked as not spam via web UI (false positive)",
                restoreTrust: true,
                ct);

            return result.Success
                ? Results.Ok(MessageActionResponse.Ok(chatsAffected: result.ChatsAffected, trustRestored: result.TrustRestored))
                : Results.BadRequest(MessageActionResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/{messageId}/temp-ban - Temporarily ban user
        group.MapPost("/{messageId:long}/temp-ban", async (
            long messageId,
            [FromBody] UserTempBanRequest request,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] ModerationOrchestrator moderationOrchestrator,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            var executor = Actor.FromWebUser(userId);
            var result = await moderationOrchestrator.TempBanUserAsync(
                userId: message!.UserId,
                messageId: messageId,
                executor: executor,
                reason: request.Reason ?? "Temporarily banned via web UI",
                duration: request.Duration,
                ct);

            return result.Success
                ? Results.Ok(TempBanResponse.Ok(DateTimeOffset.UtcNow.Add(request.Duration)))
                : Results.BadRequest(TempBanResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/send - Send a new message as bot
        group.MapPost("/send", async (
            [FromBody] SendMessageRequest request,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IWebBotMessagingService botMessagingService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            // Validate target chat is a managed chat (defense-in-depth)
            var managedChat = await chatsRepo.GetByChatIdAsync(request.ChatId, ct);
            if (managedChat == null)
            {
                return Results.BadRequest(SendMessageResponse.Fail("Target chat is not a managed chat"));
            }

            var result = await botMessagingService.SendMessageAsync(
                userId,
                request.ChatId,
                request.Text,
                request.ReplyToMessageId,
                ct);

            return result.Success
                ? Results.Ok(SendMessageResponse.Ok(result.Message?.MessageId))
                : Results.BadRequest(SendMessageResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/{messageId}/edit - Edit a bot message
        group.MapPost("/{messageId:long}/edit", async (
            long messageId,
            [FromBody] EditMessageRequest request,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] IWebBotMessagingService botMessagingService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.GlobalAdmin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            // Note: EditMessageAsync expects int messageId (Telegram API limitation)
            var result = await botMessagingService.EditMessageAsync(
                userId,
                message!.ChatId,
                (int)messageId,
                request.Text,
                ct);

            return result.Success
                ? Results.Ok(ApiResponse.Ok())
                : Results.BadRequest(ApiResponse.Fail(result.ErrorMessage!));
        });

        // POST /api/messages/{messageId}/translate - Manually translate a message
        group.MapPost("/{messageId:long}/translate", async (
            long messageId,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] IAITranslationService translationService,
            [FromServices] IMessageTranslationService messageTranslationService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();
            if (permissionLevel < PermissionLevel.Admin) return Results.Forbid();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            if (string.IsNullOrWhiteSpace(message!.MessageText))
            {
                return Results.BadRequest(TranslateMessageResponse.Fail("Message has no text to translate"));
            }

            // Call AI translation service
            var translationResult = await translationService.TranslateToEnglishAsync(message.MessageText, ct);

            if (translationResult == null || !translationResult.WasTranslated)
            {
                return Results.Ok(TranslateMessageResponse.Fail("Message is already in English or translation failed"));
            }

            // Save translation to database
            var translation = new MessageTranslation(
                Id: 0,
                MessageId: messageId,
                EditId: null,
                TranslatedText: translationResult.TranslatedText,
                DetectedLanguage: translationResult.DetectedLanguage,
                Confidence: null,
                TranslatedAt: DateTimeOffset.UtcNow
            );

            await messageTranslationService.InsertTranslationAsync(translation, ct);

            return Results.Ok(TranslateMessageResponse.Ok(
                translationResult.TranslatedText,
                translationResult.DetectedLanguage));
        });

        return endpoints;
    }
}
