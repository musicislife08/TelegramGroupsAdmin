using System.Collections.Concurrent;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Telegram.Abstractions.Services;

public class TelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, ITelegramBotClient> _clients = new();

    public ITelegramBotClient GetOrCreate(string botToken)
    {
        // Fast path: TryGetValue is lock-free and allocation-free (99.9% cache hit rate)
        if (_clients.TryGetValue(botToken, out var existingClient))
            return existingClient;

        // Slow path: Only called once per bot token (first call only)
        return _clients.GetOrAdd(botToken, static token => new TelegramBotClient(token));
    }
}
