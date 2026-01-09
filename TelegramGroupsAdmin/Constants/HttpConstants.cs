namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for HTTP client configurations.
/// </summary>
public static class HttpConstants
{
    /// <summary>
    /// SEO preview scraper timeout (5 seconds).
    /// </summary>
    public static readonly TimeSpan SeoScraperTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// VirusTotal API rate limit (4 requests per minute for free tier).
    /// </summary>
    public const int VirusTotalPermitLimit = 4;

    /// <summary>
    /// VirusTotal rate limit window (1 minute).
    /// </summary>
    public static readonly TimeSpan VirusTotalWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// VirusTotal rate limiter segments per window (4 segments = 15-second intervals).
    /// </summary>
    public const int VirusTotalSegmentsPerWindow = 4;

    /// <summary>
    /// VirusTotal rate limiter queue limit (handles burst during file upload + analysis polling).
    /// </summary>
    public const int VirusTotalQueueLimit = 10;

    /// <summary>
    /// HybridCache maximum payload size (10 MB).
    /// </summary>
    public const int HybridCacheMaxPayloadBytes = 10 * 1024 * 1024;
}
