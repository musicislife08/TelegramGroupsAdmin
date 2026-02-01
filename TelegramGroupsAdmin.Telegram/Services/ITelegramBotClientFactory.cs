using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory interface for creating and caching Telegram Bot API clients.
/// Only used by Bot Handlers layer - services and application code should use IBot*Service interfaces.
/// </summary>
public interface ITelegramBotClientFactory : IDisposable
{
    /// <summary>
    /// Get ITelegramBotClient using token loaded from database configuration.
    /// Used by Bot Handlers for direct Telegram API access.
    /// </summary>
    /// <returns>Current ITelegramBotClient instance</returns>
    Task<ITelegramBotClient> GetBotClientAsync();
}
