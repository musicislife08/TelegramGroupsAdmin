using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram chat operations.
/// Orchestrates IBotChatHandler with caching and database integration.
/// Application code should use this, not IBotChatHandler directly.
/// </summary>
public interface IBotChatService
{
    /// <summary>Get information about a chat.</summary>
    Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Get administrators of a chat with caching.
    /// Uses chat_admins table for caching, refreshes from Telegram API when needed.
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="forceRefresh">Force refresh from Telegram API (bypasses cache)</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<ChatMember>> GetAdministratorsAsync(long chatId, bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>
    /// Get invite link for a chat (from cache or Telegram API).
    /// For public groups, returns t.me/username.
    /// For private groups, returns cached link or exports primary link on first call.
    /// </summary>
    Task<string?> GetInviteLinkAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Refresh invite link from Telegram API and update cache.
    /// For public groups, checks if username changed.
    /// For private groups, returns cached link (doesn't revoke).
    /// </summary>
    Task<string?> RefreshInviteLinkAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Leave a chat (group, supergroup, or channel).
    /// </summary>
    Task LeaveChatAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Check if bot has required permissions in a chat.
    /// </summary>
    Task<bool> CheckHealthAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Get all active managed chats where the bot has confirmed permissions.
    /// Used by moderation services for cross-chat operations.
    /// </summary>
    IReadOnlyList<long> GetHealthyChatIds();

    /// <summary>
    /// Handle MyChatMember updates (bot added/removed, admin promotion/demotion).
    /// Only tracks groups/supergroups - private chats are not managed.
    /// </summary>
    Task HandleBotMembershipUpdateAsync(ChatMemberUpdated myChatMember, CancellationToken ct = default);

    /// <summary>
    /// Handle ChatMember updates for admin promotion/demotion (instant permission updates).
    /// Called when any user (not just bot) is promoted/demoted in a managed chat.
    /// </summary>
    Task HandleAdminStatusChangeAsync(ChatMemberUpdated chatMemberUpdate, CancellationToken ct = default);

    /// <summary>
    /// Handle Group to Supergroup migration.
    /// When a Group is upgraded to Supergroup, Telegram creates a new chat ID.
    /// </summary>
    Task HandleChatMigrationAsync(long oldChatId, long newChatId, CancellationToken ct = default);
}
