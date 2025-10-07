using System.Collections.Concurrent;
using Telegram.Bot;

namespace TelegramGroupsAdmin.Services.Telegram;

public class TelegramBotClientFactory
{
    private readonly ConcurrentDictionary<string, ITelegramBotClient> _clients = new();

    public ITelegramBotClient GetOrCreate(string botToken)
    {
        return _clients.GetOrAdd(botToken, token => new TelegramBotClient(token));
    }
}
