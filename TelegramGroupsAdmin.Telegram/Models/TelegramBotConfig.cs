namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Configuration for Telegram bot service
/// Controls whether the bot polling service is active
/// Stored in configs table as JSONB
/// </summary>
public class TelegramBotConfig
{
    /// <summary>
    /// Whether the Telegram bot service is enabled
    /// When false, bot stops polling for updates and becomes inactive
    /// Requires app restart if changed (for now - will be made dynamic)
    /// Default: false (users must explicitly enable after configuring bot token)
    /// </summary>
    public bool BotEnabled { get; set; }

    /// <summary>
    /// Default configuration (disabled by default - users enable after setup)
    /// </summary>
    public static TelegramBotConfig Default => new()
    {
        BotEnabled = false
    };
}
