using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing chats where the bot has been added
/// </summary>
public interface IManagedChatsRepository
{
    /// <summary>
    /// Upsert (insert or update) a managed chat record
    /// Used when bot joins/leaves chats or status changes
    /// </summary>
    Task UpsertAsync(ManagedChatRecord chat);

    /// <summary>
    /// Get managed chat by chat ID
    /// </summary>
    Task<ManagedChatRecord?> GetByChatIdAsync(long chatId);

    /// <summary>
    /// Get all active chats (is_active = true)
    /// </summary>
    Task<List<ManagedChatRecord>> GetActiveChatsAsync();

    /// <summary>
    /// Get all chats where bot is admin (is_admin = true AND is_active = true)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAdminChatsAsync();

    /// <summary>
    /// Check if chat is active and bot has admin permissions
    /// Used for command validation
    /// </summary>
    Task<bool> IsActiveAndAdminAsync(long chatId);

    /// <summary>
    /// Mark chat as inactive (bot was removed/kicked)
    /// Soft delete - preserves settings for if bot rejoins
    /// </summary>
    Task MarkInactiveAsync(long chatId);

    /// <summary>
    /// Update last_seen_at timestamp for a chat
    /// Called when receiving messages from the chat
    /// </summary>
    Task UpdateLastSeenAsync(long chatId, DateTimeOffset timestamp);

    /// <summary>
    /// Get all chats (active and inactive)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAllChatsAsync();

    /// <summary>
    /// Get all active managed chats (alias for GetActiveChatsAsync)
    /// </summary>
    Task<List<ManagedChatRecord>> GetAllAsync();
}
