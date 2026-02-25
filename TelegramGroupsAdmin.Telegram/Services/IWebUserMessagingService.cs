using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for web UI user messaging operations (Send & Edit Messages as Admin's personal Telegram account).
/// Uses WTelegram user API — messages appear from the admin's real account, not the bot.
/// The bot will see these messages as normal incoming and update the UI automatically.
/// </summary>
public interface IWebUserMessagingService
{
    /// <summary>
    /// Check if the user API messaging feature is available for a web user.
    /// Requires an active WTelegram session.
    /// </summary>
    Task<WebUserFeatureAvailability> CheckFeatureAvailabilityAsync(
        WebUserIdentity webUser,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the admin can send to a specific chat (i.e., they are a member of that chat
    /// via their personal Telegram account). Uses the peer cache from WarmPeerCacheAsync.
    /// </summary>
    Task<WebUserChatAvailability> CanSendToChatAsync(
        WebUserIdentity webUser,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message as the admin's personal Telegram account.
    /// No signature is appended — the message appears natively from their account.
    /// </summary>
    Task<WebUserMessageResult> SendMessageAsync(
        WebUserIdentity webUser,
        long chatId,
        string text,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a message previously sent by the admin's personal account.
    /// </summary>
    Task<WebUserMessageResult> EditMessageAsync(
        WebUserIdentity webUser,
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default);
}

public record WebUserFeatureAvailability(bool IsAvailable, string? UnavailableReason);

public record WebUserChatAvailability(bool CanSend, string? UnavailableReason);

public record WebUserMessageResult(bool Success, string? ErrorMessage);
