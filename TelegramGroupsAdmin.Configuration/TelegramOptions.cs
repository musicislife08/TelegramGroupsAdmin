namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Telegram bot configuration options
/// NOTE: This class is no longer actively used. Configuration is loaded from database via TelegramConfigLoader.
/// Kept only to prevent breaking changes during transition period.
/// </summary>
public sealed class TelegramOptions
{
    public required string BotToken { get; set; }
    public required string ChatId { get; set; }
    public long BotUserId { get; set; }
}
