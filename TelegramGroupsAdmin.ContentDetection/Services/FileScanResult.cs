namespace TelegramGroupsAdmin.ContentDetection.Services;

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
