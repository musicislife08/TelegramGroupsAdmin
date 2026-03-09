namespace TelegramGroupsAdmin.Core.Models;

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
