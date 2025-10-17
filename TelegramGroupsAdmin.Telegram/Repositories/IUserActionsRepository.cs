using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing user moderation actions (bans, warns, mutes, trusts)
/// </summary>
public interface IUserActionsRepository
{
    /// <summary>
    /// Insert a new user action (ban, warn, mute, trust, unban)
    /// </summary>
    Task<long> InsertAsync(UserActionRecord action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user action by ID
    /// </summary>
    Task<UserActionRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all actions for a specific user
    /// </summary>
    Task<List<UserActionRecord>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active actions for a user (not expired)
    /// </summary>
    Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active actions for a user filtered by action type (not expired)
    /// </summary>
    Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivate an action by ID (soft delete)
    /// </summary>
    Task DeactivateAsync(long actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active bans across all users
    /// </summary>
    Task<List<UserActionRecord>> GetActiveBansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is banned in a specific chat or globally
    /// </summary>
    Task<bool> IsUserBannedAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is trusted (bypasses spam detection)
    /// </summary>
    Task<bool> IsUserTrustedAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get warn count for user in specific chat or globally
    /// </summary>
    Task<int> GetWarnCountAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (soft delete) an action by setting expires_at to now
    /// Used for unban, removing trust, etc.
    /// </summary>
    Task ExpireActionAsync(long actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all active bans for a user (used for unban command)
    /// </summary>
    Task ExpireBansForUserAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all active trusts for a user
    /// </summary>
    Task ExpireTrustsForUserAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent actions with limit
    /// </summary>
    Task<List<UserActionRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete actions older than specified timestamp
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default);
}
