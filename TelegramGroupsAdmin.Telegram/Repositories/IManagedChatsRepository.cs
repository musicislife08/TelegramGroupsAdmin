using TelegramGroupsAdmin.Telegram.Models;

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
    Task MarkInactiveAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update last_seen_at timestamp for a chat
    /// Called when receiving messages from the chat
    /// </summary>
    Task UpdateLastSeenAsync(long chatId, DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chats (active and inactive)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAllChatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active managed chats (alias for GetActiveChatsAsync)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a managed chat record
    /// Used when Group is migrated to Supergroup (old chat ID becomes invalid)
    /// </summary>
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);
}
