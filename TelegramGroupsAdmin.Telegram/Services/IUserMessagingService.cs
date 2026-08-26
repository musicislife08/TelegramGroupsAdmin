using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for sending messages to users with DM preference handling.
/// Offers a DM-first strategy with a chat-mention fallback for users still in the chat,
/// and a DM-only strategy for users who are not (e.g. someone who was just banned).
/// </summary>
public interface IUserMessagingService
{
    /// <summary>
    /// Send a message to a user, attempting DM first if enabled, falling back to chat mention.
    /// Only appropriate when the user is still in the chat and can read the mention.
    /// </summary>
    /// <param name="userId">Target user's Telegram ID</param>
    /// <param name="chat">Chat for fallback mention (required)</param>
    /// <param name="message">Pre-rendered message (text + entities) to send</param>
    /// <param name="replyToMessageId">Optional message ID to reply to in chat fallback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message was sent successfully (either DM or fallback), false if both failed</returns>
    Task<MessageSendResult> SendToUserAsync(
        long userId,
        Chat chat,
        TelegramMessage message,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message to a user by DM only, with no chat-mention fallback.
    /// Use when the user cannot read a chat mention - a banned user is gone from the chat,
    /// so a mention would only leave noise behind.
    /// </summary>
    /// <param name="userId">Target user's Telegram ID</param>
    /// <param name="message">Pre-rendered message (text + entities) to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with <see cref="MessageDeliveryMethod.PrivateDm"/> if the DM landed, otherwise failure</returns>
    Task<MessageSendResult> SendDmOnlyAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken = default);
}
