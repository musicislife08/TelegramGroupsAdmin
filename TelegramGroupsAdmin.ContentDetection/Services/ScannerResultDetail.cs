namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Individual scanner result detail for UI display
/// </summary>
public class ScannerResultDetail
{
    /// <summary>
    /// Scanner name (e.g., "ClamAV", "YARA")
    /// </summary>
    public required string ScannerName { get; init; }

    /// <summary>
    /// Result type (Clean, Infected, Error)
    /// </summary>
    public required ScanResultType ResultType { get; init; }

    /// <summary>
    /// File is clean per this scanner
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// Threat name detected (if any)
    /// </summary>
    public string? ThreatName { get; init; }

    /// <summary>
    /// Scan duration for this scanner in milliseconds
    /// </summary>
    public int? ScanDurationMs { get; init; }

    /// <summary>
    /// Error message (if scan failed)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional metadata (e.g., YARA rules matched)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
