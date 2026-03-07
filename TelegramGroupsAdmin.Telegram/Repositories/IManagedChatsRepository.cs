using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing chats where the bot has been added
/// </summary>
public interface IManagedChatsRepository
{
    /// <summary>
    /// Upsert (insert or update) a managed chat record
    /// Used when bot joins/leaves chats or status changes
    /// </summary>
    Task UpsertAsync(ManagedChatRecord chat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get managed chat by chat ID
    /// </summary>
    Task<ManagedChatRecord?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple managed chats by their chat IDs in a single query.
    /// Used for batch hydration to avoid N+1 query patterns.
    /// </summary>
    Task<List<ManagedChatRecord>> GetByChatIdsAsync(IEnumerable<long> chatIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active chats (is_active = true)
    /// </summary>
    Task<List<ManagedChatRecord>> GetActiveChatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chats where bot is admin (is_admin = true AND is_active = true)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAdminChatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if chat is active and bot has admin permissions
    /// Used for command validation
    /// </summary>
    Task<bool> IsActiveAndAdminAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark chat as inactive (bot was removed/kicked)
    /// Soft delete - preserves settings for if bot rejoins
    /// </summary>
    Task MarkInactiveAsync(ChatIdentity chat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update last_seen_at timestamp for a chat
    /// Called when receiving messages from the chat
    /// </summary>
    Task UpdateLastSeenAsync(long chatId, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chats (active and inactive)
    /// </summary>
    /// <param name="includeDeleted">Include soft-deleted chats (default: false)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<ManagedChatRecord>> GetAllChatsAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active managed chats (alias for GetActiveChatsAsync)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a managed chat record
    /// Used when Group is migrated to Supergroup (old chat ID becomes invalid)
    /// </summary>
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get chats accessible to a specific user based on their permission level
    /// - Admin (0): Only active chats where user's linked Telegram account is admin (via chat_admins table)
    /// - GlobalAdmin (1) / Owner (2): All active chats
    /// </summary>
    /// <param name="userId">Web app user ID</param>
    /// <param name="permissionLevel">User's permission level</param>
    /// <param name="includeDeleted">Include soft-deleted chats (default: false)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of accessible chats, empty if Admin user has no linked Telegram account</returns>
    Task<List<ManagedChatRecord>> GetUserAccessibleChatsAsync(
        string userId,
        PermissionLevel permissionLevel,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);
}
