namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Aggregated result from Tier 1 parallel scanning
/// </summary>
public class Tier1ScanResult
{
    /// <summary>
    /// File is clean (true) or infected (false)
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// At least one scanner detected a threat
    /// </summary>
    public required bool ThreatDetected { get; init; }

    /// <summary>
    /// Individual scanner results
    /// </summary>
    public required List<FileScanResult> ScannerResults { get; init; }

    /// <summary>
    /// Total scan duration (parallel execution time)
    /// </summary>
    public required int TotalDurationMs { get; init; }

    /// <summary>
    /// Threat names detected (if any)
    /// </summary>
    public List<string>? ThreatNames { get; init; }

    /// <summary>
    /// Scanners that detected threats
    /// </summary>
    public List<string>? DetectedBy { get; init; }

    /// <summary>
    /// All scanners encountered errors (fail-open scenario)
    /// </summary>
    public bool AllScannersErrored { get; init; }

    /// <summary>
    /// Reason scan was skipped (e.g., file too large)
    /// </summary>
    public string? SkippedReason { get; init; }
}
