namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Interface for Tier 2 cloud scanning services
/// All cloud scanners (VirusTotal, MetaDefender, Hybrid Analysis, Intezer) implement this
/// </summary>
public interface ICloudScannerService
{
    /// <summary>
    /// Name of the cloud service (e.g., "VirusTotal", "MetaDefender")
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Whether this scanner is enabled in configuration
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Check if hash already exists in cloud service (hash-first optimization)
    /// Returns null if hash lookup not supported by this service
    /// </summary>
    Task<CloudHashLookupResult?> LookupHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload and scan file with cloud service
    /// Only called if hash lookup returned Unknown or if service doesn't support hash lookup
    /// </summary>
    Task<CloudScanResult> ScanFileAsync(
        byte[] fileBytes,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if quota is available for this service
    /// Returns false if daily/monthly quota exhausted or rate limit reached
    /// </summary>
    Task<bool> IsQuotaAvailableAsync(CancellationToken cancellationToken = default);
}

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
