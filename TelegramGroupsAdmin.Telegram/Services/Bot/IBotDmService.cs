namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Used by: NotificationSystem, WelcomeService, and any other feature that needs DM delivery.
/// This service is in the Bot layer and can use IBotMessageHandler directly.
/// </summary>
public interface IBotDmService
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

    /// <summary>
    /// Attempt to send a DM with optional media and inline keyboard buttons.
    /// If DM fails (403), queues notification for later delivery (without buttons).
    /// Updates bot_dm_enabled flag automatically.
    /// Phase X: Report moderation DM support with action buttons
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID to send DM to</param>
    /// <param name="notificationType">Type of notification (e.g., "report")</param>
    /// <param name="messageText">Message text to send (or caption if media present)</param>
    /// <param name="photoPath">Optional local path to photo file</param>
    /// <param name="keyboard">Optional inline keyboard markup with action buttons</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<DmDeliveryResult> SendDmWithMediaAndKeyboardAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? keyboard = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a DM text message with optional inline keyboard change.
    /// Used for updating review notification DMs after admin action (removes buttons, shows result).
    /// </summary>
    Task<global::Telegram.Bot.Types.Message> EditDmTextAsync(
        long dmChatId,
        int messageId,
        string text,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a DM media message caption with optional inline keyboard change.
    /// Used for updating review notification DMs with photos/videos after admin action.
    /// </summary>
    Task<global::Telegram.Bot.Types.Message> EditDmCaptionAsync(
        long dmChatId,
        int messageId,
        string? caption,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from a DM conversation.
    /// Used for cleaning up exam question messages after user answers.
    /// </summary>
    Task DeleteDmMessageAsync(
        long dmChatId,
        int messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a DM with an inline keyboard to a user.
    /// Does NOT queue on failure (keyboards can't be queued).
    /// Used for exam questions with answer buttons.
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID to send DM to</param>
    /// <param name="messageText">Message text to send</param>
    /// <param name="keyboard">Inline keyboard markup with buttons</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure, with MessageId if successful</returns>
    Task<DmDeliveryResult> SendDmWithKeyboardAsync(
        long telegramUserId,
        string messageText,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken = default);
}
