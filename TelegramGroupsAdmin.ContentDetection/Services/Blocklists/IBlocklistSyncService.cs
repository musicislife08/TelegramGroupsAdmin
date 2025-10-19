namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Service for downloading, parsing, and syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
public interface IBlocklistSyncService
{
    /// <summary>
    /// Sync all enabled blocklist subscriptions (download, parse, reconcile cache)
    /// Called by TickerQ job on schedule
    /// </summary>
    Task SyncAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync a specific blocklist subscription by ID
    /// Used when admin manually triggers refresh or adds new subscription
    /// </summary>
    Task SyncSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force full rebuild of cached_blocked_domains from all enabled sources
    /// Used when domain_filters change or admin requests full resync
    /// </summary>
    Task RebuildCacheAsync(long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached domains for a specific subscription (cleanup on disable/delete)
    /// </summary>
    Task RemoveCachedDomainsAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
