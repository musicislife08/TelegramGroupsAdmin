using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for enriching message text with scraped URL preview content.
/// Extracts URLs, scrapes metadata in parallel, and appends formatted previews.
/// </summary>
public partial class UrlContentScrapingService(
    IHttpClientFactory httpClientFactory,
    ILogger<UrlContentScrapingService> logger) : IUrlContentScrapingService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();
    private const string Delimiter = "\n\n━━━ URL Previews ━━━\n\n";

    /// <summary>
    /// Enriches message text by scraping all URLs and appending preview metadata.
    /// </summary>
    public async Task<string> EnrichMessageWithUrlPreviewsAsync(string messageText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return messageText;
        }

        var urls = UrlUtilities.ExtractUrls(messageText);
        if (urls == null || urls.Count == 0)
        {
            return messageText;
        }

        // Configure HTTP client
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (TelegramGroupsAdmin/1.0)");

        // Scrape all URLs in parallel
        var scrapeTasks = urls.Select(url => ScrapeUrlAsync(url, cancellationToken)).ToArray();
        var results = await Task.WhenAll(scrapeTasks);

        // Build preview section from successful scrapes
        var previewBuilder = new StringBuilder();
        var successfulScrapes = 0;

        foreach (var (url, preview) in results)
        {
            if (preview == null)
            {
                continue; // Skip failed scrapes
            }

            successfulScrapes++;

            // Format preview for this URL - single line with URL
            previewBuilder.AppendLine(url);

            // Deduplicate content - collect unique non-empty values
            // Priority order: og:description > description > og:title > title
            var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contentPriority = new[]
            {
                preview.OgDescription,
                preview.Description,
                preview.OgTitle,
                preview.Title
            };

            foreach (var content in contentPriority)
            {
                if (!string.IsNullOrWhiteSpace(content) && seenContent.Add(content))
                {
                    // Normalize newlines: collapse multiple consecutive newlines into single newlines
                    // This preserves line breaks (readability) while preventing blank lines that
                    // would cause the UI to render separate blocks
                    var normalizedContent = Regex.Replace(content, @"(\r?\n){2,}", "\n");
                    previewBuilder.AppendLine(normalizedContent);
                }
            }

            previewBuilder.AppendLine(); // Blank line between URLs
        }

        // Only enrich if we successfully scraped at least one URL
        if (successfulScrapes == 0)
        {
            logger.LogDebug("No URLs successfully scraped for message with {UrlCount} URLs", urls.Count);
            return messageText;
        }

        logger.LogDebug("Enriched message with {SuccessCount}/{TotalCount} URL previews", successfulScrapes, urls.Count);

        return messageText + Delimiter + previewBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// Scrapes a single URL for SEO metadata.
    /// </summary>
    /// <returns>Tuple of (url, preview) where preview is null if scraping failed</returns>
    private async Task<(string url, SeoPreview? preview)> ScrapeUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Failed to scrape {Url}: HTTP {StatusCode}", url, response.StatusCode);
                return (url, null);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogDebug("Skipping {Url}: Non-HTML content type {ContentType}", url, contentType);
                return (url, null);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            var preview = new SeoPreview
            {
                Title = ExtractTag(html, @"<title[^>]*>([^<]*)</title>"),
                Description = ExtractMetaContent(html, "description"),
                OgTitle = ExtractMetaContent(html, "og:title"),
                OgDescription = ExtractMetaContent(html, "og:description")
            };

            return (url, preview);
        }
        catch (TaskCanceledException)
        {
            logger.LogDebug("Timeout scraping {Url}", url);
            return (url, null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to scrape {Url}", url);
            return (url, null);
        }
    }

    /// <summary>
    /// Extract content from HTML tag using regex.
    /// </summary>
    private static string? ExtractTag(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Extract meta tag content using regex.
    /// Supports both name= and property= attributes in any order.
    /// Filters out technical metadata like viewport settings.
    /// </summary>
    private static string? ExtractMetaContent(string html, string nameOrProperty)
    {
        var patterns = new[]
        {
            $@"<meta\s+name\s*=\s*[""{Regex.Escape(nameOrProperty)}""'][^>]*content\s*=\s*[""']([^""']*)[""']",
            $@"<meta\s+property\s*=\s*[""{Regex.Escape(nameOrProperty)}""'][^>]*content\s*=\s*[""']([^""']*)[""']",
            $@"<meta\s+content\s*=\s*[""']([^""']*)[""'][^>]*name\s*=\s*[""{Regex.Escape(nameOrProperty)}""']",
            $@"<meta\s+content\s*=\s*[""']([^""']*)[""'][^>]*property\s*=\s*[""{Regex.Escape(nameOrProperty)}""']"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                var content = match.Groups[1].Value.Trim();

                // Filter out technical metadata that provides no useful content
                if (IsTechnicalMetadata(content))
                {
                    continue;
                }

                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if extracted content is technical metadata (viewport, charset, etc.) rather than meaningful text.
    /// </summary>
    private static bool IsTechnicalMetadata(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 10)
        {
            return true; // Too short to be useful content
        }

        // Check if content is purely numeric (likely an ID like "2231777543")
        if (Regex.IsMatch(content, @"^\d+$"))
        {
            return true;
        }

        // Check if content is mostly numeric with minimal other characters (e.g., "123-456-789")
        var digitCount = content.Count(char.IsDigit);
        if (digitCount > 0 && (float)digitCount / content.Length > 0.8)
        {
            return true; // More than 80% digits = likely an ID or technical value
        }

        // Common technical meta patterns to exclude
        var technicalPatterns = new[]
        {
            @"^width=",                           // viewport: width=device-width
            @"^initial-scale=",                   // viewport: initial-scale=1
            @"^maximum-scale=",                   // viewport: maximum-scale=1
            @"^user-scalable=",                   // viewport: user-scalable=0
            @"^viewport-fit=",                    // viewport: viewport-fit=cover
            @"^charset=",                         // charset=utf-8
            @"^IE=",                              // IE=edge
            @"^text/html;\s*charset=",           // content-type
            @"^application/",                     // application mime types
            @"^image/",                           // image mime types
            @"^no-cache",                         // cache control
            @"^max-age=",                         // cache control
            @"^\d+;\s*url=",                      // refresh redirects
        };

        foreach (var pattern in technicalPatterns)
        {
            if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        // Check if content is mostly technical syntax (lots of equals signs, commas, hyphens but few spaces)
        var techChars = content.Count(c => c is '=' or ',' or ';' or '-');
        var spaces = content.Count(c => c == ' ');

        // If more than 30% technical characters and fewer than 10% spaces, likely technical
        if (content.Length > 0 &&
            (float)techChars / content.Length > 0.3 &&
            (float)spaces / content.Length < 0.1)
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// SEO preview metadata extracted from a webpage.
/// </summary>
internal record SeoPreview
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? OgTitle { get; init; }
    public string? OgDescription { get; init; }
}
