using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Pure state storage for bot identity.
/// Singleton cache that stores the bot's User info from GetMe API call.
/// Thread-safe via volatile read/write for single reference assignment.
/// </summary>
public class BotIdentityCache : IBotIdentityCache
{
    private volatile User? _cachedBotUser;

    public User? GetCachedBotUser() => _cachedBotUser;

    public void SetBotUser(User user) => _cachedBotUser = user;

    public long? GetBotId() => _cachedBotUser?.Id;
}
