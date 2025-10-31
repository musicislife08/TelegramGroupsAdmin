namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Detailed scan result for UI testing
/// </summary>
public class FileScanTestResult
{
    /// <summary>
    /// SHA256 hash of the file
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// Overall scan result: file is infected
    /// </summary>
    public required bool IsInfected { get; init; }

    /// <summary>
    /// Overall scan result: file is clean
    /// </summary>
    public required bool IsClean { get; init; }

    /// <summary>
    /// Primary threat name (if infected)
    /// </summary>
    public string? PrimaryThreatName { get; init; }

    /// <summary>
    /// All detected threat names
    /// </summary>
    public List<string> ThreatNames { get; init; } = [];

    /// <summary>
    /// Scanners that detected threats
    /// </summary>
    public List<string> DetectedBy { get; init; } = [];

    /// <summary>
    /// Individual scanner results
    /// </summary>
    public List<ScannerResultDetail> ScannerResults { get; init; } = [];

    /// <summary>
    /// Total scan duration in milliseconds
    /// </summary>
    public required int TotalDurationMs { get; init; }

    /// <summary>
    /// Scan was skipped (e.g., file too large)
    /// </summary>
    public bool WasSkipped { get; init; }

    /// <summary>
    /// Reason scan was skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// All scanners encountered errors (fail-open scenario)
    /// </summary>
    public bool AllScannersErrored { get; init; }
}
