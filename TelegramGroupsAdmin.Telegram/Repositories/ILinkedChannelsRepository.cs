using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing linked channels associated with managed chats.
/// Used for impersonation detection against channel names and photos.
/// </summary>
public interface ILinkedChannelsRepository
{
    /// <summary>
    /// Upsert (insert or update) a linked channel record.
    /// Updates existing record if channel_id already exists for the chat.
    /// </summary>
    Task UpsertAsync(LinkedChannelRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get linked channel by managed chat ID (1:1 relationship).
    /// </summary>
    Task<LinkedChannelRecord?> GetByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get linked channel by channel ID.
    /// </summary>
    Task<LinkedChannelRecord?> GetByChannelIdAsync(long channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete linked channel record for a managed chat.
    /// Called when a channel is unlinked from a group.
    /// </summary>
    Task DeleteByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all linked channels.
    /// Used for impersonation detection to compare against all protected channels.
    /// </summary>
    Task<List<LinkedChannelRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all chat IDs that have linked channels.
    /// Used by sync service to know which chats need channel updates.
    /// </summary>
    Task<HashSet<long>> GetAllManagedChatIdsAsync(CancellationToken cancellationToken = default);
}
