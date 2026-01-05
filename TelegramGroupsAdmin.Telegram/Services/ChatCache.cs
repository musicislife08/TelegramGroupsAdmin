using System.Collections.Concurrent;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Thread-safe in-memory cache for SDK Chat objects.
/// Singleton service - populated by health checks (startup + periodic) and message processing.
/// </summary>
public class ChatCache : IChatCache
{
    private readonly ConcurrentDictionary<long, Chat> _cache = new();

    /// <inheritdoc />
    public Chat? GetChat(long chatId)
        => _cache.GetValueOrDefault(chatId);

    /// <inheritdoc />
    public void UpdateChat(Chat chat)
        => _cache[chat.Id] = chat;

    /// <inheritdoc />
    public void RemoveChat(long chatId)
        => _cache.TryRemove(chatId, out _);

    /// <inheritdoc />
    public IReadOnlyCollection<Chat> GetAllChats()
        => _cache.Values.ToList().AsReadOnly();
}
