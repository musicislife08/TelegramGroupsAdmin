using System.Text.Json;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized service for sending bot messages AND saving them to the messages table.
/// Ensures all bot-sent messages are tracked in the database for complete conversation history.
/// Orchestrates handlers and adds DB persistence logic.
/// </summary>
public class BotMessageService(
    IBotMessageHandler messageHandler,
    IBotUserService userService,
    IBotChatHandler chatHandler,
    IMessageHistoryRepository messageRepo,
    IMessageEditService editService,
    ITelegramUserRepository userRepo,
    ILogger<BotMessageService> logger) : IBotMessageService
{

    /// <summary>
    /// Send message via bot AND save to messages table.
    /// Returns the sent Message object (contains MessageId for tracking).
    /// </summary>
    public async Task<Message> SendAndSaveMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        // Send message via handler
        var sentMessage = await messageHandler.SendAsync(
            chatId: chatId,
            text: text,
            parseMode: parseMode,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        // Get bot user info (cached in singleton IBotIdentityCache via IBotUserService)
        var botInfo = await userService.GetMeAsync(cancellationToken);

        // Upsert bot to telegram_users table (ensures bot name is available for UI display)
        var now = DateTimeOffset.UtcNow;
        var botUser = new TelegramUser(
            TelegramUserId: botInfo.Id,
            Username: botInfo.Username,
            FirstName: botInfo.FirstName,
            LastName: botInfo.LastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: true,
            IsTrusted: false,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now
        );
        await userRepo.UpsertAsync(botUser, cancellationToken);

        // Save to messages table (use bot info from cache, not sentMessage.From which may be null)
        var messageRecord = new MessageRecord(
            MessageId: sentMessage.MessageId,
            User: new UserIdentity(botInfo.Id, botInfo.FirstName, botInfo.LastName, botInfo.Username),
            Chat: new ChatIdentity(chatId, sentMessage.Chat.Title ?? sentMessage.Chat.Username),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: text,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: replyParameters?.MessageId,
            ReplyToUser: null, // Populated by repository queries via JOIN
            ReplyToText: null, // Populated by repository queries via JOIN
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.UserAdmin // Bot messages skip content checks
        );

        await messageRepo.InsertMessageAsync(messageRecord, cancellationToken);

        logger.LogDebug(
            "Saved bot message {MessageId} to history (chat: {ChatId}, reply_to: {ReplyTo})",
            sentMessage.MessageId,
            chatId,
            replyParameters?.MessageId);

        return sentMessage;
    }

    /// <summary>
    /// Edit message via bot AND save edit history to message_edits table.
    /// Used for web UI editing of bot messages (Phase 1: Send & Edit Messages as Bot).
    /// </summary>
    public async Task<Message> EditAndUpdateMessageAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        // Get old message from database for edit history
        var oldMessage = await messageRepo.GetMessageAsync(messageId, cancellationToken);
        if (oldMessage == null)
        {
            throw new InvalidOperationException($"Message {messageId} not found in database");
        }

        var oldText = oldMessage.MessageText;

        // Edit message via handler
        var editedMessage = await messageHandler.EditTextAsync(
            chatId: chatId,
            messageId: messageId,
            text: text,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        var editDate = editedMessage.EditDate.HasValue
            ? new DateTimeOffset(editedMessage.EditDate.Value, TimeSpan.Zero) // DateTime (UTC) → DateTimeOffset
            : DateTimeOffset.UtcNow; // Fallback if Telegram doesn't provide EditDate

        // Extract URLs and calculate content hashes
        var oldUrls = UrlUtilities.ExtractUrls(oldText);
        var newUrls = UrlUtilities.ExtractUrls(text);

        var oldContentHash = HashUtilities.ComputeContentHash(oldText ?? "", oldUrls != null ? JsonSerializer.Serialize(oldUrls) : "");
        var newContentHash = HashUtilities.ComputeContentHash(text ?? "", newUrls != null ? JsonSerializer.Serialize(newUrls) : "");

        // Save edit history to message_edits table
        var editRecord = new MessageEditRecord(
            Id: 0, // Will be set by INSERT
            MessageId: messageId,
            EditDate: editDate,
            OldText: oldText,
            NewText: text,
            OldContentHash: oldContentHash,
            NewContentHash: newContentHash
        );

        await editService.InsertMessageEditAsync(editRecord, cancellationToken);

        // Update message in messages table with new text and edit date
        var updatedMessage = oldMessage with
        {
            MessageText = text,
            EditDate = editDate,
            Urls = newUrls != null ? JsonSerializer.Serialize(newUrls) : null,
            ContentHash = newContentHash
        };

        await messageRepo.UpdateMessageAsync(updatedMessage, cancellationToken);

        logger.LogDebug(
            "Edited bot message {MessageId} (chat: {ChatId}) and saved edit history",
            messageId,
            chatId);

        return editedMessage;
    }

    /// <summary>
    /// Delete message via bot AND mark as deleted in database.
    /// Gracefully handles cases where message is already deleted from Telegram.
    /// </summary>
    public async Task DeleteAndMarkMessageAsync(
        long chatId,
        int messageId,
        string deletionSource = "bot_cleanup",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete from Telegram via handler
            await messageHandler.DeleteAsync(chatId, messageId, cancellationToken);

            // Mark as deleted in database
            await messageRepo.MarkMessageAsDeletedAsync(messageId, deletionSource, cancellationToken);

            logger.LogDebug(
                "Deleted and marked message {MessageId} (chat: {ChatId}, source: {Source})",
                messageId,
                chatId,
                deletionSource);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to delete message {MessageId} from Telegram (chat: {ChatId}), marking as deleted in DB anyway",
                messageId,
                chatId);

            // Still try to mark as deleted in DB even if Telegram deletion failed
            // (message might already be deleted, or we lost permissions)
            try
            {
                await messageRepo.MarkMessageAsDeletedAsync(messageId, $"{deletionSource}_failed", cancellationToken);
            }
            catch (Exception dbEx)
            {
                logger.LogError(dbEx,
                    "Failed to mark message {MessageId} as deleted in database",
                    messageId);
            }

            // Re-throw so callers know the Telegram delete failed
            // (DB cleanup is done, but caller should log appropriately)
            throw;
        }
    }

    /// <summary>
    /// Save a bot message to the database without sending it.
    /// Used when the message was already sent and then edited (e.g., verifying → welcome message).
    /// </summary>
    public async Task SaveBotMessageAsync(
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        // Get bot user info (cached in singleton IBotIdentityCache via IBotUserService)
        var botInfo = await userService.GetMeAsync(cancellationToken);

        // Get chat info for the name
        var chatInfo = await chatHandler.GetChatAsync(chatId, cancellationToken);

        // Upsert bot to telegram_users table (ensures bot name is available for UI display)
        var now = DateTimeOffset.UtcNow;
        var botUser = new TelegramUser(
            TelegramUserId: botInfo.Id,
            Username: botInfo.Username,
            FirstName: botInfo.FirstName,
            LastName: botInfo.LastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: true,
            IsTrusted: false,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now
        );
        await userRepo.UpsertAsync(botUser, cancellationToken);

        // Save to messages table
        var messageRecord = new MessageRecord(
            MessageId: messageId,
            User: new UserIdentity(botInfo.Id, botInfo.FirstName, botInfo.LastName, botInfo.Username),
            Chat: new ChatIdentity(chatId, chatInfo.Title ?? chatInfo.Username),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: text,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: null,
            MediaFileId: null,
            MediaFileSize: null,
            MediaFileName: null,
            MediaMimeType: null,
            MediaLocalPath: null,
            MediaDuration: null,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.UserAdmin // Bot messages skip content checks
        );

        await messageRepo.InsertMessageAsync(messageRecord, cancellationToken);

        logger.LogDebug(
            "Saved bot message {MessageId} to history (chat: {ChatId})",
            messageId,
            chatId);
    }

    /// <summary>
    /// Answer a callback query to acknowledge button click and remove loading state.
    /// </summary>
    public async Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken cancellationToken = default)
    {
        await messageHandler.AnswerCallbackAsync(
            callbackQueryId: callbackQueryId,
            text: text,
            showAlert: showAlert,
            ct: cancellationToken);
    }

    /// <summary>
    /// Send an animation (GIF) to a chat AND save to message history.
    /// Used for ban celebrations and other GIF content that should appear in message history.
    /// </summary>
    public async Task<Message> SendAndSaveAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default)
    {
        // Send animation via handler
        var sentMessage = await messageHandler.SendAnimationAsync(
            chatId: chatId,
            animation: animation,
            caption: caption,
            parseMode: parseMode,
            ct: cancellationToken);

        // Get bot user info (cached in singleton IBotIdentityCache via IBotUserService)
        var botInfo = await userService.GetMeAsync(cancellationToken);

        // Upsert bot to telegram_users table
        var now = DateTimeOffset.UtcNow;
        var botUser = new TelegramUser(
            TelegramUserId: botInfo.Id,
            Username: botInfo.Username,
            FirstName: botInfo.FirstName,
            LastName: botInfo.LastName,
            UserPhotoPath: null,
            PhotoHash: null,
            PhotoFileUniqueId: null,
            IsBot: true,
            IsTrusted: false,
            IsBanned: false,
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now
        );
        await userRepo.UpsertAsync(botUser, cancellationToken);

        // Save to messages table with animation metadata
        var messageRecord = new MessageRecord(
            MessageId: sentMessage.MessageId,
            User: new UserIdentity(botInfo.Id, botInfo.FirstName, botInfo.LastName, botInfo.Username),
            Chat: new ChatIdentity(chatId, sentMessage.Chat.Title ?? sentMessage.Chat.Username),
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: caption, // Caption as message text
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            PhotoLocalPath: null,
            PhotoThumbnailPath: null,
            ChatIconPath: null,
            UserPhotoPath: null,
            DeletedAt: null,
            DeletionSource: null,
            ReplyToMessageId: null,
            ReplyToUser: null,
            ReplyToText: null,
            MediaType: Models.MediaType.Animation,
            MediaFileId: sentMessage.Animation?.FileId,
            MediaFileSize: sentMessage.Animation?.FileSize,
            MediaFileName: sentMessage.Animation?.FileName,
            MediaMimeType: sentMessage.Animation?.MimeType,
            MediaLocalPath: null,
            MediaDuration: sentMessage.Animation?.Duration,
            Translation: null,
            ContentCheckSkipReason: ContentCheckSkipReason.UserAdmin // Bot messages skip content checks
        );

        await messageRepo.InsertMessageAsync(messageRecord, cancellationToken);

        logger.LogDebug(
            "Saved bot animation {MessageId} to history (chat: {ChatId})",
            sentMessage.MessageId,
            chatId);

        return sentMessage;
    }
}
