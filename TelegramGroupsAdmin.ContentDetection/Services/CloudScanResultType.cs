namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Cloud scan result types
/// </summary>
public enum CloudScanResultType
{
    /// <summary>
    /// File is clean (no threats detected)
    /// </summary>
    Clean,

    /// <summary>
    /// File is infected (threat detected)
    /// </summary>
    Infected,

    /// <summary>
    /// Scan failed or quota exhausted
    /// </summary>
    Error,

    /// <summary>
    /// Service temporarily unavailable (rate limited)
    /// </summary>
    RateLimited
}
