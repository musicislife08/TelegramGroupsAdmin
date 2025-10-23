using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nClam;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// ClamAV file scanner service (Tier 1)
/// Connects to clamd via TCP for malware scanning
/// </summary>
public class ClamAVScannerService : IFileScannerService
{
    private readonly ILogger<ClamAVScannerService> _logger;
    private readonly FileScanningConfig _config;
    private readonly ClamClient _clamClient;

    public string ScannerName => "ClamAV";

    public ClamAVScannerService(
        ILogger<ClamAVScannerService> logger,
        IOptions<FileScanningConfig> config)
    {
        _logger = logger;
        _config = config.Value;

        // Initialize ClamClient with configured host/port
        _clamClient = new ClamClient(
            _config.Tier1.ClamAV.Host,
            _config.Tier1.ClamAV.Port);
    }

    public async Task<FileScanResult> ScanFileAsync(
        byte[] fileBytes,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check if ClamAV is enabled
            if (!_config.Tier1.ClamAV.Enabled)
            {
                _logger.LogDebug("ClamAV scanner is disabled, returning clean result");
                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,
                    ResultType = ScanResultType.Clean,
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // ClamAV has a hard 2GB limit (2147483647 bytes) - skip files larger than this
            const long maxClamAVSize = 2147483647L; // 2 GiB - 1 byte
            if (fileBytes.Length > maxClamAVSize)
            {
                _logger.LogWarning("File size ({Size} bytes) exceeds ClamAV's 2GB limit, skipping ClamAV scan (will use VirusTotal only)",
                    fileBytes.Length);

                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,  // Skip, rely on VirusTotal
                    ResultType = ScanResultType.Skipped,
                    ErrorMessage = $"File too large for ClamAV ({fileBytes.Length} bytes > 2GB limit)",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Ping ClamAV to ensure it's available
            var pingResult = await _clamClient.PingAsync(cancellationToken);
            if (!pingResult)
            {
                _logger.LogError("ClamAV daemon is not responding to ping at {Host}:{Port}",
                    _config.Tier1.ClamAV.Host, _config.Tier1.ClamAV.Port);

                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,  // Fail-open
                    ResultType = ScanResultType.Error,
                    ErrorMessage = "ClamAV daemon not available",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Scan file bytes
            _logger.LogDebug("Scanning file with ClamAV (size: {Size} bytes, name: {FileName})",
                fileBytes.Length, fileName ?? "unknown");

            var scanResult = await _clamClient.SendAndScanFileAsync(
                fileBytes,
                cancellationToken);

            stopwatch.Stop();

            // Process scan result
            switch (scanResult.Result)
            {
                case ClamScanResults.Clean:
                    _logger.LogDebug("ClamAV scan: File is clean (duration: {Duration}ms)",
                        stopwatch.ElapsedMilliseconds);

                    return new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,
                        ResultType = ScanResultType.Clean,
                        ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                    };

                case ClamScanResults.VirusDetected:
                    _logger.LogWarning("ClamAV scan: Virus detected - {VirusName} (duration: {Duration}ms)",
                        scanResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown",
                        stopwatch.ElapsedMilliseconds);

                    return new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = false,
                        ResultType = ScanResultType.Infected,
                        ThreatName = scanResult.InfectedFiles?.FirstOrDefault()?.VirusName,
                        ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                        Metadata = new Dictionary<string, object>
                        {
                            ["infected_files_count"] = scanResult.InfectedFiles?.Count ?? 0
                        }
                    };

                case ClamScanResults.Error:
                    _logger.LogError("ClamAV scan error: {RawResult}", scanResult.RawResult);

                    return new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,  // Fail-open
                        ResultType = ScanResultType.Error,
                        ErrorMessage = scanResult.RawResult,
                        ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                    };

                case ClamScanResults.Unknown:
                default:
                    _logger.LogWarning("ClamAV scan returned unknown result: {RawResult}", scanResult.RawResult);

                    return new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,  // Fail-open on unknown
                        ResultType = ScanResultType.Error,
                        ErrorMessage = $"Unknown scan result: {scanResult.RawResult}",
                        ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                    };
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception during ClamAV scan");

            return new FileScanResult
            {
                Scanner = ScannerName,
                IsClean = true,  // Fail-open on exception
                ResultType = ScanResultType.Error,
                ErrorMessage = ex.Message,
                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Check ClamAV daemon health and get version/signature info (Phase 4.22)
    /// </summary>
    /// <returns>Health check result with version and signature count</returns>
    public async Task<ClamAVHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Ping ClamAV daemon
            var pingResult = await _clamClient.PingAsync(cancellationToken);
            if (!pingResult)
            {
                return new ClamAVHealthResult
                {
                    IsHealthy = false,
                    ErrorMessage = $"ClamAV daemon not responding at {_config.Tier1.ClamAV.Host}:{_config.Tier1.ClamAV.Port}"
                };
            }

            // Get version information (includes signature count)
            var versionResult = await _clamClient.GetVersionAsync(cancellationToken);

            return new ClamAVHealthResult
            {
                IsHealthy = true,
                Version = versionResult ?? "Unknown",
                Host = _config.Tier1.ClamAV.Host,
                Port = _config.Tier1.ClamAV.Port
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ClamAV health");
            return new ClamAVHealthResult
            {
                IsHealthy = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// ClamAV health check result (Phase 4.22)
/// </summary>
public class ClamAVHealthResult
{
    public bool IsHealthy { get; set; }
    public string? Version { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? ErrorMessage { get; set; }
}
