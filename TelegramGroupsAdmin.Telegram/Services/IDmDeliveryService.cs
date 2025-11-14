namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Used by: NotificationSystem, WelcomeService, and any other feature that needs DM delivery.
/// </summary>
public interface IDmDeliveryService
{
    /// <summary>
    /// Attempt to send a DM to a user. Updates bot_dm_enabled flag automatically.
    /// If DM fails and fallbackChatId is provided, posts message in chat with optional auto-delete.
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID to send DM to</param>
    /// <param name="messageText">Message text to send</param>
    /// <param name="fallbackChatId">Optional chat ID to post fallback message if DM fails (403)</param>
    /// <param name="autoDeleteSeconds">Optional seconds to auto-delete fallback message (uses Quartz.NET)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success, fallback usage, or failure</returns>
    Task<DmDeliveryResult> SendDmAsync(
        long telegramUserId,
        string messageText,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to send a DM to a user. If DM fails (403), queues notification for later delivery.
    /// Updates bot_dm_enabled flag automatically.
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID to send DM to</param>
    /// <param name="notificationType">Type of notification (e.g., "warning", "mystatus")</param>
    /// <param name="messageText">Message text to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure (queued notifications are considered failures)</returns>
    Task<DmDeliveryResult> SendDmWithQueueAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to send a DM with optional media (photo or video) to a user.
    /// If DM fails (403), queues notification for later delivery.
    /// Updates bot_dm_enabled flag automatically.
    /// Phase 5.2: Enhanced spam notifications with media support
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID to send DM to</param>
    /// <param name="notificationType">Type of notification (e.g., "spam_banned")</param>
    /// <param name="messageText">Message text to send (or caption if media present)</param>
    /// <param name="photoPath">Optional local path to photo file</param>
    /// <param name="videoPath">Optional local path to video file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure (queued notifications are considered failures)</returns>
    Task<DmDeliveryResult> SendDmWithMediaAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        string? videoPath = null,
        CancellationToken cancellationToken = default);
}
