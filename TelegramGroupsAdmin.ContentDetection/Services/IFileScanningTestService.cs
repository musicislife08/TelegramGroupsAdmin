using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for testing file scanning via UI
/// Wraps Tier1VotingCoordinator and provides UI-friendly results
/// </summary>
public interface IFileScanningTestService
{
    /// <summary>
    /// Scan a file and return detailed results for UI display
    /// </summary>
    /// <param name="fileBytes">File content as byte array</param>
    /// <param name="fileName">Original filename</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed scan result with hash and individual scanner results</returns>
    Task<FileScanTestResult> ScanFileAsync(
        byte[] fileBytes,
        string fileName,
        CancellationToken cancellationToken = default);
}

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
    public List<string> ThreatNames { get; init; } = new();

    /// <summary>
    /// Scanners that detected threats
    /// </summary>
    public List<string> DetectedBy { get; init; } = new();

    /// <summary>
    /// Individual scanner results
    /// </summary>
    public List<ScannerResultDetail> ScannerResults { get; init; } = new();

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
