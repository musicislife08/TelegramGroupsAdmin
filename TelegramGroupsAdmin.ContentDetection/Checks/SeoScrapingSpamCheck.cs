using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that scrapes URL metadata and analyzes for suspicious content patterns
/// Based on existing SeoPreviewScraper and suspicious content analysis
/// </summary>
public partial class SeoScrapingSpamCheck(
    ILogger<SeoScrapingSpamCheck> logger,
    IHttpClientFactory httpClientFactory) : IContentCheck
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    // Suspicious patterns from existing SpamCheckService
    private static readonly Regex[] SuspiciousPatterns =
    [
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
        "try it urself", "here is the link", "ready to get started", "don't miss out",
        "already trading on opensea", "nft airdrop", "yes cards", "secure delivery service",
        "make withdrawals at any atm", "lost superfoods", "doomsday cracker", "lewis and clark",
        "native american superfood", "make money with", "learn how to make money", "earn money fast",
        "passive income", "get paid instantly", "work from home", "start earning today", "earn extra income",
        "help to earn", "help you earn", "100% assured profit", "guaranteed profit", "assured returns",
        "earn daily", "safe investment","expert trader", "the industry is hiring", "crypto industry", "founder ◇◇",
        "blockchainassn"
    ];

    public string CheckName => "SeoScraping";

    /// <summary>
    /// Check if SEO scraping check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Only run if message contains URLs
        return UrlUtilities.ExtractUrls(request.Message) != null;
    }

    /// <summary>
    /// Execute SEO scraping spam check
    /// </summary>
    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (SeoScrapingCheckRequest)request;

        try
        {
            // Configure HTTP client
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (TelegramGroupsAdmin/1.0)");

            var urls = UrlUtilities.ExtractUrls(req.Message) ?? [];

            foreach (var url in urls)
            {
                var preview = await GetSeoPreviewAsync(url, req.CancellationToken);
                if (preview == null)
                {
                    continue;
                }

                // Combine all text content for analysis
                var combinedText = string.Join(' ',
                    preview.Title,
                    preview.Description,
                    preview.OgTitle,
                    preview.OgDescription);

                if (LooksSuspicious(combinedText))
                {
                    logger.LogDebug("SeoScraping check for user {UserId}: Suspicious content in {Url}",
                        req.UserId, url);

                    return new ContentCheckResponse
                    {
                        CheckName = CheckName,
                        Result = CheckResultType.Spam,
                        Details = $"Suspicious content detected in webpage: {url}",
                        Confidence = req.ConfidenceThreshold
                    };
                }
            }

            logger.LogDebug("SeoScraping check for user {UserId}: No suspicious content found for {UrlCount} URLs",
                req.UserId, urls.Count);

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = $"No suspicious content found for {urls.Count} URLs",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
        }
    }

    /// <summary>
    /// Scrape SEO metadata from URL (simplified version without AngleSharp dependency)
    /// </summary>
    private async Task<SeoPreview?> GetSeoPreviewAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Simple regex-based HTML parsing (not as robust as AngleSharp but avoids dependency)
            return new SeoPreview
            {
                Title = ExtractTag(html, @"<title[^>]*>([^<]*)</title>"),
                Description = ExtractMetaContent(html, "description"),
                OgTitle = ExtractMetaContent(html, "og:title"),
                OgDescription = ExtractMetaContent(html, "og:description"),
                OgImage = ExtractMetaContent(html, "og:image"),
                FinalUrl = response.RequestMessage?.RequestUri?.ToString()
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to scrape SEO preview for URL: {Url}", url);
            return null; // Fail-safe: treat as no preview
        }
    }

    /// <summary>
    /// Extract content from HTML tag using regex
    /// </summary>
    private static string? ExtractTag(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Extract meta tag content using regex
    /// </summary>
    private static string? ExtractMetaContent(string html, string nameOrProperty)
    {
        var patterns = new[]
        {
            $@"<meta\s+name\s*=\s*[""']{Regex.Escape(nameOrProperty)}[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
            $@"<meta\s+property\s*=\s*[""']{Regex.Escape(nameOrProperty)}[""'][^>]*content\s*=\s*[""']([^""']*)[""']",
            $@"<meta\s+content\s*=\s*[""']([^""']*)[""'][^>]*name\s*=\s*[""']{Regex.Escape(nameOrProperty)}[""']",
            $@"<meta\s+content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""']{Regex.Escape(nameOrProperty)}[""']"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Analyze content for suspicious patterns
    /// </summary>
    private static bool LooksSuspicious(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var text = NormalizeVisualNoise(content.ToLowerInvariant());

        return SuspiciousPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
               SuspiciousPatterns.Any(r => r.IsMatch(text));
    }

    /// <summary>
    /// Normalize text by removing visual noise (Unicode fancy fonts, etc.)
    /// </summary>
    private static string NormalizeVisualNoise(string input)
    {
        // Strip out fancy Unicode fonts (e.g., Mathematical Alphanumeric Symbols)
        return input.Normalize(NormalizationForm.FormKD)
            .Where(c => !char.IsControl(c))
            .Select(c => char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark ? '\0' : c)
            .Where(c => c != '\0')
            .Aggregate(new StringBuilder(), (sb, c) => sb.Append(c), sb => sb.ToString());
    }


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

/// <summary>
/// Simplified SEO preview result
/// </summary>
internal record SeoPreview
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? OgTitle { get; init; }
    public string? OgDescription { get; init; }
    public string? OgImage { get; init; }
    public string? FinalUrl { get; init; }
}