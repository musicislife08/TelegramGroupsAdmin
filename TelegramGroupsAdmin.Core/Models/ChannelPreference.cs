namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Configuration for a single notification channel
/// </summary>
public class ChannelPreference
{
    /// <summary>The notification channel</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>
    /// List of event types enabled for this channel
    /// Events not in this list are disabled
    /// </summary>
    public List<NotificationEventType> EnabledEvents { get; set; } = [];

    /// <summary>
    /// Digest interval in minutes (Email channel only)
    /// 0 = send immediately, otherwise batch notifications
    /// </summary>
    public int DigestMinutes { get; set; }
}
