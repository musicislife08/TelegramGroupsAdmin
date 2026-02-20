namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration for Telegram User API (WTelegram/MTProto)
/// Stores non-sensitive settings. API Hash stored separately in encrypted column.
/// Stored in configs table as JSONB (user_api_config column) at chat_id=0 (global config)
/// </summary>
public record UserApiConfig
{
    /// <summary>
    /// Telegram API ID from my.telegram.org
    /// Required together with API Hash before any WTelegram features can be used
    /// </summary>
    public int ApiId { get; init; }

    /// <summary>
    /// Default configuration (no API ID configured)
    /// </summary>
    public static readonly UserApiConfig Default = new();
}
