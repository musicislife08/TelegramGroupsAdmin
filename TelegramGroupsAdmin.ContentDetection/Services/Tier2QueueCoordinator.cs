using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Tier 2 Queue Coordinator - orchestrates cloud scanning services in priority order
/// Only executed if ALL Tier 1 scanners report clean
/// Sequential execution: try services in user-configured order until one succeeds
/// Fail-open when all services exhausted or unavailable
/// </summary>
public class Tier2QueueCoordinator
{
    private readonly ILogger<Tier2QueueCoordinator> _logger;
    private readonly IFileScanningConfigRepository _configRepository;
    private readonly Dictionary<string, ICloudScannerService> _cloudScanners;

    public Tier2QueueCoordinator(
        ILogger<Tier2QueueCoordinator> logger,
        IFileScanningConfigRepository configRepository,
        VirusTotalScannerService virusTotalScanner)
    {
        _logger = logger;
        _configRepository = configRepository;

        // Register all cloud scanners by name
        _cloudScanners = new Dictionary<string, ICloudScannerService>(StringComparer.OrdinalIgnoreCase)
        {
            ["VirusTotal"] = virusTotalScanner
        };
    }

    /// <summary>
    /// Execute Tier 2 cloud queue scan
    /// Tries services in priority order until one provides a definitive result
    /// </summary>
    public async Task<Tier2ScanResult> ScanFileAsync(
        byte[] fileBytes,
        string fileHash,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        // Load config from database
        var config = await _configRepository.GetAsync(chatId: null, cancellationToken);

        _logger.LogInformation("Starting Tier 2 cloud queue scan (file: {FileName}, hash: {Hash}, size: {Size} bytes)",
            fileName ?? "unknown", fileHash[..16] + "...", fileBytes.Length);

        var startTime = DateTimeOffset.UtcNow;
        var scanResults = new List<CloudScanResult>();
        var hashLookupResults = new List<(string ServiceName, CloudHashLookupResult Result)>();

        // Try each cloud service in configured priority order
        foreach (var serviceName in config.Tier2.CloudQueuePriority)
        {
            if (!_cloudScanners.TryGetValue(serviceName, out var scanner))
            {
                _logger.LogWarning("Unknown cloud service in priority list: {ServiceName}", serviceName);
                continue;
            }

            if (!scanner.IsEnabled)
            {
                _logger.LogDebug("Cloud service {ServiceName} is disabled, skipping", serviceName);
                continue;
            }

            _logger.LogInformation("Trying cloud service: {ServiceName}", serviceName);

            // Step 1: Try hash lookup first (if supported)
            var hashLookup = await scanner.LookupHashAsync(fileHash, cancellationToken);

            if (hashLookup != null)
            {
                hashLookupResults.Add((serviceName, hashLookup));

                switch (hashLookup.Status)
                {
                    case HashLookupStatus.Malicious:
                        // Hash is known-malicious, no need to upload file
                        _logger.LogWarning("Hash lookup: {ServiceName} reports file as MALICIOUS (hash-only, no upload)",
                            serviceName);

                        var totalDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                        return new Tier2ScanResult
                        {
                            IsClean = false,
                            ThreatDetected = true,
                            CloudScanResults = scanResults,
                            HashLookupResults = hashLookupResults,
                            TotalDurationMs = totalDuration,
                            ThreatNames = new List<string> { hashLookup.ThreatName ?? "Unknown" },
                            DetectedBy = new List<string> { serviceName },
                            DecisionSource = $"{serviceName} (hash lookup)"
                        };

                    case HashLookupStatus.Clean:
                        // Hash is known-clean, file is safe
                        _logger.LogInformation("Hash lookup: {ServiceName} reports file as CLEAN (hash-only, no upload)",
                            serviceName);

                        var cleanDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                        return new Tier2ScanResult
                        {
                            IsClean = true,
                            ThreatDetected = false,
                            CloudScanResults = scanResults,
                            HashLookupResults = hashLookupResults,
                            TotalDurationMs = cleanDuration,
                            DecisionSource = $"{serviceName} (hash lookup)"
                        };

                    case HashLookupStatus.Unknown:
                        // Hash not in database, must upload file to scan
                        _logger.LogInformation("Hash lookup: {ServiceName} reports file as UNKNOWN (will upload for scanning)",
                            serviceName);
                        break;

                    case HashLookupStatus.Error:
                        // Hash lookup failed, try file upload or skip to next service
                        _logger.LogWarning("Hash lookup: {ServiceName} encountered error, will try file upload",
                            serviceName);
                        break;
                }
            }

            // Step 2: Upload file for scanning (hash was unknown/error or service doesn't support hash lookup)
            var scanResult = await scanner.ScanFileAsync(fileBytes, fileName, cancellationToken);
            scanResults.Add(scanResult);

            switch (scanResult.ResultType)
            {
                case CloudScanResultType.Infected:
                    // File is infected, stop processing
                    _logger.LogWarning("Cloud scan: {ServiceName} detected THREAT - {ThreatName}",
                        serviceName, scanResult.ThreatName ?? "Unknown");

                    var infectedDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                    return new Tier2ScanResult
                    {
                        IsClean = false,
                        ThreatDetected = true,
                        CloudScanResults = scanResults,
                        HashLookupResults = hashLookupResults,
                        TotalDurationMs = infectedDuration,
                        ThreatNames = new List<string> { scanResult.ThreatName ?? "Unknown" },
                        DetectedBy = new List<string> { serviceName },
                        DecisionSource = $"{serviceName} (file scan)"
                    };

                case CloudScanResultType.Clean:
                    // File is clean, stop processing (we have a definitive result)
                    _logger.LogInformation("Cloud scan: {ServiceName} reports file as CLEAN",
                        serviceName);

                    var cleanFileDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                    return new Tier2ScanResult
                    {
                        IsClean = true,
                        ThreatDetected = false,
                        CloudScanResults = scanResults,
                        HashLookupResults = hashLookupResults,
                        TotalDurationMs = cleanFileDuration,
                        DecisionSource = $"{serviceName} (file scan)"
                    };

                case CloudScanResultType.Error:
                case CloudScanResultType.RateLimited:
                    // Service failed or rate-limited, try next service
                    _logger.LogWarning("Cloud scan: {ServiceName} failed ({ResultType}: {ErrorMessage}), trying next service",
                        serviceName, scanResult.ResultType, scanResult.ErrorMessage);
                    continue;
            }
        }

        // All services exhausted or failed
        var failOpenDuration = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

        if (config.Tier2.FailOpenWhenExhausted)
        {
            _logger.LogWarning("All Tier 2 cloud services exhausted/failed, failing OPEN (allowing file)");

            return new Tier2ScanResult
            {
                IsClean = true,  // Fail-open
                ThreatDetected = false,
                CloudScanResults = scanResults,
                HashLookupResults = hashLookupResults,
                TotalDurationMs = failOpenDuration,
                AllServicesExhausted = true,
                DecisionSource = "fail-open (all services exhausted)"
            };
        }
        else
        {
            _logger.LogWarning("All Tier 2 cloud services exhausted/failed, failing CLOSE (blocking file)");

            return new Tier2ScanResult
            {
                IsClean = false,  // Fail-close
                ThreatDetected = true,
                CloudScanResults = scanResults,
                HashLookupResults = hashLookupResults,
                TotalDurationMs = failOpenDuration,
                AllServicesExhausted = true,
                ThreatNames = new List<string> { "All cloud services unavailable" },
                DecisionSource = "fail-close (all services exhausted)"
            };
        }
    }
}
