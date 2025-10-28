namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Aggregated result from Tier 2 cloud queue scanning
/// </summary>
public class Tier2ScanResult
{
    /// <summary>
    /// File is clean (true) or infected (false)
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// At least one cloud service detected a threat
    /// </summary>
    public required bool ThreatDetected { get; init; }

    /// <summary>
    /// Results from cloud file scans (upload + scan)
    /// </summary>
    public required List<CloudScanResult> CloudScanResults { get; init; }

    /// <summary>
    /// Results from hash-only lookups (no upload)
    /// </summary>
    public required List<(string ServiceName, CloudHashLookupResult Result)> HashLookupResults { get; init; }

    /// <summary>
    /// Total duration for Tier 2 processing (sequential)
    /// </summary>
    public required int TotalDurationMs { get; init; }

    /// <summary>
    /// Threat names detected (if any)
    /// </summary>
    public List<string>? ThreatNames { get; init; }

    /// <summary>
    /// Cloud services that detected threats
    /// </summary>
    public List<string>? DetectedBy { get; init; }

    /// <summary>
    /// All cloud services were exhausted/unavailable
    /// </summary>
    public bool AllServicesExhausted { get; init; }

    /// <summary>
    /// Which service provided the final decision (for debugging)
    /// </summary>
    public string? DecisionSource { get; init; }
}
