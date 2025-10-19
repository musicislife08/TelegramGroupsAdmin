using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

/// <summary>
/// Service for downloading, parsing, and syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
public class BlocklistSyncService : IBlocklistSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlocklistSyncService> _logger;

    // Map of parsers by format
    private readonly Dictionary<BlocklistFormat, IBlocklistParser> _parsers;

    public BlocklistSyncService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<BlocklistSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
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

        // Get enabled subscription IDs using a separate scope
        List<long> enabledSubscriptionIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var subscriptionsRepo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();
            var subscriptions = await subscriptionsRepo.GetAllAsync(cancellationToken: cancellationToken);
            enabledSubscriptionIds = subscriptions.Where(s => s.Enabled).Select(s => s.Id).ToList();
        }

        _logger.LogInformation("Found {Count} enabled subscriptions to sync", enabledSubscriptionIds.Count);

        // Sync each subscription in parallel, each with its own scope
        var syncTasks = enabledSubscriptionIds.Select(id => SyncSubscriptionAsync(id, cancellationToken));
        await Task.WhenAll(syncTasks);

        _logger.LogInformation("Completed blocklist sync for all subscriptions");
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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error downloading blocklist {Name} from {Url}",
                subscription.Name, subscription.Url);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing subscription {SubscriptionId} ({Name})",
                subscriptionId, subscription.Name);
            throw;
        }
    }

    public async Task RebuildCacheAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full cache rebuild for chatId={ChatId}", chatId?.ToString() ?? "global");

        // Get enabled subscription IDs using a separate scope
        List<long> enabledSubscriptionIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();
            var subscriptionsRepo = scope.ServiceProvider.GetRequiredService<IBlocklistSubscriptionsRepository>();

            // Delete all cached domains for this chat (or all if chatId=null)
            await cacheRepo.DeleteAllAsync(chatId, cancellationToken);

            // Get all enabled subscriptions for this chat
            var subscriptions = await subscriptionsRepo.GetAllAsync(chatId, cancellationToken);
            enabledSubscriptionIds = subscriptions.Where(s => s.Enabled).Select(s => s.Id).ToList();
        }

        _logger.LogInformation("Rebuilding cache from {Count} enabled subscriptions", enabledSubscriptionIds.Count);

        // Sync each subscription (this will repopulate cache with its own scope)
        foreach (var subscriptionId in enabledSubscriptionIds)
        {
            await SyncSubscriptionAsync(subscriptionId, cancellationToken);
        }

        // Add manual domain filters to cache
        await SyncManualFiltersAsync(chatId, cancellationToken);

        _logger.LogInformation("Completed full cache rebuild");
    }

    public async Task RemoveCachedDomainsAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheRepo = scope.ServiceProvider.GetRequiredService<ICachedBlockedDomainsRepository>();

        _logger.LogInformation("Removing cached domains for subscription {SubscriptionId}", subscriptionId);
        await cacheRepo.DeleteBySourceAsync("subscription", subscriptionId, cancellationToken);
    }

    /// <summary>
    /// Download blocklist content from URL
    /// Automatically upgrades HTTP to HTTPS for security (with HTTP fallback)
    /// </summary>
    private async Task<string> DownloadBlocklistAsync(string url, CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Security: If URL starts with http://, try https:// first to prevent MitM attacks
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var httpsUrl = "https://" + url[7..]; // Replace http:// with https://

            try
            {
                _logger.LogDebug("Attempting HTTPS upgrade for blocklist: {OriginalUrl} → {HttpsUrl}", url, httpsUrl);

                var httpsResponse = await httpClient.GetAsync(httpsUrl, cancellationToken);
                httpsResponse.EnsureSuccessStatusCode();

                var httpsContent = await httpsResponse.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("Successfully downloaded blocklist via HTTPS (upgraded from HTTP): {HttpsUrl} ({Size} bytes)",
                    httpsUrl, httpsContent.Length);

                return httpsContent;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTPS upgrade failed for {HttpsUrl}, falling back to insecure HTTP {HttpUrl}",
                    httpsUrl, url);
                // Fall through to HTTP attempt below
            }
        }

        // Original URL (either already HTTPS, or HTTP fallback after HTTPS failed)
        _logger.LogDebug("Downloading blocklist from {Url}", url);

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

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

    /// <summary>
    /// Sync manual domain filters (blacklist entries) into cache
    /// Note: Whitelist entries are NOT cached - they're checked separately in real-time
    /// </summary>
    private async Task SyncManualFiltersAsync(long? chatId, CancellationToken cancellationToken)
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
