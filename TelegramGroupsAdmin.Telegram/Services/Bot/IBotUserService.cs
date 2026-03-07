using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram user operations.
/// Orchestrates IBotUserHandler with caching (GetMe) and admin checks.
/// Application code should use this, not IBotUserHandler directly.
/// </summary>
public interface IBotUserService
{
    /// <summary>
    /// Get bot's own user info (cached for performance).
    /// The result is cached on first call and reused.
    /// </summary>
    Task<User> GetMeAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a user's membership status in a chat.
    /// </summary>
    Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Check if a user is an admin or owner in a chat.
    /// </summary>
    Task<bool> IsAdminAsync(long chatId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Get the bot's user ID (cached).
    /// Convenience method that returns GetMeAsync().Id.
    /// </summary>
    Task<long> GetBotIdAsync(CancellationToken ct = default);
}
