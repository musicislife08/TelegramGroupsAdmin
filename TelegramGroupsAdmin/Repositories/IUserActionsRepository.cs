using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing user moderation actions (bans, warns, mutes, trusts)
/// </summary>
public interface IUserActionsRepository
{
    /// <summary>
    /// Insert a new user action (ban, warn, mute, trust, unban)
    /// </summary>
    Task<long> InsertAsync(UserActionRecord action);

    /// <summary>
    /// Get user action by ID
    /// </summary>
    Task<UserActionRecord?> GetByIdAsync(long id);

    /// <summary>
    /// Get all actions for a specific user
    /// </summary>
    Task<List<UserActionRecord>> GetByUserIdAsync(long userId);

    /// <summary>
    /// Get active actions for a user (not expired)
    /// </summary>
    Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId);

    /// <summary>
    /// Get active actions for a user filtered by action type (not expired)
    /// </summary>
    Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType);

    /// <summary>
    /// Deactivate an action by ID (soft delete)
    /// </summary>
    Task DeactivateAsync(long actionId);

    /// <summary>
    /// Get all active bans across all users
    /// </summary>
    Task<List<UserActionRecord>> GetActiveBansAsync();

    /// <summary>
    /// Check if user is banned in a specific chat or globally
    /// </summary>
    Task<bool> IsUserBannedAsync(long userId, long? chatId = null);

    /// <summary>
    /// Check if user is trusted (bypasses spam detection)
    /// </summary>
    Task<bool> IsUserTrustedAsync(long userId, long? chatId = null);

    /// <summary>
    /// Get warn count for user in specific chat or globally
    /// </summary>
    Task<int> GetWarnCountAsync(long userId, long? chatId = null);

    /// <summary>
    /// Remove (soft delete) an action by setting expires_at to now
    /// Used for unban, removing trust, etc.
    /// </summary>
    Task ExpireActionAsync(long actionId);

    /// <summary>
    /// Remove all active bans for a user (used for unban command)
    /// </summary>
    Task ExpireBansForUserAsync(long userId, long? chatId = null);

    /// <summary>
    /// Remove all active trusts for a user
    /// </summary>
    Task ExpireTrustsForUserAsync(long userId, long? chatId = null);

    /// <summary>
    /// Get recent actions with limit
    /// </summary>
    Task<List<UserActionRecord>> GetRecentAsync(int limit = 100);

    /// <summary>
    /// Get all actions for a specific chat
    /// </summary>
    Task<List<UserActionRecord>> GetByChatIdAsync(long chatId);

    /// <summary>
    /// Delete actions older than specified timestamp
    /// </summary>
    Task<int> DeleteOlderThanAsync(long timestamp);
}
