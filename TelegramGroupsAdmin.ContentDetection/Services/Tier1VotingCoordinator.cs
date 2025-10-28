using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Tier 1 Voting Coordinator - runs local scanners in parallel
/// Voting Logic: ANY scanner detecting threat = file is infected (OR logic)
/// Scanners: ClamAV (+ optional Windows AMSI)
/// Note: YARA was removed - ClamAV provides superior coverage with 10M+ signatures
/// </summary>
public class Tier1VotingCoordinator
{
    private readonly ILogger<Tier1VotingCoordinator> _logger;
    private readonly FileScanningConfig _config;
    private readonly ClamAVScannerService _clamAvScanner;

    public Tier1VotingCoordinator(
        ILogger<Tier1VotingCoordinator> logger,
        IOptions<FileScanningConfig> config,
        ClamAVScannerService clamAvScanner)
    {
        _logger = logger;
        _config = config.Value;
        _clamAvScanner = clamAvScanner;
    }

    /// <summary>
    /// Scan file with all Tier 1 scanners in parallel
    /// Returns aggregated result with OR voting logic
    /// </summary>
    public async Task<Tier1ScanResult> ScanFileAsync(
        byte[] fileBytes,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Tier 1 scan (file: {FileName}, size: {Size} bytes)",
            fileName ?? "unknown", fileBytes.Length);

        // Check file size limit
        if (fileBytes.Length > _config.General.MaxFileSizeBytes)
        {
            _logger.LogWarning("File exceeds size limit ({Size} > {Limit}), skipping scan",
                fileBytes.Length, _config.General.MaxFileSizeBytes);

            return new Tier1ScanResult
            {
                IsClean = true,  // Fail-open for oversized files
                ThreatDetected = false,
                ScannerResults = new List<FileScanResult>(),
                TotalDurationMs = 0,
                SkippedReason = $"File size {fileBytes.Length} exceeds limit {_config.General.MaxFileSizeBytes}"
            };
        }

        var scanTasks = new List<Task<FileScanResult>>();

        // Launch ClamAV scan
        if (_config.Tier1.ClamAV.Enabled)
        {
            scanTasks.Add(_clamAvScanner.ScanFileAsync(fileBytes, fileName, cancellationToken));
        }

        // TODO: Add Windows AMSI scanner when implemented (Phase 3)
        // if (_config.Tier1.WindowsAmsi.Enabled)
        // {
        //     scanTasks.Add(_windowsAmsiScanner.ScanFileAsync(fileBytes, fileName, cancellationToken));
        // }

        if (!scanTasks.Any())
        {
            _logger.LogWarning("No Tier 1 scanners are enabled!");
            return new Tier1ScanResult
            {
                IsClean = true,  // Fail-open when no scanners
                ThreatDetected = false,
                ScannerResults = new List<FileScanResult>(),
                TotalDurationMs = 0,
                SkippedReason = "No scanners enabled"
            };
        }

        // Run all scanners in parallel
        var startTime = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(scanTasks);
        var totalDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

        // Apply OR voting logic: ANY scanner detecting threat = infected
        var threatsDetected = results.Where(r => r.ResultType == ScanResultType.Infected && !r.IsClean).ToList();
        var cleanResults = results.Where(r => r.ResultType == ScanResultType.Clean && r.IsClean).ToList();
        var errorResults = results.Where(r => r.ResultType == ScanResultType.Error).ToList();

        bool isThreatDetected = threatsDetected.Any();
        bool allScannersErrored = results.All(r => r.ResultType == ScanResultType.Error);

        if (isThreatDetected)
        {
            var threatNames = string.Join(", ", threatsDetected.Select(t => $"{t.Scanner}:{t.ThreatName}"));
            _logger.LogWarning("Tier 1 THREAT DETECTED by {Count} scanner(s): {Threats} (duration: {Duration}ms)",
                threatsDetected.Count, threatNames, totalDuration);

            return new Tier1ScanResult
            {
                IsClean = false,
                ThreatDetected = true,
                ScannerResults = results.ToList(),
                TotalDurationMs = totalDuration,
                ThreatNames = threatsDetected.Select(t => t.ThreatName ?? "Unknown").ToList(),
                DetectedBy = threatsDetected.Select(t => t.Scanner).ToList()
            };
        }

        if (allScannersErrored)
        {
            _logger.LogError("All Tier 1 scanners encountered errors, failing open (duration: {Duration}ms)", totalDuration);

            return new Tier1ScanResult
            {
                IsClean = true,  // Fail-open when all scanners error
                ThreatDetected = false,
                ScannerResults = results.ToList(),
                TotalDurationMs = totalDuration,
                AllScannersErrored = true
            };
        }

        // At least one scanner returned clean, no threats detected
        _logger.LogInformation("Tier 1 scan complete: CLEAN ({CleanCount} clean, {ErrorCount} errors, duration: {Duration}ms)",
            cleanResults.Count, errorResults.Count, totalDuration);

        return new Tier1ScanResult
        {
            IsClean = true,
            ThreatDetected = false,
            ScannerResults = results.ToList(),
            TotalDurationMs = totalDuration
        };
    }
}
