using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for moderation message operations.
/// Owns backfill (ensuring messages exist in DB) and deletion.
/// Named BotModerationMessageHandler to avoid conflict with BotMessageHandler (send/edit).
/// </summary>
public class BotModerationMessageHandler : IBotModerationMessageHandler
{
    private readonly IMessageHistoryRepository _messageHistoryRepository;
    private readonly IMessageQueryService _messageQueryService;
    private readonly IMessageBackfillService _messageBackfillService;
    private readonly IBotMessageService _botMessageService;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<BotModerationMessageHandler> _logger;

    public BotModerationMessageHandler(
        IMessageHistoryRepository messageHistoryRepository,
        IMessageQueryService messageQueryService,
        IMessageBackfillService messageBackfillService,
        IBotMessageService botMessageService,
        IJobScheduler jobScheduler,
        ILogger<BotModerationMessageHandler> logger)
    {
        _messageHistoryRepository = messageHistoryRepository;
        _messageQueryService = messageQueryService;
        _messageBackfillService = messageBackfillService;
        _botMessageService = botMessageService;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackfillResult> EnsureExistsAsync(
        long messageId,
        ChatIdentity chat,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Ensuring message {MessageId} exists in database for {Chat}",
            messageId, chat.ToLogDebug());

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
                messageId, chat.Id, telegramMessage, cancellationToken);

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
        ChatIdentity chat,
        long messageId,
        Actor executor,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Deleting message {MessageId} from {Chat} by {Executor}",
            messageId, chat.ToLogDebug(), executor.GetDisplayText());

        try
        {
            await _botMessageService.DeleteAndMarkMessageAsync(
                chat.Id,
                (int)messageId,
                deletionSource: "moderation_action",
                cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} from {Chat}",
                messageId, chat.ToLogInfo());

            return DeleteResult.Succeeded(messageDeleted: true);
        }
        catch (Exception ex)
        {
            // Report the failure - let the orchestrator decide what to do
            // (message may already be deleted, API error, etc.)
            _logger.LogWarning(ex,
                "Failed to delete message {MessageId} in {Chat}",
                messageId, chat.ToLogDebug());

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
