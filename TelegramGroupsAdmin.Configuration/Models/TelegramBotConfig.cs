namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration for Telegram bot service
/// Controls bot polling settings
/// Stored in configs table as JSONB (telegram_bot_config column) at chat_id=0 (global config)
/// Note: BotToken stored separately in telegram_bot_token_encrypted column (encrypted TEXT)
/// </summary>
/// <remarks>
/// IMPORTANT: This bot is MULTI-GROUP. It discovers and monitors ALL groups it's added to dynamically.
/// DO NOT add ChatId to this config - the bot is not limited to a single group.
/// Chat discovery happens through Telegram's MyChatMember updates when the bot is added to groups.
/// </remarks>
public class TelegramBotConfig
{
    /// <summary>
    /// Whether the Telegram bot service is enabled
    /// When false, bot stops polling for updates and becomes inactive
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
