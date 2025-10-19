namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Interface for file scanning services (Tier 1 local scanners)
/// Implemented by ClamAVScannerService, YaraScannerService, WindowsAmsiScannerService
/// </summary>
public interface IFileScannerService
{
    /// <summary>
    /// Scanner name for logging and result attribution
    /// </summary>
    string ScannerName { get; }

    /// <summary>
    /// Scan a file for malware/threats
    /// </summary>
    /// <param name="fileBytes">File content as byte array</param>
    /// <param name="fileName">Original filename (optional, for context)</param>
    /// <param name="cancellationToken">Cancellation token for timeout handling</param>
    /// <returns>Scan result with threat detection status</returns>
    Task<FileScanResult> ScanFileAsync(byte[] fileBytes, string? fileName = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a file scan operation
/// </summary>
public class FileScanResult
{
    /// <summary>
    /// Scanner that produced this result
    /// </summary>
    public required string Scanner { get; init; }

    /// <summary>
    /// File is clean (true) or infected/suspicious (false)
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// Threat/malware name if detected
    /// </summary>
    public string? ThreatName { get; init; }

    /// <summary>
    /// Result classification
    /// </summary>
    public ScanResultType ResultType { get; init; } = ScanResultType.Clean;

    /// <summary>
    /// Scan duration in milliseconds
    /// </summary>
    public int? ScanDurationMs { get; init; }

    /// <summary>
    /// Additional metadata (YARA rules matched, VT engine details, etc.)
    /// Serialized to JSONB in database
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Error message if scan failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Scan result classification
/// </summary>
public enum ScanResultType
{
    /// <summary>
    /// File is clean - no threats detected
    /// </summary>
    Clean = 0,

    /// <summary>
    /// File contains malware or known threat
    /// </summary>
    Infected = 1,

    /// <summary>
    /// File is suspicious but not definitively malicious
    /// </summary>
    Suspicious = 2,

    /// <summary>
    /// Scan encountered an error
    /// </summary>
    Error = 3
}
