using Telegram.Bot;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory interface for creating and caching Telegram Bot API clients.
/// Only used by Bot Handlers layer - services and application code should use IBot*Service interfaces.
/// The one sanctioned exception is TelegramBotPollingHost, which needs the raw client to run the polling loop.
/// </summary>
public interface ITelegramBotClientFactory : IDisposable
{
    /// <summary>
    /// Get ITelegramBotClient using token loaded from database configuration.
    /// Used by Bot Handlers for direct Telegram API access, and by TelegramBotPollingHost to start polling.
    /// </summary>
    /// <returns>Current ITelegramBotClient instance</returns>
    Task<ITelegramBotClient> GetBotClientAsync();

    /// <summary>
    /// Get ITelegramApiClient wrapper for mockable Telegram API access.
    /// This wraps the extension methods into an interface for unit testing.
    /// </summary>
    /// <returns>Current ITelegramApiClient instance</returns>
    Task<ITelegramApiClient> GetApiClientAsync();
}
