namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for enriching message text with scraped URL preview content.
/// Extracts URLs, scrapes metadata (title, description, Open Graph tags),
/// and appends formatted previews to message text with delimiter.
/// </summary>
public interface IUrlContentScrapingService
{
    /// <summary>
    /// Enriches message text by scraping all URLs and appending preview metadata.
    /// </summary>
    /// <param name="messageText">Original message text containing URLs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Enriched message text with URL previews appended after delimiter,
    /// or original text if no URLs found or all scrapes failed
    /// </returns>
    Task<string> EnrichMessageWithUrlPreviewsAsync(string messageText, CancellationToken cancellationToken = default);
}
