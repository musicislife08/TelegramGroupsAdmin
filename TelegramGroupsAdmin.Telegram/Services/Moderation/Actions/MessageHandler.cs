using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
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
    private readonly IMessageQueryService _messageQueryService;
    private readonly IMessageBackfillService _messageBackfillService;
    private readonly IBotMessageService _botMessageService;
    private readonly IManagedChatsRepository _chatsRepository;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IMessageQueryService messageQueryService,
        IMessageBackfillService messageBackfillService,
        IBotMessageService botMessageService,
        IManagedChatsRepository chatsRepository,
        IJobScheduler jobScheduler,
        ILogger<MessageHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _messageQueryService = messageQueryService;
        _messageBackfillService = messageBackfillService;
        _botMessageService = botMessageService;
        _chatsRepository = chatsRepository;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackfillResult> EnsureExistsAsync(
        long messageId,
        long chatId,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var chat = await _chatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        _logger.LogDebug(
            "Ensuring message {MessageId} exists in database for {Chat}",
            messageId, chat.ToLogDebug(chatId));

        // Check if message already exists
        var existingMessage = await _messageHistoryRepository.GetMessageAsync(messageId, cancellationToken);
        if (existingMessage != null)
        {
            _logger.LogDebug("Message {MessageId} already exists in database", messageId);
            return BackfillResult.AlreadyExists();
        }

        // Try to backfill if we have a Telegram message object
        if (telegramMessage != null)
        {
            var backfilled = await _messageBackfillService.BackfillIfMissingAsync(
                messageId, chatId, telegramMessage, cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var chat = await _chatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        _logger.LogDebug(
            "Deleting message {MessageId} from {Chat} by {Executor}",
            messageId, chat.ToLogDebug(chatId), executor.GetDisplayText());

        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                chatId,
                (int)messageId,
                deletionSource: "moderation_action",
                cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} from {Chat}",
                messageId, chat.ToLogInfo(chatId));

            return DeleteResult.Succeeded(messageDeleted: true);
        }
        catch (Exception ex)
        {
            // Report the failure - let the orchestrator decide what to do
            // (message may already be deleted, API error, etc.)
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} in {Chat}",
                messageId, chat.ToLogDebug(chatId));

            return DeleteResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task ScheduleUserMessagesCleanupAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        await _jobScheduler.ScheduleJobAsync(
            BackgroundJobNames.DeleteUserMessages,
            new DeleteUserMessagesPayload { TelegramUserId = userId },
            delaySeconds: SpamDetectionConstants.CleanupJobDelaySeconds,
            deduplicationKey: DeleteUserMessages(userId),
            cancellationToken);

        _logger.LogInformation("Scheduled messages cleanup job for user {UserId}", userId);
    }

    /// <inheritdoc />
    public async Task<MessageWithDetectionHistory?> GetEnrichedAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _messageQueryService.GetMessageWithDetectionHistoryAsync(messageId, cancellationToken);
    }
}
