using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for normalized cached blocked domains (reconciled from subscriptions + manual filters)
/// Phase 4.13: URL Filtering
/// </summary>
public interface ICachedBlockedDomainsRepository
{
    /// <summary>
    /// Get all cached blocked domains for a chat (global + chat-specific)
    /// </summary>
    Task<List<CachedBlockedDomain>> GetAllAsync(long? chatId = null, BlockMode? blockMode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search cached domains by domain name (for domain lookup tool)
    /// </summary>
    Task<CachedBlockedDomain?> GetByDomainAsync(string domain, long chatId, BlockMode blockMode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find domain in hard block cache (BlockMode = Hard)
    /// Used by UrlPreFilterService for instant blocking
    /// </summary>
    Task<CachedBlockedDomain?> FindHardBlockAsync(string domain, long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk insert cached domains (for sync service efficiency)
    /// </summary>
    Task BulkInsertAsync(List<CachedBlockedDomain> domains, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete cached domains by source (for cleanup when subscription/filter is removed)
    /// </summary>
    Task DeleteBySourceAsync(string sourceType, long sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all cached domains for a chat (for full resync)
    /// </summary>
    Task DeleteAllAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get domain statistics for UI display
    /// </summary>
    Task<UrlFilterStats> GetStatsAsync(long? chatId = null, CancellationToken cancellationToken = default);
}
