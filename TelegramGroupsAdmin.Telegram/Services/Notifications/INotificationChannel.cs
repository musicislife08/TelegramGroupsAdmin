namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Represents a notification delivery channel (Telegram DM, email, push, etc.)
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Unique identifier for this channel (e.g., "telegram-dm", "email", "push")
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Sends a notification through this channel
    /// </summary>
    /// <param name="recipient">Channel-specific recipient identifier (Telegram user ID, email address, device token, etc.)</param>
    /// <param name="notification">The notification to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with optional error message</returns>
    Task<DeliveryResult> SendAsync(
        string recipient,
        Notification notification,
        CancellationToken cancellationToken = default);
}
