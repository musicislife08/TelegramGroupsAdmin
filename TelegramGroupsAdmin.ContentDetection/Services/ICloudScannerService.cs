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
