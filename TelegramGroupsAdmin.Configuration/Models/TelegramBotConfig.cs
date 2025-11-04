namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration for Telegram bot service
/// Controls bot polling, chat configuration, and API server settings
/// Stored in configs table as JSONB (telegram_bot_config column)
/// Note: BotToken stored separately in telegram_bot_token_encrypted column (encrypted TEXT)
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
    /// Telegram chat ID where the bot operates (typically a group chat)
    /// Must be negative for group chats (e.g., -1001234567890)
    /// Migrated from TELEGRAM__CHATID env var to database
    /// Required for bot operation (null = not configured)
    /// </summary>
    public long? ChatId { get; set; }

    /// <summary>
    /// Optional custom Bot API server URL for self-hosted mode
    /// When null/empty: Uses standard api.telegram.org (20MB file download limit)
    /// When set: Uses custom server (e.g., http://bot-api-server:8081) with unlimited downloads
    /// Requires self-hosted telegram-bot-api container with --local flag
    /// Migrated from TELEGRAM__APISERVERURL env var to database
    /// </summary>
    public string? ApiServerUrl { get; set; }

    /// <summary>
    /// Default configuration (disabled by default - users enable after setup)
    /// </summary>
    public static TelegramBotConfig Default => new()
    {
        BotEnabled = false,
        ChatId = null,
        ApiServerUrl = null
    };
}
