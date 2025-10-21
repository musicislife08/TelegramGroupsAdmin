namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Orchestrates notification delivery across multiple channels (Telegram DM, email, push, etc.)
/// </summary>
public interface INotificationOrchestrator
{
    /// <summary>
    /// Send a notification through a specific channel
    /// </summary>
    /// <param name="channelName">Channel identifier (e.g., "telegram-dm", "email")</param>
    /// <param name="recipient">Channel-specific recipient identifier (user ID, email address, device token, etc.)</param>
    /// <param name="notification">The notification to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with optional error message</returns>
    Task<DeliveryResult> SendAsync(
        string channelName,
        string recipient,
        Notification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a Telegram DM notification (convenience method)
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID</param>
    /// <param name="notification">The notification to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with optional error message</returns>
    Task<DeliveryResult> SendTelegramDmAsync(
        long telegramUserId,
        Notification notification,
        CancellationToken cancellationToken = default);
}
