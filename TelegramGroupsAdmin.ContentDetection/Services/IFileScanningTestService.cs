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
