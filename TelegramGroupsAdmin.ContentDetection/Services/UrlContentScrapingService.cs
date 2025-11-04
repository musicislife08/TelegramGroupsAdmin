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

            // Format preview for this URL
            previewBuilder.AppendLine(url);

            if (!string.IsNullOrWhiteSpace(preview.Title))
            {
                previewBuilder.AppendLine(preview.Title);
            }

            if (!string.IsNullOrWhiteSpace(preview.Description))
            {
                previewBuilder.AppendLine(preview.Description);
            }

            if (!string.IsNullOrWhiteSpace(preview.OgTitle))
            {
                previewBuilder.AppendLine(preview.OgTitle);
            }

            if (!string.IsNullOrWhiteSpace(preview.OgDescription))
            {
                previewBuilder.AppendLine(preview.OgDescription);
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
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
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
