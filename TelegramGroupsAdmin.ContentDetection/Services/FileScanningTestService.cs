using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Implementation of file scanning test service for UI
/// Wraps Tier1VotingCoordinator and provides SHA256 hashing
/// </summary>
public class FileScanningTestService : IFileScanningTestService
{
    private readonly ILogger<FileScanningTestService> _logger;
    private readonly Tier1VotingCoordinator _tier1Coordinator;

    public FileScanningTestService(
        ILogger<FileScanningTestService> logger,
        Tier1VotingCoordinator tier1Coordinator)
    {
        _logger = logger;
        _tier1Coordinator = tier1Coordinator;
    }

    public async Task<FileScanTestResult> ScanFileAsync(
        byte[] fileBytes,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing file scan: {FileName} ({Size} bytes)", fileName, fileBytes.Length);

        // Calculate SHA256 hash
        var fileHash = CalculateSHA256(fileBytes);
        _logger.LogDebug("File hash: {Hash}", fileHash);

        // Run Tier 1 scan
        var tier1Result = await _tier1Coordinator.ScanFileAsync(fileBytes, fileName, cancellationToken)
            .ConfigureAwait(false);

        // Map to UI-friendly result
        var result = new FileScanTestResult
        {
            FileHash = fileHash,
            IsInfected = tier1Result.ThreatDetected,
            IsClean = tier1Result.IsClean,
            PrimaryThreatName = tier1Result.ThreatNames?.FirstOrDefault(),
            ThreatNames = tier1Result.ThreatNames ?? new List<string>(),
            DetectedBy = tier1Result.DetectedBy ?? new List<string>(),
            ScannerResults = tier1Result.ScannerResults
                .Select(sr => new ScannerResultDetail
                {
                    ScannerName = sr.Scanner,
                    ResultType = sr.ResultType,
                    IsClean = sr.IsClean,
                    ThreatName = sr.ThreatName,
                    ScanDurationMs = sr.ScanDurationMs,
                    ErrorMessage = sr.ErrorMessage,
                    Metadata = sr.Metadata
                })
                .ToList(),
            TotalDurationMs = tier1Result.TotalDurationMs,
            WasSkipped = !string.IsNullOrEmpty(tier1Result.SkippedReason),
            SkipReason = tier1Result.SkippedReason,
            AllScannersErrored = tier1Result.AllScannersErrored
        };

        _logger.LogInformation(
            "File scan test complete: {FileName} - {Result} (hash: {Hash}, duration: {Duration}ms)",
            fileName,
            result.IsInfected ? "INFECTED" : "CLEAN",
            fileHash,
            result.TotalDurationMs);

        return result;
    }

    /// <summary>
    /// Calculate SHA256 hash of file bytes
    /// </summary>
    private static string CalculateSHA256(byte[] fileBytes)
    {
        var hashBytes = SHA256.HashData(fileBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
