using System.Text.Json;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized service for sending bot messages AND saving them to the messages table.
/// Ensures all bot-sent messages are tracked in the database for complete conversation history.
/// Phase 1: Bot message storage and deletion tracking
/// </summary>
public class BotMessageService
{
    private readonly IMessageHistoryRepository _messageRepo;
    private readonly IMessageEditService _editService;
    private readonly ITelegramUserRepository _userRepo;
    private readonly ILogger<BotMessageService> _logger;
    private User? _cachedBotInfo; // In-memory cache to avoid repeated GetMe() calls

    public BotMessageService(
        IMessageHistoryRepository messageRepo,
        IMessageEditService editService,
        ITelegramUserRepository userRepo,
        ILogger<BotMessageService> logger)
    {
        _messageRepo = messageRepo;
        _editService = editService;
        _userRepo = userRepo;
        _logger = logger;
    }

    /// <summary>
    /// Send message via bot AND save to messages table.
    /// Returns the sent Message object (contains MessageId for tracking).
    /// </summary>
    public async Task<Message> SendAndSaveMessageAsync(
        ITelegramBotClient botClient,
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        CancellationToken cancellationToken = default)
    {
        // Send message via Telegram
        var sentMessage = parseMode.HasValue
            ? await botClient.SendMessage(
                chatId: chatId,
                text: text,
                parseMode: parseMode.Value,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken)
            : await botClient.SendMessage(
                chatId: chatId,
                text: text,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

        // Get bot user info (fetch once and cache in memory)
        if (_cachedBotInfo == null)
        {
            _cachedBotInfo = await botClient.GetMe(cancellationToken);
            _logger.LogDebug("Fetched and cached bot info: {BotId} (@{BotUsername})", _cachedBotInfo.Id, _cachedBotInfo.Username);
        }
        var botInfo = _cachedBotInfo;

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
            BotDmEnabled: false,
            FirstSeenAt: now,
            LastSeenAt: now,
            CreatedAt: now,
            UpdatedAt: now
        );
        await _userRepo.UpsertAsync(botUser, cancellationToken);

        // Save to messages table (use bot info from cache, not sentMessage.From which may be null)
        var messageRecord = new MessageRecord(
            MessageId: sentMessage.MessageId,
            UserId: botInfo.Id,
            UserName: botInfo.Username,
            FirstName: botInfo.FirstName,
            ChatId: chatId,
            Timestamp: DateTimeOffset.UtcNow,
            MessageText: text,
            PhotoFileId: null,
            PhotoFileSize: null,
            Urls: null,
            EditDate: null,
            ContentHash: null,
            ChatName: sentMessage.Chat.Title ?? sentMessage.Chat.Username,
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

        await _messageRepo.InsertMessageAsync(messageRecord, cancellationToken);

        _logger.LogDebug(
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
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default)
    {
        // Get old message from database for edit history
        var oldMessage = await _messageRepo.GetMessageAsync(messageId, cancellationToken);
        if (oldMessage == null)
        {
            throw new InvalidOperationException($"Message {messageId} not found in database");
        }

        var oldText = oldMessage.MessageText;

        // Edit message via Telegram
        var editedMessage = parseMode.HasValue
            ? await botClient.EditMessageText(
                chatId: chatId,
                messageId: messageId,
                text: text,
                parseMode: parseMode.Value,
                cancellationToken: cancellationToken)
            : await botClient.EditMessageText(
                chatId: chatId,
                messageId: messageId,
                text: text,
                cancellationToken: cancellationToken);

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

        await _editService.InsertMessageEditAsync(editRecord, cancellationToken);

        // Update message in messages table with new text and edit date
        var updatedMessage = oldMessage with
        {
            MessageText = text,
            EditDate = editDate,
            Urls = newUrls != null ? JsonSerializer.Serialize(newUrls) : null,
            ContentHash = newContentHash
        };

        await _messageRepo.UpdateMessageAsync(updatedMessage, cancellationToken);

        _logger.LogDebug(
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
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        string deletionSource = "bot_cleanup",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete from Telegram
            await botClient.DeleteMessage(chatId, messageId, cancellationToken);

            // Mark as deleted in database
            await _messageRepo.MarkMessageAsDeletedAsync(messageId, deletionSource, cancellationToken);

            _logger.LogDebug(
                "Deleted and marked message {MessageId} (chat: {ChatId}, source: {Source})",
                messageId,
                chatId,
                deletionSource);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} from Telegram (chat: {ChatId}), marking as deleted in DB anyway",
                messageId,
                chatId);

            // Still try to mark as deleted in DB even if Telegram deletion failed
            // (message might already be deleted, or we lost permissions)
            try
            {
                await _messageRepo.MarkMessageAsDeletedAsync(messageId, $"{deletionSource}_failed", cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx,
                    "Failed to mark message {MessageId} as deleted in database",
                    messageId);
            }
        }
    }
}
