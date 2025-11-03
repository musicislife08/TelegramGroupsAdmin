using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized service for sending bot messages AND saving them to the messages table.
/// Ensures all bot-sent messages are tracked in the database for complete conversation history.
/// Phase 1: Bot message storage and deletion tracking
/// </summary>
public class BotMessageService
{
    private readonly IMessageHistoryRepository _messageRepo;
    private readonly ITelegramUserRepository _userRepo;
    private readonly TelegramAdminBotService _botService;
    private readonly ILogger<BotMessageService> _logger;

    public BotMessageService(
        IMessageHistoryRepository messageRepo,
        ITelegramUserRepository userRepo,
        TelegramAdminBotService botService,
        ILogger<BotMessageService> logger)
    {
        _messageRepo = messageRepo;
        _userRepo = userRepo;
        _botService = botService;
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

        // Get bot user info (cached from startup)
        var botInfo = _botService.BotUserInfo;
        if (botInfo == null)
        {
            _logger.LogWarning("Bot user info not cached yet, fetching from API");
            botInfo = await botClient.GetMe(cancellationToken);
        }

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
            SpamCheckSkipReason: SpamCheckSkipReason.UserAdmin // Bot messages skip spam checks
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
