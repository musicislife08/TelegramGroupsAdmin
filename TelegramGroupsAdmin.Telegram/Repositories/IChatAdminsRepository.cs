using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing Telegram admin status per chat
/// Used for permission caching to avoid API calls on every command
/// </summary>
public interface IChatAdminsRepository
{
    /// <summary>
    /// Get admin permission level for a specific user in a specific chat
    /// Returns 2 if creator, 1 if admin, -1 if not admin/not found
    /// </summary>
    Task<int> GetPermissionLevelAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is an active admin in the specified chat
    /// </summary>
    Task<bool> IsAdminAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active admins for a specific chat
    /// </summary>
    Task<List<ChatAdmin>> GetChatAdminsAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chats where a user is an active admin
    /// </summary>
    Task<List<long>> GetAdminChatsAsync(long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert admin record (insert or update if exists)
    /// </summary>
    Task UpsertAsync(long chatId, long telegramId, bool isCreator, string? username = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark admin as demoted (soft delete via is_active=false)
    /// </summary>
    Task DeactivateAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all admins for a chat (when bot leaves group)
    /// </summary>
    Task DeleteByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh all admin records for a chat (mark existing as verified, used during startup)
    /// </summary>
    Task UpdateLastVerifiedAsync(long chatId, long telegramId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of active admins cached for a chat (used to detect empty cache)
    /// </summary>
    Task<int> GetAdminCountAsync(long chatId, CancellationToken cancellationToken = default);
}
