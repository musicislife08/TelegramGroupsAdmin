using System.Collections.Concurrent;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Metrics;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Thread-safe in-memory cache for SDK Chat objects.
/// Singleton service - populated by health checks (startup + periodic) and message processing.
/// </summary>
public class ChatCache(CacheMetrics cacheMetrics) : IChatCache
{
    private readonly ConcurrentDictionary<long, Chat> _cache = new();

    /// <inheritdoc />
    public Chat? GetChat(long chatId)
    {
        var chat = _cache.GetValueOrDefault(chatId);
        if (chat != null)
            cacheMetrics.RecordHit("chat");
        else
            cacheMetrics.RecordMiss("chat");
        return chat;
    }

    /// <inheritdoc />
    public void UpdateChat(Chat chat)
        => _cache[chat.Id] = chat;

    /// <inheritdoc />
    public void RemoveChat(long chatId)
    {
        if (_cache.TryRemove(chatId, out _))
            cacheMetrics.RecordRemoval("chat");
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Chat> GetAllChats()
        => _cache.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public int Count => _cache.Count;
}
