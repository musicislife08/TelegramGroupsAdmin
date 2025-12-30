using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Ui.Api;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Extensions;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Pages;

/// <summary>
/// Aggregate endpoint for the Messages page.
/// Bundles all data needed for the Messages UI in a single HTTP call.
/// Route pattern: /api/pages/messages
/// </summary>
public static class MessagesPageEndpoints
{
    public static IEndpointRouteBuilder MapMessagesPageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Routes.Pages.Base)
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
            [FromServices] IUserTagsRepository tagsRepo,
            [FromServices] IAdminNotesRepository notesRepo,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = httpContext.GetUserId();
            var permissionLevel = httpContext.GetPermissionLevel();

            if (userId == null)
                return Results.Ok(MessagesPageResponse.Fail("User not authenticated"));

            pageSize = Math.Clamp(pageSize, 10, 100);

            // Get user context (linked accounts, bot features) in parallel with chat data
            var mappingsTask = mappingRepo.GetByUserIdAsync(userId, ct);
            var botAvailabilityTask = botMessagingService.CheckFeatureAvailabilityAsync(userId, ct);
            var chatsTask = chatsRepo.GetUserAccessibleChatsAsync(userId, permissionLevel, includeDeleted: false, ct);

            await Task.WhenAll(mappingsTask, botAvailabilityTask, chatsTask);

            var mappings = await mappingsTask;
            var botAvailability = await botAvailabilityTask;
            var chats = await chatsTask;

            // Get last message previews for all chats (for sidebar display)
            var chatIds = chats.Select(c => c.ChatId).ToList();
            var lastMessagePreviews = await queryService.GetLastMessagePreviewsAsync(chatIds, ct);

            var linkedTelegramIds = mappings.Select(m => m.TelegramId).ToList();
            var chatSummaries = chats.ToChatSummaries(lastMessagePreviews);

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

            if (selectedChatId is { } activeChatId)
            {
                var hasAccess = permissionLevel >= PermissionLevel.GlobalAdmin ||
                    chats.Any(c => c.ChatId == activeChatId);

                if (!hasAccess)
                {
                    return Results.Ok(MessagesPageResponse.Fail("You don't have access to this chat"));
                }

                // Get messages with cursor-based pagination
                var messageRecords = await queryService.GetMessagesWithDetectionHistoryAsync(
                    activeChatId,
                    pageSize,
                    beforeTimestamp: before,
                    ct);

                // Get metadata in batch
                var messageIds = messageRecords.Select(m => m.Message.MessageId).ToList();
                var userIds = messageRecords.Select(m => m.Message.UserId).Distinct().ToList();

                var editCounts = await editService.GetEditCountsForMessagesAsync(messageIds, ct);
                var contentChecks = await queryService.GetContentChecksForMessagesAsync(messageIds, ct);
                var userTags = await tagsRepo.GetTagsByUserIdsAsync(userIds, ct);
                var userNotes = await notesRepo.GetNotesByUserIdsAsync(userIds, ct);

                messages = messageRecords.Select(m =>
                {
                    editCounts.TryGetValue(m.Message.MessageId, out var editCount);
                    contentChecks.TryGetValue(m.Message.MessageId, out var check);
                    userTags.TryGetValue(m.Message.UserId, out var tags);
                    userNotes.TryGetValue(m.Message.UserId, out var notes);

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

                    return new MessageWithMetadata(m.Message, editCount, checkSummary, UserTags: tags, UserNotes: notes);
                }).ToList();

                totalCount = await messagesRepo.GetMessageCountByChatIdAsync(selectedChatId.Value, ct);
            }

            return Results.Ok(MessagesPageResponse.Ok(
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
            [FromServices] IUserTagsRepository tagsRepo,
            [FromServices] IAdminNotesRepository notesRepo,
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

            // Get user tags and admin notes
            var userTags = await tagsRepo.GetTagsByUserIdAsync(message.UserId, ct);
            var userNotes = await notesRepo.GetNotesByUserIdAsync(message.UserId, ct);

            return Results.Ok(new MessageDetailResponse(
                message,
                telegramUser,
                detectionHistory,
                editHistory,
                userTags,
                userNotes
            ));
        });

        return endpoints;
    }
}
