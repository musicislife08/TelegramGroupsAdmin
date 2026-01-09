using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for sending messages to users with DM preference handling and fallback.
/// Attempts private DM first (if user enabled), falls back to chat mentions on failure.
/// </summary>
public interface IUserMessagingService
{
    /// <summary>
    /// Send a message to a user, attempting DM first if enabled, falling back to chat mention.
    /// </summary>
    /// <param name="userId">Target user's Telegram ID</param>
    /// <param name="chat">Chat for fallback mention (required)</param>
    /// <param name="messageText">Message to send</param>
    /// <param name="replyToMessageId">Optional message ID to reply to in chat fallback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message was sent successfully (either DM or fallback), false if both failed</returns>
    Task<MessageSendResult> SendToUserAsync(
        long userId,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to multiple users (e.g., all admins in a chat).
    /// Each user gets DM if available, otherwise fallback to single chat mention.
    /// </summary>
    Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        List<long> userIds,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default);
}
