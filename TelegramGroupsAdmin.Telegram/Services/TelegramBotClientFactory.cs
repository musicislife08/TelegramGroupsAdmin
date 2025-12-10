using System.Collections.Concurrent;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Factory for creating and caching Telegram Bot API clients.
/// Uses standard api.telegram.org endpoint with 20MB file download limit.
///
/// Two usage patterns:
/// - GetBotClientAsync(): Loads token from database config (recommended for most services)
/// - GetOrCreate(token): Direct token injection (used by TelegramAdminBotService which caches token)
/// </summary>
public class TelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, ITelegramBotClient> _clients = new();
    private readonly TelegramConfigLoader _configLoader;

    public TelegramBotClientFactory(TelegramConfigLoader configLoader)
    {
        _configLoader = configLoader;
    }

    /// <summary>
    /// Get or create a bot client using token loaded from database configuration.
    /// This is the recommended method for most services - eliminates need to inject TelegramConfigLoader separately.
    /// </summary>
    /// <returns>Cached or newly created ITelegramBotClient instance</returns>
    public virtual async Task<ITelegramBotClient> GetBotClientAsync()
    {
        var botToken = await _configLoader.LoadConfigAsync();
        return GetOrCreate(botToken);
    }

    /// <summary>
    /// Get or create a Telegram bot client using provided token.
    /// Use this when you already have the token (e.g., TelegramAdminBotService caches it).
    /// </summary>
    /// <param name="botToken">Bot token from BotFather</param>
    /// <returns>Cached or newly created ITelegramBotClient instance</returns>
    public virtual ITelegramBotClient GetOrCreate(string botToken)
    {
        // Fast path: TryGetValue is lock-free and allocation-free (99.9% cache hit rate)
        if (_clients.TryGetValue(botToken, out var existingClient))
            return existingClient;

        // Slow path: Only called once per unique token (first call only)
        return _clients.GetOrAdd(botToken, _ => new TelegramBotClient(botToken));
    }
}
