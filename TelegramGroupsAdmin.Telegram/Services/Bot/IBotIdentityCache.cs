using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Pure state storage for bot identity.
/// Singleton cache that stores the bot's User info from GetMe API call.
/// Prevents redundant GetMe API calls across scoped services.
/// </summary>
public interface IBotIdentityCache
{
    /// <summary>
    /// Get the cached bot user (null if not yet fetched).
    /// </summary>
    User? GetCachedBotUser();

    /// <summary>
    /// Cache the bot user after a GetMe API call.
    /// </summary>
    void SetBotUser(User user);

    /// <summary>
    /// Get the cached bot ID (null if not yet fetched).
    /// Convenience method that returns GetCachedBotUser()?.Id.
    /// </summary>
    long? GetBotId();
}
