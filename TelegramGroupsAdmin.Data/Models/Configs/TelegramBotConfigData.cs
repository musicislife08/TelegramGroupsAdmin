namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of TelegramBotConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class TelegramBotConfigData
{
    /// <summary>
    /// Whether the Telegram bot service is enabled
    /// </summary>
    public bool BotEnabled { get; set; }
}
