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
