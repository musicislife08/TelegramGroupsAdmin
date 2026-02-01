using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Routes Telegram updates to appropriate handler services.
/// Extracted for testability - pure routing logic with mockable dependencies.
/// </summary>
public class UpdateProcessor(
    IMessageProcessingService messageProcessingService,
    IBotChatHealthService chatHealthService,
    IWelcomeService welcomeService,
    IBanCallbackHandler banCallbackHandler,
    IReportCallbackHandler reportCallbackHandler,
    IBotMessageService messageService,
    ILogger<UpdateProcessor> logger) : IUpdateProcessor
{
    /// <summary>
    /// Process a Telegram update - routes to appropriate handler based on update type.
    /// </summary>
    /// <param name="update">The Telegram update to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Processing update {UpdateId} of type {UpdateType}", update.Id, update.Type);

        // Handle bot's chat member status changes (added/removed from chats)
        if (update.MyChatMember is { } myChatMember)
        {
            logger.LogDebug(
                "Routing MyChatMember update for chat {Chat}",
                LogDisplayName.ChatDebug(myChatMember.Chat.Title, myChatMember.Chat.Id));
            await chatHealthService.HandleMyChatMemberUpdateAsync(myChatMember, cancellationToken);
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
            await chatHealthService.HandleAdminStatusChangeAsync(chatMember, cancellationToken);

            // Handle joins/leaves (welcome system)
            await welcomeService.HandleChatMemberUpdateAsync(chatMember, cancellationToken);
            return;
        }

        // Handle callback queries from inline buttons
        if (update.CallbackQuery is { } callbackQuery)
        {
            logger.LogDebug("Routing CallbackQuery {CallbackId}", callbackQuery.Id);

            // Route to appropriate handler based on callback data prefix
            var callbackData = callbackQuery.Data ?? "";
            if (reportCallbackHandler.CanHandle(callbackData))
            {
                await reportCallbackHandler.HandleCallbackAsync(callbackQuery, cancellationToken);
            }
            else if (banCallbackHandler.CanHandle(callbackData))
            {
                await banCallbackHandler.HandleCallbackAsync(callbackQuery, cancellationToken);
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
            logger.LogDebug(
                "Routing edited message {MessageId} from chat {Chat}",
                editedMessage.MessageId,
                LogDisplayName.ChatDebug(editedMessage.Chat.Title, editedMessage.Chat.Id));
            await messageProcessingService.HandleEditedMessageAsync(editedMessage, cancellationToken);
            return;
        }

        // Log unhandled update types for visibility
        logger.LogDebug("Update {UpdateId} of type {UpdateType} not handled by any processor", update.Id, update.Type);
    }
}
