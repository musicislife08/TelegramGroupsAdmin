using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Hybrid;

namespace TgSpam_PreFilterApi;

public sealed partial class SpamCheckService(HybridCache cache, IHttpClientFactory http, SeoPreviewScraper seo, IThreatIntelService threatIntel) // now includes SeoPreviewScraper
{
    private static readonly string[] BlocklistNames = [
        "abuse", "fraud", "malware", "phishing", "ransomware", "redirect", "scam"
    ];

    private static readonly Regex UrlRegex = CompiledUrlRegex();
    private static readonly Regex DomainRegex = CompiledDomainRegex();

    public async Task<CheckResult> CheckMessageAsync(string message)
    {
        var allUrls = ExtractUrlsAndDomains(message);

        foreach (var url in allUrls)
        {
            var domain = ExtractDomain(url);

            // Blocklist check
            foreach (var listName in BlocklistNames)
            {
                var entries = await cache.GetOrCreateAsync(
                    $"blocklist::{listName}",
                    listName,
                    FetchBlocklistEntriesAsync
                );

                var match = entries.FirstOrDefault(entry =>
                    domain.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                    domain.EndsWith($".{entry}", StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    return new(true, $"Blocked by list '{listName}' for domain '{domain}' matching '{match}' in URL: {url}");
                }
            }

            // SEO/OG scraping if no blocklist match
            var preview = await seo.GetSeoPreviewAsync(url);
            if (preview is null) continue;
            var combinedText = string.Join(' ', preview.Title, preview.Description, preview.OgTitle, preview.OgDescription);

            if (LooksSuspicious(combinedText))
            {
                return new(true, $"Suspicious preview content in URL: {url}");
            }

            if (await threatIntel.IsThreatAsync(url))
            {
                return new(true, $"URL flagged by Google Safe Browsing: {url}");
            }
        }

        return new(false, "no blocklist matches or suspicious preview content");
    }

    private async ValueTask<List<string>> FetchBlocklistEntriesAsync(string listName, CancellationToken ct)
    {
        using var client = http.CreateClient();
        var url = $"https://blocklistproject.github.io/Lists/alt-version/{listName}-nl.txt";
        var raw = await client.GetStringAsync(url, ct);

        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractUrlsAndDomains(string message)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in UrlRegex.Matches(message))
            found.Add(m.Value);

        foreach (Match m in DomainRegex.Matches(message))
        {
            var domain = m.Value;
            if (!found.Any(url => url.Contains(domain, StringComparison.OrdinalIgnoreCase)))
                found.Add(domain);
        }

        return found.ToList();
    }

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

    private static string NormalizeVisualNoise(string input)
    {
        // Strip out fancy Unicode fonts (e.g., Mathematical Alphanumeric Symbols)
        return input.Normalize(NormalizationForm.FormKD)
            .Where(c => !char.IsControl(c))
            .Select(c => char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark ? '\0' : c)
            .Where(c => c != '\0')
            .Aggregate(new StringBuilder(), (sb, c) => sb.Append(c), sb => sb.ToString());
    }

    private static readonly Regex[] SuspiciousPatterns =
    [
        // Common scam formats with variable amounts/timing
        DepositedRegex(),
        GotInHoursRegex(),
        ReceivedRegex(),
        EarnRegex(),
        ReferralRegex(),
        InvestedRegex(),
        ProfitInTimeRegex(),
    ];

    private static readonly string[] SuspiciousPhrases =
    [
        "grow your crypto", "investment plan", "build consistent daily income",
        "crypto market booming", "bitcoin on the rise", "withdrawals are instant",
        "second profit", "best platform", "most honest trader", "forex mentor",
        "your profits", "click the link", "join now", "send me a direct message",
        "try it urself", "here is the link", "ready to get started", "don’t miss out",
        "already trading on opensea", "nft airdrop", "yes cards", "secure delivery service",
        "make withdrawals at any atm", "lost superfoods", "doomsday cracker", "lewis and clark",
        "native american superfood", "make money with", "learn how to make money", "earn money fast",
        "passive income", "get paid instantly", "work from home", "start earning today", "earn extra income",
        "help to earn", "help you earn", "100% assured profit", "guaranteed profit", "assured returns",
        "earn daily", "safe investment","expert trader", "the industry is hiring", "crypto industry", "founder ◇◇",
        "blockchainassn"
    ];

    private static bool LooksSuspicious(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var text = NormalizeVisualNoise(content.ToLowerInvariant());

        return SuspiciousPhrases.Any(p => text.Contains(p)) ||
               SuspiciousPatterns.Any(r => r.IsMatch(text));
    }

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledUrlRegex();

    [GeneratedRegex(@"\b[\w\-_.]+\.[a-z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledDomainRegex();
    [GeneratedRegex(@"i\s+deposited\s+\$?\d+.*got\s+\$?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex DepositedRegex();
    [GeneratedRegex(@"got\s+\$?\d+.*in\s+just\s+(a\s+)?\d+\s*(hours?|hrs?)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex GotInHoursRegex();
    [GeneratedRegex(@"received\s+\$?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ReceivedRegex();
    [GeneratedRegex(@"earn\s+(daily\s+)?returns?\s+of\s+\d+%+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex EarnRegex();
    [GeneratedRegex(@"referral\s+bonus\s+.*\d+%+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ReferralRegex();

    [GeneratedRegex(@"i\s+invested\s+\$?\d+.*got\s+\$?\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InvestedRegex();

    [GeneratedRegex(@"\$?\d{1,3}(,\d{3})*(\.\d+)?\s+profit\s+(in|within)\s+\d+\s+(hours?|days?)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ProfitInTimeRegex();
}