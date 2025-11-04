using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to delete all messages from a banned user across all chats
/// Phase 4.23: Cross-chat ban message cleanup
/// Respects Telegram's 48-hour deletion window and rate limits
/// </summary>
public class DeleteUserMessagesJob(
    ILogger<DeleteUserMessagesJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader,
    IMessageHistoryRepository messageHistoryRepository)
{
    private readonly ILogger<DeleteUserMessagesJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;
    private readonly IMessageHistoryRepository _messageHistoryRepository = messageHistoryRepository;

    /// <summary>
    /// Execute cross-chat message cleanup for banned user
    /// Deletes all non-deleted messages from the user with rate limiting
    /// </summary>
    [TickerFunction(functionName: "DeleteUserMessages")]
    public async Task ExecuteAsync(TickerFunctionContext<DeleteUserMessagesPayload> context, CancellationToken cancellationToken)
    {
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogError("DeleteUserMessagesJob received null payload");
            return;
        }

        _logger.LogInformation(
            "Starting cross-chat message cleanup for user {UserId}",
            payload.TelegramUserId);

        // Load bot config from database
        var (botToken, apiServerUrl) = await _configLoader.LoadConfigAsync();

        // Get bot client from factory
        var botClient = _botClientFactory.GetOrCreate(botToken, apiServerUrl);

        // Fetch all user messages (non-deleted only)
        var userMessages = await _messageHistoryRepository.GetUserMessagesAsync(
            payload.TelegramUserId,
            cancellationToken);

        if (userMessages.Count == 0)
        {
            _logger.LogInformation(
                "No messages found for user {UserId}, cleanup complete",
                payload.TelegramUserId);
            return;
        }

        _logger.LogInformation(
            "Found {MessageCount} messages to delete for user {UserId}",
            userMessages.Count,
            payload.TelegramUserId);

        var deletedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        // Delete messages with rate limiting (~10 deletions/second)
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
                await botClient.DeleteMessage(
                    chatId: message.ChatId,
                    messageId: (int)message.MessageId,
                    cancellationToken: cancellationToken);

                deletedCount++;

                _logger.LogDebug(
                    "Deleted message {MessageId} in chat {ChatId} for user {UserId}",
                    message.MessageId,
                    message.ChatId,
                    payload.TelegramUserId);

                // Rate limiting: ~10 deletions/second
                await Task.Delay(100, cancellationToken);
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

        // Don't throw - this job is best-effort (48-hour window limitation)
    }
}
