using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Services.Bot;
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
    IBotMessageService messageService,
    IMessageHistoryRepository messageHistoryRepository) : IJob
{
    private readonly ILogger<DeleteUserMessagesJob> _logger = logger;
    private readonly IBotMessageService _messageService = messageService;
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
        var user = payload.User;

        try
        {
            _logger.LogInformation(
                "Starting cross-chat message cleanup for user {UserDisplay} ({UserId})",
                user.DisplayName, user.Id);

            // Fetch all user messages (non-deleted only)
            var userMessages = await _messageHistoryRepository.GetUserMessagesAsync(
                user.Id,
                cancellationToken);

            if (userMessages.Count == 0)
            {
                _logger.LogInformation(
                    "No messages found for user {UserDisplay} ({UserId}), cleanup complete",
                    user.DisplayName, user.Id);
                success = true;
                return;
            }

            _logger.LogInformation(
                "Found {MessageCount} messages to delete for user {UserDisplay} ({UserId})",
                userMessages.Count,
                user.DisplayName, user.Id);

            var deletedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;

            // Delete messages (Telegram.Bot handles 429 rate limiting internally)
            foreach (var message in userMessages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "Message cleanup cancelled for user {UserDisplay} ({UserId}) after {DeletedCount} deletions",
                        user.DisplayName, user.Id,
                        deletedCount);
                    break;
                }

                try
                {
                    await _messageService.DeleteAndMarkMessageAsync(
                        chatId: message.ChatId,
                        messageId: (int)message.MessageId,
                        deletionSource: "ban_cleanup",
                        cancellationToken: cancellationToken);

                    deletedCount++;

                    _logger.LogDebug(
                        "Deleted message {MessageId} in chat {ChatId} for user {UserId}",
                        message.MessageId,
                        message.ChatId,
                        user.Id);
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
                        user.Id,
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
                        user.Id);
                    // Continue with other messages, don't throw
                }
            }

            _logger.LogInformation(
                "Cross-chat message cleanup complete for user {UserDisplay} ({UserId}): {DeletedCount} deleted, {SkippedCount} skipped, {FailedCount} failed",
                user.DisplayName, user.Id,
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
