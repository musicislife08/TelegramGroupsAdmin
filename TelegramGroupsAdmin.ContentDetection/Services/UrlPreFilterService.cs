using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Services;

public class UrlPreFilterService : IUrlPreFilterService
{
    private readonly ICachedBlockedDomainsRepository _cacheRepo;
    private readonly IDomainFiltersRepository _filtersRepo;
    private readonly ILogger<UrlPreFilterService> _logger;

    public UrlPreFilterService(
        ICachedBlockedDomainsRepository cacheRepo,
        IDomainFiltersRepository filtersRepo,
        ILogger<UrlPreFilterService> logger)
    {
        _cacheRepo = cacheRepo;
        _filtersRepo = filtersRepo;
        _logger = logger;
    }

    public async Task<HardBlockResult> CheckHardBlockAsync(string messageText, ChatIdentity chat, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return new HardBlockResult(ShouldBlock: false, Reason: null, BlockedDomain: null);
        }

        // Extract URLs from message
        var urls = UrlUtilities.ExtractUrls(messageText) ?? [];
        if (urls.Count == 0)
        {
            return new HardBlockResult(ShouldBlock: false, Reason: null, BlockedDomain: null);
        }

        _logger.LogDebug("Checking {Count} URLs for hard blocks in {Chat}", urls.Count, chat.ToLogDebug());

        // Check whitelist first (whitelist always wins)
        // Use GetEffectiveAsync to fetch only enabled whitelists (optimized query with index)
        var whitelistFilters = await _filtersRepo.GetEffectiveAsync(
            chat.Id,
            DomainFilterType.Whitelist,
            blockMode: null,
            cancellationToken);
        var whitelistDomains = whitelistFilters
            .Select(f => f.Domain.ToLowerInvariant())
            .ToHashSet();

        // Extract domains from URLs
        var domains = urls.Select(ExtractDomain).Where(d => !string.IsNullOrEmpty(d)).ToList();

        // Check whitelist
        foreach (var domain in domains)
        {
            if (whitelistDomains.Contains(domain!))
            {
                _logger.LogDebug("Domain {Domain} is whitelisted, skipping hard block check", domain);
                return new HardBlockResult(ShouldBlock: false, Reason: null, BlockedDomain: null);
            }
        }

        // Check hard block cache (blocklists + manual filters with BlockMode = Hard)
        foreach (var domain in domains)
        {
            var blockedDomain = await _cacheRepo.FindHardBlockAsync(domain!, chat.Id, cancellationToken);
            if (blockedDomain != null)
            {
                var reason = blockedDomain.SourceSubscriptionId.HasValue
                    ? $"Domain {domain} is on hard block list (subscription ID: {blockedDomain.SourceSubscriptionId})"
                    : $"Domain {domain} is on manual hard block list";

                _logger.LogWarning("Hard block triggered for domain {Domain} in {Chat}: {Reason}",
                    domain, chat.ToLogDebug(), reason);

                return new HardBlockResult(
                    ShouldBlock: true,
                    Reason: reason,
                    BlockedDomain: domain);
            }
        }

        return new HardBlockResult(ShouldBlock: false, Reason: null, BlockedDomain: null);
    }


    /// <summary>
    /// Extract domain from URL
    /// Returns normalized domain (lowercase, no www prefix)
    /// </summary>
    private static string? ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            var domain = uri.Host.ToLowerInvariant();

            // Remove www prefix
            if (domain.StartsWith("www."))
            {
                domain = domain[4..];
            }

            return domain;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
