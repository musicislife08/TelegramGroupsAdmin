using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// File scanning check using two-tier architecture:
/// - Tier 1: Local scanners (ClamAV) run in parallel with OR voting
/// - Tier 2: Cloud services (e.g., VirusTotal) run sequentially in priority order (only if Tier 1 reports clean)
/// Phase 4.17 - always_run=true (bypasses trust/admin exemptions)
/// Implements hash-based caching with 24-hour TTL
/// </summary>
public class FileScanningCheck : IContentCheck
{
    private readonly ILogger<FileScanningCheck> _logger;
    private readonly FileScanningConfig _config;
    private readonly Tier1VotingCoordinator _tier1Coordinator;
    private readonly Tier2QueueCoordinator _tier2Coordinator;
    private readonly IFileScanResultRepository _scanResultRepository;

    public CheckName CheckName => CheckName.FileScanning;

    public FileScanningCheck(
        ILogger<FileScanningCheck> logger,
        IOptions<FileScanningConfig> config,
        Tier1VotingCoordinator tier1Coordinator,
        Tier2QueueCoordinator tier2Coordinator,
        IFileScanResultRepository scanResultRepository)
    {
        _logger = logger;
        _config = config.Value;
        _tier1Coordinator = tier1Coordinator;
        _tier2Coordinator = tier2Coordinator;
        _scanResultRepository = scanResultRepository;
    }

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip for now - this check will be called directly with FileScanCheckRequest
        // when MessageProcessingService detects a file attachment
        // TODO: Integrate with ContentDetectionEngine for automatic file detection
        return false;
    }

    public async Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (FileScanCheckRequest)request;

        try
        {
            // Check file size limit
            if (req.FileSize > _config.General.MaxFileSizeBytes)
            {
                _logger.LogWarning("File exceeds size limit for user {UserId}: {Size} > {Limit} bytes",
                    req.UserId, req.FileSize, _config.General.MaxFileSizeBytes);

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,  // Fail-open for oversized files
                    Details = $"File size {req.FileSize} exceeds limit {_config.General.MaxFileSizeBytes} (fail-open)",
                    Confidence = 0  // No confidence - file not scanned
                };
            }

            // Check cache first (24-hour TTL)
            var cachedResults = await _scanResultRepository.GetCachedResultsByHashAsync(
                req.FileHash,
                req.CancellationToken);

            if (cachedResults.Any())
            {
                _logger.LogInformation("Cache HIT for file hash {FileHash}: {Count} cached results found",
                    req.FileHash, cachedResults.Count);

                // Check if any cached scanner detected a threat
                var threatDetected = cachedResults.Any(r => r.Result == "Infected" && r.ThreatName != null);

                if (threatDetected)
                {
                    var threats = cachedResults
                        .Where(r => r.Result == "Infected")
                        .Select(r => $"{r.Scanner}:{r.ThreatName}")
                        .ToList();

                    return new ContentCheckResponse
                    {
                        CheckName = CheckName,
                        Result = CheckResultType.Spam,  // Infected file = spam
                        Details = $"Malware detected (cached): {string.Join(", ", threats)} | Hash: {req.FileHash}",
                        Confidence = 100  // Virus scanner results are definitive
                    };
                }

                // All cached results were clean
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = $"File clean (cached) | Hash: {req.FileHash}",
                    Confidence = 100  // Virus scanner results are definitive
                };
            }

            // Cache MISS - perform fresh Tier 1 scan
            _logger.LogInformation("Cache MISS for file hash {FileHash}, performing Tier 1 scan (size: {Size} bytes)",
                req.FileHash, req.FileSize);

            var tier1Result = await _tier1Coordinator.ScanFileAsync(
                req.FilePath,
                req.FileSize,
                req.FileName,
                req.CancellationToken);

            // Cache all scanner results
            foreach (var scannerResult in tier1Result.ScannerResults)
            {
                await _scanResultRepository.AddScanResultAsync(
                    new FileScanResultModel(
                        Id: 0,  // Will be set by database
                        FileHash: req.FileHash,
                        Scanner: scannerResult.Scanner,
                        Result: scannerResult.ResultType.ToString(),
                        ThreatName: scannerResult.ThreatName,
                        ScanDurationMs: scannerResult.ScanDurationMs,
                        ScannedAt: DateTimeOffset.UtcNow,
                        MetadataJson: scannerResult.Metadata != null
                            ? System.Text.Json.JsonSerializer.Serialize(scannerResult.Metadata)
                            : null
                    ),
                    req.CancellationToken);
            }

            // Tier 1 detected threat - return immediately (no need for Tier 2)
            if (tier1Result.ThreatDetected)
            {
                var threats = tier1Result.ThreatNames ?? [];
                var detectedBy = tier1Result.DetectedBy ?? [];

                _logger.LogWarning("File scanning THREAT for user {UserId}: {Threats} detected by {Scanners} (Tier 1)",
                    req.UserId,
                    string.Join(", ", threats),
                    string.Join(", ", detectedBy));

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Spam,  // Infected file = spam
                    Details = $"Malware detected: {string.Join(", ", threats)} by {string.Join("+", detectedBy)} (Tier 1, {tier1Result.TotalDurationMs}ms)",
                    Confidence = 100  // Virus scanner results are definitive
                };
            }

            // Tier 1 reported clean - proceed to Tier 2 cloud queue
            _logger.LogInformation("Tier 1 scan CLEAN for user {UserId}, proceeding to Tier 2 cloud queue",
                req.UserId);

            var tier2Result = await _tier2Coordinator.ScanFileAsync(
                req.FilePath,
                req.FileSize,
                req.FileHash,
                req.FileName,
                req.CancellationToken);

            // Cache Tier 2 cloud scan results
            foreach (var cloudScanResult in tier2Result.CloudScanResults)
            {
                await _scanResultRepository.AddScanResultAsync(
                    new FileScanResultModel(
                        Id: 0,
                        FileHash: req.FileHash,
                        Scanner: cloudScanResult.ServiceName,
                        Result: cloudScanResult.ResultType.ToString(),
                        ThreatName: cloudScanResult.ThreatName,
                        ScanDurationMs: cloudScanResult.ScanDurationMs,
                        ScannedAt: DateTimeOffset.UtcNow,
                        MetadataJson: cloudScanResult.Metadata != null
                            ? System.Text.Json.JsonSerializer.Serialize(cloudScanResult.Metadata)
                            : null
                    ),
                    req.CancellationToken);
            }

            // Cache hash lookup results (didn't require file upload, so store separately)
            foreach (var (serviceName, hashLookup) in tier2Result.HashLookupResults)
            {
                await _scanResultRepository.AddScanResultAsync(
                    new FileScanResultModel(
                        Id: 0,
                        FileHash: req.FileHash,
                        Scanner: $"{serviceName} (hash lookup)",
                        Result: hashLookup.Status.ToString(),
                        ThreatName: hashLookup.ThreatName,
                        ScanDurationMs: hashLookup.DurationMs,
                        ScannedAt: DateTimeOffset.UtcNow,
                        MetadataJson: hashLookup.Metadata != null
                            ? System.Text.Json.JsonSerializer.Serialize(hashLookup.Metadata)
                            : null
                    ),
                    req.CancellationToken);
            }

            // Return final result based on Tier 2 outcome
            if (tier2Result.ThreatDetected)
            {
                var threats = tier2Result.ThreatNames ?? [];
                var detectedBy = tier2Result.DetectedBy ?? [];

                _logger.LogWarning("File scanning THREAT for user {UserId}: {Threats} detected by {Scanners} (Tier 2)",
                    req.UserId,
                    string.Join(", ", threats),
                    string.Join(", ", detectedBy));

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Spam,  // Infected file = spam
                    Details = $"Malware detected: {string.Join(", ", threats)} by {string.Join("+", detectedBy)} (Tier 2, {tier2Result.TotalDurationMs}ms)",
                    Confidence = 100  // Virus scanner results are definitive
                };
            }

            // File is clean (both Tier 1 and Tier 2)
            int totalDuration = tier1Result.TotalDurationMs + tier2Result.TotalDurationMs;
            _logger.LogInformation("File scanning CLEAN for user {UserId} (Tier 1 + Tier 2, total: {Duration}ms)",
                req.UserId, totalDuration);

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = $"File clean | Tier 1: {tier1Result.ScannerResults.Count} scanner(s), Tier 2: {tier2Result.CloudScanResults.Count} service(s) ({totalDuration}ms)",
                Confidence = 100  // Virus scanner results are definitive
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during file scanning for user {UserId}", req.UserId);

            // Fail-open: allow file through on infrastructure errors
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,  // Fail-open - no Error type available
                Details = $"Scan error (fail-open): {ex.Message}",
                Confidence = 0  // No confidence - scan failed
            };
        }
    }
}
