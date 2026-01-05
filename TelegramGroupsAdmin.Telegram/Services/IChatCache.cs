using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// In-memory cache for SDK Chat objects.
/// Populated by health checks (on startup and periodically) and message processing.
/// Used by services that need SDK Chat but only have chatId.
/// </summary>
public interface IChatCache
{
    /// <summary>
    /// Get cached SDK Chat by ID.
    /// Returns null if not in cache (caller should handle gracefully).
    /// </summary>
    Chat? GetChat(long chatId);

    /// <summary>
    /// Add or update a chat in the cache.
    /// Called by health checks and message processing.
    /// </summary>
    void UpdateChat(Chat chat);

    /// <summary>
    /// Remove a chat from the cache.
    /// Called when bot is removed from a chat.
    /// </summary>
    void RemoveChat(long chatId);

    /// <summary>
    /// Get all cached chats.
    /// </summary>
    IReadOnlyCollection<Chat> GetAllChats();
}
