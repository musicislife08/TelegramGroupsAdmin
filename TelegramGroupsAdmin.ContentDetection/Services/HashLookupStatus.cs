namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Hash lookup status
/// </summary>
public enum HashLookupStatus
{
    /// <summary>
    /// Hash is known-clean (no detections)
    /// </summary>
    Clean,

    /// <summary>
    /// Hash is known-malicious (at least one engine detected it)
    /// </summary>
    Malicious,

    /// <summary>
    /// Hash is unknown (file not in cloud database, must upload to scan)
    /// </summary>
    Unknown,

    /// <summary>
    /// Error during hash lookup
    /// </summary>
    Error
}
