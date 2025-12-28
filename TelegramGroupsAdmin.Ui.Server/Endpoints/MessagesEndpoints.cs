using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Extensions;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

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

            return Results.Ok(new MessageActionResponse(
                result.Success,
                result.ErrorMessage,
                result.MessageDeleted));
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

            return Results.Ok(new MessageActionResponse(
                result.Success,
                result.ErrorMessage,
                result.MessageDeleted,
                result.ChatsAffected));
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

            return Results.Ok(new MessageActionResponse(
                result.Success,
                result.ErrorMessage,
                ChatsAffected: result.ChatsAffected,
                TrustRestored: result.TrustRestored));
        });

        // POST /api/messages/{messageId}/temp-ban - Temporarily ban user
        group.MapPost("/{messageId:long}/temp-ban", async (
            long messageId,
            [FromBody] TempBanRequest request,
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

            return Results.Ok(new TempBanResponse(
                result.Success,
                result.ErrorMessage,
                result.Success ? DateTimeOffset.UtcNow.Add(request.Duration) : null));
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
                return Results.BadRequest(new SendMessageResponse(false, "Target chat is not a managed chat"));
            }

            var result = await botMessagingService.SendMessageAsync(
                userId,
                request.ChatId,
                request.Text,
                request.ReplyToMessageId,
                ct);

            if (result.Success)
            {
                return Results.Ok(new SendMessageResponse(true, MessageId: result.Message?.MessageId));
            }
            return Results.BadRequest(new SendMessageResponse(false, result.ErrorMessage));
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
                ? Results.Ok(new ApiResponse(true))
                : Results.BadRequest(new ApiResponse(false, result.ErrorMessage));
        });

        return endpoints;
    }

    // Request DTOs (private to this endpoint class)
    private record TempBanRequest(TimeSpan Duration, string? Reason);
    private record SendMessageRequest(long ChatId, string Text, long? ReplyToMessageId);
    private record EditMessageRequest(string Text);
}
