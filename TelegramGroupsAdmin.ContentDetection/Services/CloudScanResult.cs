namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Result from cloud file upload and scan
/// </summary>
public class CloudScanResult
{
    /// <summary>
    /// Cloud service that performed the scan
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// File is clean (true) or infected (false)
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// Scan result type
    /// </summary>
    public required CloudScanResultType ResultType { get; init; }

    /// <summary>
    /// Threat name if infected
    /// </summary>
    public string? ThreatName { get; init; }

    /// <summary>
    /// Number of AV engines that detected this file (multi-engine scanners)
    /// </summary>
    public int? DetectionCount { get; init; }

    /// <summary>
    /// Total number of engines that scanned
    /// </summary>
    public int? TotalEngines { get; init; }

    /// <summary>
    /// Scan duration in milliseconds
    /// </summary>
    public int ScanDurationMs { get; init; }

    /// <summary>
    /// Error message if scan failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional metadata from the scan
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Whether quota was consumed by this scan
    /// </summary>
    public bool QuotaConsumed { get; init; } = true;
}
