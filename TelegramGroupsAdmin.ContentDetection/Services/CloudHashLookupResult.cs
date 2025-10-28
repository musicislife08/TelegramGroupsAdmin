namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Result from hash-only lookup (doesn't consume upload quota)
/// </summary>
public class CloudHashLookupResult
{
    /// <summary>
    /// Whether hash was found in cloud database
    /// </summary>
    public required HashLookupStatus Status { get; init; }

    /// <summary>
    /// If hash is known-malicious, the threat name
    /// </summary>
    public string? ThreatName { get; init; }

    /// <summary>
    /// Number of AV engines that detected this file (e.g., VirusTotal: 15/70 engines)
    /// </summary>
    public int? DetectionCount { get; init; }

    /// <summary>
    /// Total number of engines that scanned (e.g., VirusTotal: 70 engines)
    /// </summary>
    public int? TotalEngines { get; init; }

    /// <summary>
    /// Lookup duration in milliseconds
    /// </summary>
    public int DurationMs { get; init; }

    /// <summary>
    /// Additional metadata from the lookup
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
