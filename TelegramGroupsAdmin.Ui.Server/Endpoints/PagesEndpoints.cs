using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Extensions;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

/// <summary>
/// Aggregate "page-oriented" endpoints that bundle all data needed for a UI page in a single HTTP call.
/// These endpoints reduce HTTP round trips by returning everything a page needs to render.
/// Route pattern: /api/pages/{resource}
/// </summary>
public static class PagesEndpoints
{
    public static IEndpointRouteBuilder MapPagesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pages")
            .RequireAuthorization();

        // GET /api/pages/messages - Full page data for Messages page
        group.MapGet("/messages", async (
            [FromQuery] long? chatId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] DateTimeOffset? before,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] IMessageQueryService queryService,
            [FromServices] IMessageEditService editService,
            [FromServices] ITelegramUserMappingRepository mappingRepo,
            [FromServices] IWebBotMessagingService botMessagingService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();

            pageSize = Math.Clamp(pageSize, 10, 100);

            // Get user context (linked accounts, bot features) in parallel with chat data
            var mappingsTask = mappingRepo.GetByUserIdAsync(userId, ct);
            var botAvailabilityTask = botMessagingService.CheckFeatureAvailabilityAsync(userId, ct);
            var chatsTask = chatsRepo.GetUserAccessibleChatsAsync(userId, permissionLevel, includeDeleted: false, ct);

            await Task.WhenAll(mappingsTask, botAvailabilityTask, chatsTask);

            var mappings = await mappingsTask;
            var botAvailability = await botAvailabilityTask;
            var chats = await chatsTask;

            var linkedTelegramIds = mappings.Select(m => m.TelegramId).ToList();
            var chatSummaries = chats.ToChatSummaries();

            var userContext = new MessagesPageUserContext(
                (int)permissionLevel,
                linkedTelegramIds,
                botAvailability.IsAvailable,
                botAvailability.LinkedUsername,
                botAvailability.BotUserId,
                botAvailability.UnavailableReason
            );

            var selectedChatId = chatId ?? chats.FirstOrDefault()?.ChatId;

            List<MessageWithMetadata> messages = [];
            var totalCount = 0;

            if (selectedChatId.HasValue)
            {
                var hasAccess = permissionLevel >= PermissionLevel.GlobalAdmin ||
                    chats.Any(c => c.ChatId == selectedChatId.Value);

                if (!hasAccess)
                {
                    return Results.Forbid();
                }

                // Get messages with cursor-based pagination
                var messageRecords = await queryService.GetMessagesWithDetectionHistoryAsync(
                    selectedChatId.Value,
                    pageSize,
                    beforeTimestamp: before,
                    ct);

                // Get metadata in batch
                var messageIds = messageRecords.Select(m => m.Message.MessageId).ToList();
                var editCounts = await editService.GetEditCountsForMessagesAsync(messageIds, ct);
                var contentChecks = await queryService.GetContentChecksForMessagesAsync(messageIds, ct);

                messages = messageRecords.Select(m =>
                {
                    editCounts.TryGetValue(m.Message.MessageId, out var editCount);
                    contentChecks.TryGetValue(m.Message.MessageId, out var check);

                    ContentCheckSummary? checkSummary = null;
                    if (check != null)
                    {
                        checkSummary = new ContentCheckSummary(
                            check.IsSpam,
                            check.Confidence,
                            check.Reason,
                            check.CheckTimestamp
                        );
                    }

                    return new MessageWithMetadata(m.Message, editCount, checkSummary);
                }).ToList();

                totalCount = await messagesRepo.GetMessageCountByChatIdAsync(selectedChatId.Value, ct);
            }

            return Results.Ok(new MessagesPageResponse(
                chatSummaries,
                messages,
                new PaginationInfo(page, pageSize, totalCount, messages.Count == pageSize),
                selectedChatId,
                userContext
            ));
        });

        // GET /api/pages/messages/{messageId} - Full data for message detail modal
        group.MapGet("/messages/{messageId:long}", async (
            long messageId,
            [FromServices] IManagedChatsRepository chatsRepo,
            [FromServices] IMessageHistoryRepository messagesRepo,
            [FromServices] IMessageEditService editService,
            [FromServices] IDetectionResultsRepository detectionRepo,
            [FromServices] ITelegramUserRepository userRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null) return Results.Unauthorized();

            var (message, error) = await messagesRepo.GetMessageWithAccessCheckAsync(
                chatsRepo, messageId, userId, permissionLevel, ct);
            if (error != null) return error;

            var telegramUser = await userRepo.GetByTelegramIdAsync(message!.UserId, ct);

            // Get detection history (GlobalAdmin+ only)
            var detectionHistory = permissionLevel >= PermissionLevel.GlobalAdmin
                ? await detectionRepo.GetByMessageIdAsync(messageId, ct)
                : [];

            // Get edit history
            var editHistory = await editService.GetEditsForMessageAsync(messageId, ct);

            return Results.Ok(new MessageDetailResponse(
                message,
                telegramUser,
                detectionHistory,
                editHistory,
                null, // TODO: UserTags
                null  // TODO: UserNotes
            ));
        });

        return endpoints;
    }
}
