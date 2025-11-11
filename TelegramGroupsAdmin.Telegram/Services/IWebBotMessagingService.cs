using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for web UI bot messaging operations (Phase 1: Send & Edit Messages as Bot)
/// Encapsulates bot client management, feature availability, and signature logic
/// </summary>
public interface IWebBotMessagingService
{
    /// <summary>
    /// Check if the feature is available for a web user
    /// Returns success with bot user ID, or failure with reason
    /// </summary>
    /// <param name="webUserId">Web user ID from authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with availability status, bot user ID (if available), and reason (if unavailable)</returns>
    Task<WebBotFeatureAvailability> CheckFeatureAvailabilityAsync(
        string webUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a new message as bot with signature
    /// Signature format: \n\n—username (using linked Telegram username)
    /// </summary>
    /// <param name="webUserId">Web user ID (for signature and audit)</param>
    /// <param name="chatId">Target chat ID</param>
    /// <param name="text">Message text (signature will be appended)</param>
    /// <param name="replyToMessageId">Optional message ID to reply to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with success status and sent message (if successful)</returns>
    Task<WebBotMessageResult> SendMessageAsync(
        string webUserId,
        long chatId,
        string text,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit an existing bot message (no signature added/modified)
    /// </summary>
    /// <param name="webUserId">Web user ID (for audit)</param>
    /// <param name="chatId">Chat ID where message exists</param>
    /// <param name="messageId">Message ID to edit</param>
    /// <param name="text">New message text (replaces existing, no signature appended)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with success status and edited message (if successful)</returns>
    Task<WebBotMessageResult> EditMessageAsync(
        string webUserId,
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of feature availability check
/// </summary>
public record WebBotFeatureAvailability(
    bool IsAvailable,
    long? BotUserId,
    string? LinkedUsername,
    string? UnavailableReason);

/// <summary>
/// Result of send/edit message operation
/// </summary>
public record WebBotMessageResult(
    bool Success,
    Message? Message,
    string? ErrorMessage);
