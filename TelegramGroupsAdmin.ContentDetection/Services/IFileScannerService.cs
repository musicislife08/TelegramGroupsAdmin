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
    /// Phase 6: Updated to accept file path instead of byte array for large file support
    /// Scanners open their own streams to enable parallel scanning without memory duplication
    /// </summary>
    /// <param name="filePath">Path to the file to scan</param>
    /// <param name="fileName">Original filename (optional, for context)</param>
    /// <param name="cancellationToken">Cancellation token for timeout handling</param>
    /// <returns>Scan result with threat detection status</returns>
    Task<FileScanResult> ScanFileAsync(string filePath, string? fileName = null, CancellationToken cancellationToken = default);
}
