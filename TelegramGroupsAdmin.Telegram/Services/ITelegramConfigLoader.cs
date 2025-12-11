namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Interface for loading Telegram bot configuration from database.
/// Enables unit testing by allowing mock implementations.
/// </summary>
public interface ITelegramConfigLoader
{
    /// <summary>
    /// Load Telegram bot token from database (global config, chat_id=0)
    /// </summary>
    /// <returns>Bot token string</returns>
    /// <exception cref="InvalidOperationException">Thrown if bot token not configured</exception>
    Task<string> LoadConfigAsync();
}
