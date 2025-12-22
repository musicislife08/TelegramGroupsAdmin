using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory interface for creating and caching Telegram Bot API clients.
/// Enables unit testing by allowing mock implementations.
/// </summary>
public interface ITelegramBotClientFactory : IDisposable
{
    /// <summary>
    /// Get ITelegramBotClient using token loaded from database configuration.
    /// Used by TelegramBotPollingHost for polling infrastructure (ReceiveAsync).
    /// </summary>
    /// <returns>Current ITelegramBotClient instance</returns>
    Task<ITelegramBotClient> GetBotClientAsync();

    /// <summary>
    /// Get ITelegramOperations using token loaded from database configuration.
    /// Returns mockable wrapper around the current ITelegramBotClient.
    /// </summary>
    /// <returns>Current ITelegramOperations instance</returns>
    /// <remarks>
    /// This is the recommended method for services that need Telegram API access.
    /// ITelegramOperations can be mocked with NSubstitute for unit testing.
    /// </remarks>
    Task<ITelegramOperations> GetOperationsAsync();
}
