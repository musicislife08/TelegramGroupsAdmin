using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that validates URLs against cached domain filters (soft blocks only)
/// Hard blocks are handled by UrlPreFilterService before spam detection
/// Phase 4.13: URL Filtering
/// V2: Scores 2.0 points for blocklisted domains, abstains when no matches
/// </summary>
public partial class UrlBlocklistSpamCheckV2(
    ILogger<UrlBlocklistSpamCheckV2> logger,
    ICachedBlockedDomainsRepository cacheRepo,
    IDomainFiltersRepository filtersRepo) : IContentCheckV2
{
    private static readonly Regex DomainRegex = CompiledDomainRegex();

    public CheckName CheckName => CheckName.UrlBlocklist;

    /// <summary>
    /// Check if URL blocklist check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Only run if message contains URLs or domains
        return ExtractUrlsAndDomains(request.Message).Any();
    }

    /// <summary>
    /// Execute URL filter spam check (soft blocks only, hard blocks already filtered)
    /// </summary>
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (UrlBlocklistCheckRequest)request;

        try
        {
            // Extract URLs and domains from message
            var urls = ExtractUrlsAndDomains(req.Message);
            var domains = urls.Select(ExtractDomain).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();

            if (domains.Count == 0)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No URLs found in message"
                };
            }

            logger.LogDebug("URL filter check for user {UserId}: Checking {DomainCount} domains in chat {ChatId}",
                req.UserId, domains.Count, req.ChatId);

            // Check whitelist first (whitelist bypasses all filters)
            // Use GetEffectiveAsync to fetch only enabled whitelists (optimized query with index)
            var whitelistFilters = await filtersRepo.GetEffectiveAsync(
                req.ChatId,
                DomainFilterType.Whitelist,
                blockMode: null,
                req.CancellationToken);
            var whitelistDomains = whitelistFilters
                .Select(f => NormalizeDomain(f.Domain))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var domain in domains)
            {
                var normalized = NormalizeDomain(domain!);

                if (whitelistDomains.Contains(normalized))
                {
                    logger.LogDebug("Domain {Domain} is whitelisted, skipping filter check", normalized);
                    continue;
                }

                // Check soft block cache (BlockMode = Soft)
                var blockedDomain = await cacheRepo.GetByDomainAsync(normalized, req.ChatId, BlockMode.Soft, req.CancellationToken);
                if (blockedDomain != null)
                {
                    var source = blockedDomain.SourceSubscriptionId.HasValue
                        ? $"blocklist subscription ID {blockedDomain.SourceSubscriptionId}"
                        : "manual filter";

                    logger.LogInformation("URL filter match for user {UserId}: Domain {Domain} on soft block list (source: {Source})",
                        req.UserId, normalized, source);

                    return new ContentCheckResponseV2
                    {
                        CheckName = CheckName,
                        Score = 2.0,
                        Abstained = false,
                        Details = $"Domain '{normalized}' on soft block list (source: {source})"
                    };
                }
            }

            logger.LogDebug("URL filter check for user {UserId}: No soft blocks found for {DomainCount} domains",
                req.UserId, domains.Count);

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"No filter matches for {domains.Count} domains"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "URL filter check failed for user {UserId}", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex
            };
        }
    }

    /// <summary>
    /// Normalize domain: lowercase, trim, remove www prefix
    /// Must match repository normalization logic
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();

        // Remove www prefix if present
        if (domain.StartsWith("www."))
        {
            domain = domain[4..];
        }

        return domain;
    }

    /// <summary>
    /// Extract URLs and domains from message text
    /// </summary>
    private static List<string> ExtractUrlsAndDomains(string message)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract full URLs using shared utility
        var urls = UrlUtilities.ExtractUrls(message);
        if (urls != null)
        {
            foreach (var url in urls)
                found.Add(url);
        }

        // Extract standalone domains
        foreach (Match m in DomainRegex.Matches(message))
        {
            var domain = m.Value;
            if (!found.Any(url => url.Contains(domain, StringComparison.OrdinalIgnoreCase)))
                found.Add(domain);
        }

        return found.ToList();
    }

    /// <summary>
    /// Extract domain from URL or return as-is if already a domain
    /// </summary>
    private static string ExtractDomain(string urlOrDomain)
    {
        // If it's a full URL, extract the host
        if (Uri.TryCreate(urlOrDomain, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        // Otherwise assume it's already a domain
        return urlOrDomain;
    }

    [GeneratedRegex(@"\b[\w\-_.]+\.[a-z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledDomainRegex();
}