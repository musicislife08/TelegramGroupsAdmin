using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Backfills messages to the database when they weren't captured in real-time.
/// Handles edge cases like old messages (Telegram 48-hour API limit), deleted messages,
/// and media-only content gracefully.
/// </summary>
public class MessageBackfillService : IMessageBackfillService
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly ILogger<MessageBackfillService> _logger;

    public MessageBackfillService(
        IMessageHistoryRepository messageHistoryRepository,
        ILogger<MessageBackfillService> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> BackfillIfMissingAsync(
        long messageId,
        long chatId,
        Message telegramMessage,
        CancellationToken cancellationToken = default)
    {
        // Check if message already exists in database
        var existingMessage = await _messageHistoryRepository.GetMessageAsync(messageId, cancellationToken);
        if (existingMessage != null)
        {
            _logger.LogDebug(
                "Message {MessageId} already exists in database, skipping backfill",
                messageId);
            return false;
        }

        // Extract text content (text or caption for media messages)
        var messageText = telegramMessage.Text ?? telegramMessage.Caption;

        if (string.IsNullOrWhiteSpace(messageText))
        {
            // Media-only message or empty content - can't use for text-based training
            _logger.LogInformation(
                "Message {MessageId} has no text content (media-only or empty). " +
                "Skipping backfill - message can still be used for image training if applicable.",
                messageId);
            return false;
        }

        try
        {
            var messageRecord = new MessageRecord(
                MessageId: messageId,
                User: telegramMessage.From is { } from ? UserIdentity.From(from) : UserIdentity.FromId(0),
                Chat: ChatIdentity.FromId(chatId),
                Timestamp: telegramMessage.Date,
                MessageText: messageText,
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
                ReplyToMessageId: telegramMessage.ReplyToMessage?.MessageId,
                ReplyToUser: telegramMessage.ReplyToMessage?.From?.Username,
                ReplyToText: telegramMessage.ReplyToMessage?.Text,
                MediaType: null,
                MediaFileId: null,
                MediaFileSize: null,
                MediaFileName: null,
                MediaMimeType: null,
                MediaLocalPath: null,
                MediaDuration: null,
                Translation: null,
                ContentCheckSkipReason: ContentCheckSkipReason.NotSkipped
            );

            await _messageHistoryRepository.InsertMessageAsync(messageRecord, cancellationToken);

            _logger.LogInformation(
                "Backfilled message {MessageId} from chat {ChatId} for user {UserId}",
                messageId, chatId, telegramMessage.From?.Id ?? 0);

            return true;
        }
        catch (Exception ex)
        {
            // Don't fail the moderation action if backfill fails
            // The message might be too old, already deleted, or have other issues
            _logger.LogWarning(ex,
                "Failed to backfill message {MessageId}. This is non-critical - " +
                "moderation action will proceed but training data may be incomplete. " +
                "Common causes: message too old (>48h), already deleted, or database constraint.",
                messageId);
            return false;
        }
    }
}
