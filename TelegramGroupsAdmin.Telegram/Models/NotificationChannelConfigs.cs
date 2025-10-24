namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Root container for all notification channel configurations
/// Deserialized from notification_preferences.channel_configs JSONB column
/// </summary>
public class NotificationChannelConfigs
{
    public EmailChannelConfig? Email { get; set; }
    public TelegramChannelConfig? Telegram { get; set; }
}

/// <summary>
/// Email notification channel configuration
/// </summary>
public class EmailChannelConfig
{
    /// <summary>
    /// Email address to send notifications to
    /// If null, uses the user's account email
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Digest interval in minutes (0 = send immediately)
    /// If > 0, batch notifications and send digest at this interval
    /// </summary>
    public int DigestMinutes { get; set; }
}

/// <summary>
/// Telegram DM notification channel configuration
/// </summary>
public class TelegramChannelConfig
{
    // Currently no additional config needed - user must link account via /link command
    // Future: Could add custom message format preferences
}
