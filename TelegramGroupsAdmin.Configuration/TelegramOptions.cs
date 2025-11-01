namespace TelegramGroupsAdmin.Configuration;

public sealed class TelegramOptions
{
    public required string BotToken { get; set; }
    public required string ChatId { get; set; }

    /// <summary>
    /// The bot's Telegram user ID, populated on startup via GetMe() API call.
    /// Used to filter bot messages from user statistics queries.
    /// </summary>
    public long BotUserId { get; set; }
}
