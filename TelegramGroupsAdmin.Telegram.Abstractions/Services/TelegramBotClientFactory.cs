using System.Collections.Concurrent;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Abstractions.Services;

/// <summary>
/// Factory for creating and caching Telegram Bot API clients
/// Uses standard api.telegram.org endpoint with 20MB file download limit
/// </summary>
public class TelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, ITelegramBotClient> _clients = new();

    /// <summary>
    /// Get or create a Telegram bot client using standard api.telegram.org endpoint
    /// </summary>
    /// <param name="botToken">Bot token from BotFather</param>
    /// <returns>Cached or newly created ITelegramBotClient instance</returns>
    public ITelegramBotClient GetOrCreate(string botToken)
    {
        // Fast path: TryGetValue is lock-free and allocation-free (99.9% cache hit rate)
        if (_clients.TryGetValue(botToken, out var existingClient))
            return existingClient;

        // Slow path: Only called once per unique token (first call only)
        return _clients.GetOrAdd(botToken, _ => new TelegramBotClient(botToken));
    }
}
