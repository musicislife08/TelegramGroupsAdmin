using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that validates URLs against multiple blocklists from the Block List Project
/// Based on the existing SpamCheckService URL validation logic
/// </summary>
public partial class UrlBlocklistSpamCheck : ISpamCheck
{
    private readonly ILogger<UrlBlocklistSpamCheck> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    private static readonly string[] BlocklistNames = [
        "abuse", "fraud", "malware", "phishing", "ransomware", "redirect", "scam"
    ];

    private static readonly Regex UrlRegex = CompiledUrlRegex();
    private static readonly Regex DomainRegex = CompiledDomainRegex();

    public string CheckName => "UrlBlocklist";

    public UrlBlocklistSpamCheck(
        ILogger<UrlBlocklistSpamCheck> logger,
        SpamDetectionConfig config,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;

        // Configure HTTP client
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Check if URL blocklist check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Check if URL blocklist check is enabled
        if (!_config.UrlBlocklist.Enabled)
        {
            return false;
        }

        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Only run if message contains URLs or domains
        return ExtractUrlsAndDomains(request.Message).Any();
    }

    /// <summary>
    /// Execute URL blocklist spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var allUrls = ExtractUrlsAndDomains(request.Message);

            foreach (var url in allUrls)
            {
                var domain = ExtractDomain(url);

                // Check against all blocklists
                foreach (var listName in BlocklistNames)
                {
                    var entries = await _cache.GetOrCreateAsync(
                        $"blocklist::{listName}",
                        async entry =>
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
                            return await FetchBlocklistEntriesAsync(listName, cancellationToken);
                        });

                    var match = entries?.FirstOrDefault(entry =>
                        domain.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                        domain.EndsWith($".{entry}", StringComparison.OrdinalIgnoreCase));

                    if (match is not null)
                    {
                        _logger.LogDebug("URL blocklist match for user {UserId}: Domain {Domain} blocked by list {ListName}",
                            request.UserId, domain, listName);

                        return new SpamCheckResponse
                        {
                            CheckName = CheckName,
                            IsSpam = true,
                            Details = $"Domain '{domain}' blocked by '{listName}' list (matched: {match})",
                            Confidence = 95 // High confidence for blocklist matches
                        };
                    }
                }
            }

            _logger.LogDebug("URL blocklist check for user {UserId}: No matches found for {UrlCount} URLs",
                request.UserId, allUrls.Count);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = $"No blocklist matches for {allUrls.Count} URLs",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "URL blocklist check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "URL blocklist check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Fetch blocklist entries from the Block List Project
    /// </summary>
    private async Task<List<string>> FetchBlocklistEntriesAsync(string listName, CancellationToken ct)
    {
        try
        {
            var url = $"https://blocklistproject.github.io/Lists/alt-version/{listName}-nl.txt";
            _logger.LogDebug("Fetching blocklist: {ListName} from {Url}", listName, url);

            var raw = await _httpClient.GetStringAsync(url, ct);

            var entries = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith('#'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogDebug("Loaded {Count} entries from {ListName} blocklist", entries.Count, listName);
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch blocklist: {ListName}", listName);
            return [];
        }
    }

    /// <summary>
    /// Extract URLs and domains from message text
    /// </summary>
    private static List<string> ExtractUrlsAndDomains(string message)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract full URLs
        foreach (Match m in UrlRegex.Matches(message))
            found.Add(m.Value);

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

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledUrlRegex();

    [GeneratedRegex(@"\b[\w\-_.]+\.[a-z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledDomainRegex();
}