using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// File scanning check using Tier 1 local scanners (ClamAV + YARA)
/// Phase 4.17 - always_run=true (bypasses trust/admin exemptions)
/// Implements hash-based caching with 24-hour TTL
/// </summary>
public class FileScanningCheck : IContentCheck
{
    private readonly ILogger<FileScanningCheck> _logger;
    private readonly FileScanningConfig _config;
    private readonly Tier1VotingCoordinator _tier1Coordinator;
    private readonly IFileScanResultRepository _scanResultRepository;

    public string CheckName => "FileScanning";

    public FileScanningCheck(
        ILogger<FileScanningCheck> logger,
        IOptions<FileScanningConfig> config,
        Tier1VotingCoordinator tier1Coordinator,
        IFileScanResultRepository scanResultRepository)
    {
        _logger = logger;
        _config = config.Value;
        _tier1Coordinator = tier1Coordinator;
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
                req.FileBytes,
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

            // Return result based on Tier 1 voting outcome
            if (tier1Result.ThreatDetected)
            {
                var threats = tier1Result.ThreatNames ?? new List<string>();
                var detectedBy = tier1Result.DetectedBy ?? new List<string>();

                _logger.LogWarning("File scanning THREAT for user {UserId}: {Threats} detected by {Scanners}",
                    req.UserId,
                    string.Join(", ", threats),
                    string.Join(", ", detectedBy));

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Spam,  // Infected file = spam
                    Details = $"Malware detected: {string.Join(", ", threats)} by {string.Join("+", detectedBy)} ({tier1Result.TotalDurationMs}ms)",
                    Confidence = 100  // Virus scanner results are definitive
                };
            }

            // File is clean
            _logger.LogInformation("File scanning CLEAN for user {UserId} (duration: {Duration}ms)",
                req.UserId, tier1Result.TotalDurationMs);

            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = $"File clean | Scanned by {tier1Result.ScannerResults.Count} scanner(s) ({tier1Result.TotalDurationMs}ms)",
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
