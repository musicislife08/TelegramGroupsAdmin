using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for blocklist subscriptions (external URL-based blocklists)
/// Phase 4.13: URL Filtering
/// </summary>
public interface IBlocklistSubscriptionsRepository
{
    /// <summary>
    /// Get all blocklist subscriptions (global and chat-specific)
    /// </summary>
    Task<List<BlocklistSubscription>> GetAllAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get effective subscriptions for a chat (merges global + chat-specific)
    /// </summary>
    Task<List<BlocklistSubscription>> GetEffectiveAsync(long chatId, BlockMode? blockMode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get subscription by ID
    /// </summary>
    Task<BlocklistSubscription?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert new blocklist subscription
    /// </summary>
    Task<long> InsertAsync(BlocklistSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing blocklist subscription
    /// </summary>
    Task UpdateAsync(BlocklistSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete blocklist subscription
    /// </summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update fetch metadata (last_fetched, entry_count) after successful sync
    /// </summary>
    Task UpdateFetchMetadataAsync(long id, DateTimeOffset lastFetched, int entryCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a URL already exists in subscriptions (for duplicate detection)
    /// Returns all matching subscriptions (global + chat-specific)
    /// </summary>
    Task<List<BlocklistSubscription>> FindByUrlAsync(string url, long? chatId = null, CancellationToken cancellationToken = default);
}
