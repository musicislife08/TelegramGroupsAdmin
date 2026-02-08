using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Routes Telegram updates to appropriate services.
/// Singleton that creates a scope per-update to resolve scoped services.
/// Contains no business logic - purely routing/dispatching.
/// </summary>
public class UpdateRouter(
    IServiceProvider serviceProvider,
    ILogger<UpdateRouter> logger) : IUpdateRouter
{
    /// <summary>
    /// Route a Telegram update to the appropriate service based on update type.
    /// Creates a scope per-update for proper service lifetime management.
    /// </summary>
    public async Task RouteUpdateAsync(Update update, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Routing update {UpdateId} of type {UpdateType}", update.Id, update.Type);

        // Create scope per-update for proper scoped service lifetime
        // This fixes captive dependency issues and enables one-way layered flow
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        // Resolve services from scope (singletons return same instance, scoped get new instance)
        var chatService = services.GetRequiredService<IBotChatService>();
        var welcomeService = services.GetRequiredService<IWelcomeService>();
        var messageProcessingService = services.GetRequiredService<IMessageProcessingService>();
        var banCallbackService = services.GetRequiredService<IBanCallbackService>();
        var reportCallbackService = services.GetRequiredService<IReportCallbackService>();
        var messageService = services.GetRequiredService<IBotMessageService>();
        var healthOrchestrator = services.GetRequiredService<IChatHealthRefreshOrchestrator>();

        // Handle bot's chat member status changes (added/removed from chats)
        if (update.MyChatMember is { } myChatMember)
        {
            logger.LogDebug(
                "Routing MyChatMember update for chat {Chat}",
                LogDisplayName.ChatDebug(myChatMember.Chat.Title, myChatMember.Chat.Id));
            await chatService.HandleBotMembershipUpdateAsync(myChatMember, cancellationToken);

            // Trigger immediate health check when bot status changes
            await healthOrchestrator.RefreshHealthForChatAsync(myChatMember.Chat.Id, cancellationToken);
            return;
        }

        // Handle user joins/leaves/promotions/demotions
        if (update.ChatMember is { } chatMember)
        {
            var user = chatMember.NewChatMember.User;
            logger.LogDebug(
                "Routing ChatMember update for user {User} in chat {Chat}",
                LogDisplayName.UserDebug(user.FirstName, user.LastName, user.Username, user.Id),
                LogDisplayName.ChatDebug(chatMember.Chat.Title, chatMember.Chat.Id));

            // Check for admin status changes (instant permission updates)
            await chatService.HandleAdminStatusChangeAsync(chatMember, cancellationToken);

            // Handle joins/leaves (welcome system)
            await welcomeService.HandleChatMemberUpdateAsync(chatMember, cancellationToken);
            return;
        }

        // Handle callback queries from inline buttons
        if (update.CallbackQuery is { } callbackQuery)
        {
            logger.LogDebug("Routing CallbackQuery {CallbackId}", callbackQuery.Id);

            // Route to appropriate service based on callback data prefix
            var callbackData = callbackQuery.Data ?? "";
            if (reportCallbackService.CanHandle(callbackData))
            {
                await reportCallbackService.HandleCallbackAsync(callbackQuery, cancellationToken);
            }
            else if (banCallbackService.CanHandle(callbackData))
            {
                await banCallbackService.HandleCallbackAsync(callbackQuery, cancellationToken);
            }
            else
            {
                // Default to welcome service for welcome accept/deny buttons
                await welcomeService.HandleCallbackQueryAsync(callbackQuery, cancellationToken);
            }

            // Always answer callback queries to remove loading state
            await messageService.AnswerCallbackAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            return;
        }

        // Handle new messages
        if (update.Message is { } message)
        {
            // Skip bot's own messages - already saved when sent via BotMessageService
            var userService = services.GetRequiredService<IBotUserService>();
            var botId = await userService.GetBotIdAsync(cancellationToken);
            if (message.From?.Id == botId)
            {
                logger.LogTrace("Skipping bot's own message {MessageId}", message.MessageId);
                return;
            }

            logger.LogDebug(
                "Routing new message {MessageId} from chat {Chat}",
                message.MessageId,
                LogDisplayName.ChatDebug(message.Chat.Title, message.Chat.Id));
            await messageProcessingService.HandleNewMessageAsync(message, cancellationToken);
            return;
        }

        // Handle edited messages
        if (update.EditedMessage is { } editedMessage)
        {
            // Skip bot's own edited messages - already saved via BotMessageService.EditAndUpdateMessageAsync
            var userService = services.GetRequiredService<IBotUserService>();
            var botId = await userService.GetBotIdAsync(cancellationToken);
            if (editedMessage.From?.Id == botId)
            {
                logger.LogTrace("Skipping bot's own edited message {MessageId}", editedMessage.MessageId);
                return;
            }

            logger.LogDebug(
                "Routing edited message {MessageId} from chat {Chat}",
                editedMessage.MessageId,
                LogDisplayName.ChatDebug(editedMessage.Chat.Title, editedMessage.Chat.Id));
            await messageProcessingService.HandleEditedMessageAsync(editedMessage, cancellationToken);
            return;
        }

        // Log unhandled update types for visibility
        logger.LogDebug("Update {UpdateId} of type {UpdateType} not handled by any route", update.Id, update.Type);
    }
}
