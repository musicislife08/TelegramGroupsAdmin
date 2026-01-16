using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to delete all messages from a banned user across all chats
/// Phase 4.23: Cross-chat ban message cleanup
/// Respects Telegram's 48-hour deletion window and rate limits
/// </summary>
public class DeleteUserMessagesJob(
    ILogger<DeleteUserMessagesJob> logger,
    ITelegramBotClientFactory botClientFactory,
    IMessageHistoryRepository messageHistoryRepository) : IJob
{
    private readonly ILogger<DeleteUserMessagesJob> _logger = logger;
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly IMessageHistoryRepository _messageHistoryRepository = messageHistoryRepository;

    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<DeleteUserMessagesPayload>(context, _logger);
        if (payload == null) return;

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute cross-chat message cleanup for banned user
    /// Deletes all non-deleted messages from the user with rate limiting
    /// </summary>
    private async Task ExecuteAsync(DeleteUserMessagesPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "DeleteUserMessages";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation(
                "Starting cross-chat message cleanup for user {UserId}",
                payload.TelegramUserId);

            // Get operations from factory
            var operations = await _botClientFactory.GetOperationsAsync();

            // Fetch all user messages (non-deleted only)
            var userMessages = await _messageHistoryRepository.GetUserMessagesAsync(
                payload.TelegramUserId,
                cancellationToken);

            if (userMessages.Count == 0)
            {
                _logger.LogInformation(
                    "No messages found for user {UserId}, cleanup complete",
                    payload.TelegramUserId);
                success = true;
                return;
            }

            _logger.LogInformation(
                "Found {MessageCount} messages to delete for user {UserId}",
                userMessages.Count,
                payload.TelegramUserId);

            var deletedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;

            // Delete messages (Telegram.Bot handles 429 rate limiting internally)
            foreach (var message in userMessages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "Message cleanup cancelled for user {UserId} after {DeletedCount} deletions",
                        payload.TelegramUserId,
                        deletedCount);
                    break;
                }

                try
                {
                    await operations.DeleteMessageAsync(
                        chatId: message.ChatId,
                        messageId: (int)message.MessageId,
                        cancellationToken: cancellationToken);

                    deletedCount++;

                    _logger.LogDebug(
                        "Deleted message {MessageId} in chat {ChatId} for user {UserId}",
                        message.MessageId,
                        message.ChatId,
                        payload.TelegramUserId);
                }
                catch (Exception apiEx) when (
                    apiEx.Message.Contains("message to delete not found") ||
                    apiEx.Message.Contains("message can't be deleted") ||
                    apiEx.Message.Contains("MESSAGE_DELETE_FORBIDDEN") ||
                    apiEx.Message.Contains("Bad Request"))
                {
                    // Expected errors: message already deleted, too old (>48h), or bot lacks permissions
                    skippedCount++;
                    _logger.LogDebug(
                        "Skipped message {MessageId} in chat {ChatId} for user {UserId}: {Reason}",
                        message.MessageId,
                        message.ChatId,
                        payload.TelegramUserId,
                        apiEx.Message);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(
                        ex,
                        "Failed to delete message {MessageId} in chat {ChatId} for user {UserId}",
                        message.MessageId,
                        message.ChatId,
                        payload.TelegramUserId);
                    // Continue with other messages, don't throw
                }
            }

            _logger.LogInformation(
                "Cross-chat message cleanup complete for user {UserId}: {DeletedCount} deleted, {SkippedCount} skipped, {FailedCount} failed",
                payload.TelegramUserId,
                deletedCount,
                skippedCount,
                failedCount);

            success = true;
            // Don't throw - this job is best-effort (48-hour window limitation)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DeleteUserMessagesJobLogic");
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
