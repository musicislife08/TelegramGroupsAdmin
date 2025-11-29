namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Notification delivery channels
/// Values stored as integers in JSONB config
/// </summary>
public enum NotificationChannel
{
    /// <summary>Telegram Direct Message via linked account</summary>
    TelegramDm = 0,

    /// <summary>Email to user's validated account email</summary>
    Email = 1,

    /// <summary>In-app notifications (bell icon + browser push)</summary>
    WebPush = 2
}

/// <summary>
/// User's notification configuration - Channel×Event matrix
/// Each channel can independently enable/disable each event type
/// </summary>
public class NotificationConfig
{
    /// <summary>
    /// Per-channel preferences (which events are enabled for each channel)
    /// </summary>
    public List<ChannelPreference> Channels { get; set; } = [];

    /// <summary>
    /// Get or create preferences for a specific channel
    /// </summary>
    public ChannelPreference GetOrCreateChannel(NotificationChannel channel)
    {
        var pref = Channels.FirstOrDefault(c => c.Channel == channel);
        if (pref == null)
        {
            pref = new ChannelPreference { Channel = channel };
            Channels.Add(pref);
        }
        return pref;
    }

    /// <summary>
    /// Check if a specific event is enabled for a specific channel
    /// </summary>
    public bool IsEnabled(NotificationChannel channel, NotificationEventType eventType)
    {
        var pref = Channels.FirstOrDefault(c => c.Channel == channel);
        return pref?.EnabledEvents.Contains(eventType) ?? false;
    }
}

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
