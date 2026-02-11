using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

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
    Task<WebBotFeatureAvailability> CheckFeatureAvailabilityAsync(
        WebUserIdentity webUser,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a new message as bot with signature
    /// Signature format: \n\n—username (using linked Telegram username)
    /// </summary>
    Task<WebBotMessageResult> SendMessageAsync(
        WebUserIdentity webUser,
        long chatId,
        string text,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit an existing bot message (no signature added/modified)
    /// </summary>
    Task<WebBotMessageResult> EditMessageAsync(
        WebUserIdentity webUser,
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
