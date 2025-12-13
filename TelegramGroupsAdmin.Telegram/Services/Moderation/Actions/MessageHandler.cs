using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for message operations.
/// Owns backfill (ensuring messages exist in DB) and deletion.
/// </summary>
public class MessageHandler : IMessageHandler
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IMessageBackfillService _messageBackfillService;
    private readonly IBotMessageService _botMessageService;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IMessageBackfillService messageBackfillService,
        IBotMessageService botMessageService,
        ILogger<MessageHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _messageBackfillService = messageBackfillService;
        _botMessageService = botMessageService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackfillResult> EnsureExistsAsync(
        long messageId,
        long chatId,
        Message? telegramMessage = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Ensuring message {MessageId} exists in database for chat {ChatId}",
            messageId, chatId);

        // Check if message already exists
        var existingMessage = await _messageHistoryRepository.GetMessageAsync(messageId, ct);
        if (existingMessage != null)
        {
            _logger.LogDebug("Message {MessageId} already exists in database", messageId);
            return BackfillResult.AlreadyExists();
        }

        // Try to backfill if we have a Telegram message object
        if (telegramMessage != null)
        {
            var backfilled = await _messageBackfillService.BackfillIfMissingAsync(
                messageId, chatId, telegramMessage, ct);

            if (backfilled)
            {
                _logger.LogInformation(
                    "Message {MessageId} backfilled from Telegram object",
                    messageId);
                return BackfillResult.Backfilled();
            }
        }

        // Message not found and couldn't be backfilled
        _logger.LogWarning(
            "Message {MessageId} not found in database and could not be backfilled " +
            "(telegramMessage provided: {HasTelegramMessage})",
            messageId, telegramMessage != null);

        return BackfillResult.NotFound();
    }

    /// <inheritdoc />
    public async Task<DeleteResult> DeleteAsync(
        long chatId,
        long messageId,
        Actor executor,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Deleting message {MessageId} from chat {ChatId} by {Executor}",
            messageId, chatId, executor.GetDisplayText());

        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                chatId,
                (int)messageId,
                deletionSource: "moderation_action",
                ct);

            _logger.LogInformation(
                "Deleted message {MessageId} from chat {ChatId}",
                messageId, chatId);

            return DeleteResult.Succeeded(messageDeleted: true);
        }
        catch (Exception ex)
        {
            // Report the failure - let the orchestrator decide what to do
            // (message may already be deleted, API error, etc.)
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} in chat {ChatId}",
                messageId, chatId);

            return DeleteResult.Failed(ex.Message);
        }
    }
}
