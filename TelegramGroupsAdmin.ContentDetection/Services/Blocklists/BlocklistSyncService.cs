using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Http;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Service for downloading, parsing, and syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
/// <remarks>
/// Subscription URLs are admin-entered, so downloads go through <see cref="IPublicUrlFetcher"/>:
/// a URL that resolves to a loopback, LAN or other non-public address is refused before any
/// request is made. A refused or failed subscription is reported by name through
/// <see cref="BlocklistSyncException"/>; in <see cref="SyncAllAsync"/> and
/// <see cref="RebuildCacheAsync"/> the remaining subscriptions are still synced first.
/// </remarks>
public class BlocklistSyncService : IBlocklistSyncService
{
    /// <summary>
    /// Most bytes accepted for one blocklist. The largest widely used lists (HaGeZi "ultimate"
    /// in hosts format, OISD "big", StevenBlack unified with every extension) are tens of
    /// megabytes; 64 MB leaves headroom while bounding what one subscription can make the
    /// single-instance server buffer and parse.
    /// </summary>
    public const long MaxBlocklistBytes = 64L * 1024 * 1024;

    private static readonly PublicUrlFetchOptions FetchOptions = new() { MaxBytes = MaxBlocklistBytes };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublicUrlFetcher _urlFetcher;
    private readonly ILogger<BlocklistSyncService> _logger;

    // Map of parsers by format
    private readonly Dictionary<BlocklistFormat, IBlocklistParser> _parsers;

    public BlocklistSyncService(
        IServiceScopeFactory scopeFactory,
        IPublicUrlFetcher urlFetcher,
        ILogger<BlocklistSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _urlFetcher = urlFetcher;
        _logger = logger;

        // Initialize parsers
        _parsers = new Dictionary<BlocklistFormat, IBlocklistParser>
        {
            [BlocklistFormat.NewlineDomains] = new NewlineDomainsParser(),
            [BlocklistFormat.HostsFile] = new HostsFileParser(),
            [BlocklistFormat.Csv] = new CsvBlocklistParser()
        };
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting blocklist sync for all enabled subscriptions");

        // Get enabled subscriptions using a separate scope
        List<(long Id, string Name)> enabled;
        using (var scope = _scopeFactory.CreateScope())
        {
            var subscriptionsRepo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();
            var subscriptions = await subscriptionsRepo.GetAllAsync(cancellationToken: cancellationToken);
            enabled = subscriptions.Where(s => s.Enabled).Select(s => (s.Id, s.Name)).ToList();
        }

        _logger.LogInformation("Found {Count} enabled subscriptions to sync", enabled.Count);

        var failures = await SyncEachAsync(enabled, cancellationToken);

        _logger.LogInformation("Completed blocklist sync for all subscriptions ({Failed} failed)", failures.Count);

        if (failures.Count > 0)
            throw new BlocklistSyncException(failures, enabled.Count);
    }

    /// <summary>
    /// Syncs each subscription sequentially (parallel execution deadlocks on the unique index
    /// domain, block_mode, chat_id) and keeps going past one that fails, so a single refused or
    /// unreachable list cannot stop the others from refreshing.
    /// </summary>
    private async Task<List<BlocklistSyncFailure>> SyncEachAsync(
        List<(long Id, string Name)> subscriptions, CancellationToken cancellationToken)
    {
        var failures = new List<BlocklistSyncFailure>();
        foreach (var (id, name) in subscriptions)
        {
            try
            {
                await SyncSubscriptionAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BlocklistSyncException ex)
            {
                failures.AddRange(ex.Failures);
            }
            catch (Exception ex)
            {
                // Already logged by SyncSubscriptionAsync; recorded here so the run can report it.
                failures.Add(new BlocklistSyncFailure(id, name, ex.Message));
            }
        }

        return failures;
    }

    public async Task SyncSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        // Create a new scope for this subscription to avoid DbContext concurrency issues
        using var scope = _scopeFactory.CreateScope();
        var subscriptionsRepo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();
        var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();

        var subscription = await subscriptionsRepo.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found, skipping sync", subscriptionId);
            return;
        }

        if (!subscription.Enabled)
        {
            _logger.LogInformation("Subscription {SubscriptionId} ({Name}) is disabled, skipping sync", subscriptionId, subscription.Name);
            return;
        }

        _logger.LogInformation("Syncing subscription {SubscriptionId} ({Name}) from {Url}",
            subscriptionId, subscription.Name, subscription.Url);

        try
        {
            // Download blocklist
            var content = await DownloadBlocklistAsync(subscription.Url, cancellationToken);

            // Parse domains
            var parser = _parsers[subscription.Format];
            var domains = parser.Parse(content);

            _logger.LogInformation("Parsed {Count} domains from {Name}", domains.Count, subscription.Name);

            // Remove old cached entries for this subscription
            await cacheRepo.DeleteBySourceAsync("subscription", subscriptionId, cancellationToken);

            // Insert new cached entries
            var cachedDomains = domains.Select(domain => new CachedBlockedDomain(
                Id: 0,  // Will be assigned by database
                Domain: domain,
                BlockMode: subscription.BlockMode,
                ChatId: subscription.ChatId,
                SourceSubscriptionId: subscriptionId,
                FirstSeen: DateTimeOffset.UtcNow,
                LastVerified: DateTimeOffset.UtcNow,
                Notes: null
            )).ToList();

            await cacheRepo.BulkInsertAsync(cachedDomains, cancellationToken);

            // Update subscription metadata
            await subscriptionsRepo.UpdateFetchMetadataAsync(
                subscriptionId,
                DateTimeOffset.UtcNow,
                domains.Count,
                cancellationToken);

            _logger.LogInformation("Successfully synced {Count} domains for {Name}", domains.Count, subscription.Name);
        }
        catch (PublicUrlFetchException ex)
        {
            // ex.Message is safe to show; ex.Reason may name the address and stays in the log.
            _logger.LogError("Could not download blocklist {Name} ({SubscriptionId}) from {Url}: {Reason}",
                subscription.Name, subscriptionId, subscription.Url, ex.Reason);
            throw new BlocklistSyncException(new BlocklistSyncFailure(subscriptionId, subscription.Name, ex.Message), ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing subscription {SubscriptionId} ({Name})",
                subscriptionId, subscription.Name);
            throw;
        }
    }

    public async Task RebuildCacheAsync(long chatId = 0, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full cache rebuild for chatId={ChatId}", chatId == 0 ? "global" : chatId.ToString());

        // Get enabled subscriptions using a separate scope
        List<(long Id, string Name)> enabled;
        using (var scope = _scopeFactory.CreateScope())
        {
            var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();
            var subscriptionsRepo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();

            // Delete all cached domains for this chat (or all if chatId=null)
            await cacheRepo.DeleteAllAsync(chatId, cancellationToken);

            // Get all enabled subscriptions for this chat
            var subscriptions = await subscriptionsRepo.GetAllAsync(chatId, cancellationToken);
            enabled = subscriptions.Where(s => s.Enabled).Select(s => (s.Id, s.Name)).ToList();
        }

        _logger.LogInformation("Rebuilding cache from {Count} enabled subscriptions", enabled.Count);

        // Sync each subscription (this will repopulate cache with its own scope); a failed one
        // must not leave the cache without the others or without the manual filters.
        var failures = await SyncEachAsync(enabled, cancellationToken);

        // Add manual domain filters to cache
        await SyncManualFiltersAsync(chatId, cancellationToken);

        _logger.LogInformation("Completed full cache rebuild ({Failed} subscriptions failed)", failures.Count);

        if (failures.Count > 0)
            throw new BlocklistSyncException(failures, enabled.Count);
    }

    public async Task RemoveCachedDomainsAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();

        _logger.LogInformation("Removing cached domains for subscription {SubscriptionId}", subscriptionId);
        await cacheRepo.DeleteBySourceAsync("subscription", subscriptionId, cancellationToken);
    }

    /// <summary>
    /// Download blocklist content from URL through the public-url fetcher, under <see cref="MaxBlocklistBytes"/>.
    /// Automatically upgrades HTTP to HTTPS for security, falling back to HTTP only when the HTTPS
    /// endpoint is unreachable, answers an error status, or redirects back to HTTP (the downgrade is refused,
    /// and the admin configured that HTTP URL anyway). Any other refusal (same address, so HTTP would be
    /// refused too), a timeout or an oversized list fails the sync instead of retrying in plaintext.
    /// </summary>
    /// <exception cref="PublicUrlFetchException">The URL was refused, too large, unreachable or answered an error.</exception>
    private async Task<string> DownloadBlocklistAsync(string url, CancellationToken cancellationToken)
    {
        // Security: If URL starts with http://, try https:// first to prevent MitM attacks
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var httpsUrl = "https://" + url[7..]; // Replace http:// with https://

            try
            {
                _logger.LogDebug("Attempting HTTPS upgrade for blocklist: {OriginalUrl} → {HttpsUrl}", url, httpsUrl);

                var httpsContent = await FetchTextAsync(httpsUrl, cancellationToken);

                _logger.LogInformation("Successfully downloaded blocklist via HTTPS (upgraded from HTTP): {HttpsUrl} ({Size} bytes)",
                    httpsUrl, httpsContent.Length);

                return httpsContent;
            }
            catch (PublicUrlFetchException ex) when (ex.Kind is PublicUrlFetchFailure.Unreachable or PublicUrlFetchFailure.HttpStatus or PublicUrlFetchFailure.DowngradeRefused)
            {
                _logger.LogWarning("HTTPS upgrade failed for {HttpsUrl} ({Reason}), falling back to insecure HTTP {HttpUrl}",
                    httpsUrl, ex.Reason, url);
                // Fall through to HTTP attempt below
            }
        }

        // Original URL (either already HTTPS, or HTTP fallback after HTTPS failed)
        _logger.LogDebug("Downloading blocklist from {Url}", url);

        var content = await FetchTextAsync(url, cancellationToken);

        // Warn if we're downloading via insecure HTTP
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Blocklist downloaded via insecure HTTP (vulnerable to tampering): {Url} ({Size} bytes) - " +
                "Consider using HTTPS to prevent man-in-the-middle attacks",
                url, content.Length);
        }
        else
        {
            _logger.LogDebug("Downloaded {Size} bytes from {Url}", content.Length, url);
        }

        return content;
    }

    /// <summary>Blocklists are ASCII/UTF-8 text (domains, hosts lines, CSV); decoded as UTF-8.</summary>
    private async Task<string> FetchTextAsync(string url, CancellationToken cancellationToken)
    {
        var fetched = await _urlFetcher.FetchAsync(url, FetchOptions, cancellationToken);
        return Encoding.UTF8.GetString(fetched.Content);
    }

    /// <summary>
    /// Sync manual domain filters (blacklist entries) into cache
    /// Note: Whitelist entries are NOT cached - they're checked separately in real-time
    /// </summary>
    private async Task SyncManualFiltersAsync(long chatId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var filtersRepo = scope.ServiceProvider.GetRequiredService<IDomainFiltersRepository>();
        var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();

        var filters = await filtersRepo.GetAllAsync(chatId, cancellationToken);
        var blacklistFilters = filters
            .Where(f => f.Enabled && f.FilterType == DomainFilterType.Blacklist)
            .ToList();

        if (blacklistFilters.Count == 0)
        {
            _logger.LogDebug("No manual blacklist filters to sync");
            return;
        }

        _logger.LogInformation("Syncing {Count} manual blacklist filters to cache", blacklistFilters.Count);

        var cachedDomains = blacklistFilters.Select(filter => new CachedBlockedDomain(
            Id: 0,  // Will be assigned by database
            Domain: filter.Domain,
            BlockMode: filter.BlockMode,
            ChatId: filter.ChatId,
            SourceSubscriptionId: null,  // NULL = manual filter (not from subscription)
            FirstSeen: filter.AddedDate,
            LastVerified: DateTimeOffset.UtcNow,
            Notes: filter.Notes
        )).ToList();

        await cacheRepo.BulkInsertAsync(cachedDomains, cancellationToken);

        _logger.LogInformation("Synced {Count} manual filters to cache", blacklistFilters.Count);
    }
}
