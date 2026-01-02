using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Repository for managing configs table (unified configuration storage)
/// </summary>
public interface IConfigRepository
{
    /// <summary>
    /// Get config record for a specific chat (0 = global)
    /// </summary>
    Task<ConfigRecordDto?> GetAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert (insert or update) config record for a specific chat
    /// </summary>
    Task UpsertAsync(ConfigRecordDto config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete config record for a specific chat (0 = global)
    /// </summary>
    Task DeleteAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get config record by chat ID (alias for GetAsync with non-null chatId)
    /// </summary>
    Task<ConfigRecordDto?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save/update invite link for a chat (upserts config row if needed)
    /// </summary>
    Task SaveInviteLinkAsync(long chatId, string inviteLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear cached invite link for a chat (set to null)
    /// Use when link is known to be invalid/revoked
    /// </summary>
    Task ClearInviteLinkAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all cached invite links (for all chats)
    /// Use when admin regenerates all chat links or troubleshooting
    /// </summary>
    Task ClearAllInviteLinksAsync(CancellationToken cancellationToken = default);
}
