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
    private readonly IOptionsMonitor<FileScanningConfig> _configMonitor;

    public string ScannerName => "ClamAV";

    public ClamAVScannerService(
        ILogger<ClamAVScannerService> logger,
        IOptionsMonitor<FileScanningConfig> configMonitor)
    {
        _logger = logger;
        _configMonitor = configMonitor;
    }

    // Helper property to get current config (supports hot-reload)
    private FileScanningConfig Config => _configMonitor.CurrentValue;

    // Helper method to create ClamClient with current config values
    private ClamClient CreateClamClient()
    {
        var config = Config;
        return new ClamClient(
            config.Tier1.ClamAV.Host,
            config.Tier1.ClamAV.Port);
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
            if (!Config.Tier1.ClamAV.Enabled)
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

            // Scan file bytes with retry logic for transient ClamAV failures
            // nClam library doesn't auto-reconnect, so retry on network errors
            _logger.LogDebug("Scanning file with ClamAV (size: {Size} bytes, name: {FileName})",
                fileBytes.Length, fileName ?? "unknown");

            ClamScanResult? scanResult = null;
            var maxRetries = 3;
            var retryDelay = TimeSpan.FromMilliseconds(500); // Start with 500ms

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Ping ClamAV before scan attempt to detect connection issues early
                    var clamClient = CreateClamClient();
                    var pingResult = await clamClient.PingAsync(cancellationToken);
                    if (!pingResult)
                    {
                        if (attempt == maxRetries)
                        {
                            _logger.LogError("ClamAV daemon not responding to ping after {Attempts} attempts at {Host}:{Port}",
                                maxRetries, Config.Tier1.ClamAV.Host, Config.Tier1.ClamAV.Port);

                            return new FileScanResult
                            {
                                Scanner = ScannerName,
                                IsClean = true,  // Fail-open
                                ResultType = ScanResultType.Error,
                                ErrorMessage = "ClamAV daemon not available after retries",
                                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                            };
                        }

                        _logger.LogWarning("ClamAV ping failed on attempt {Attempt}/{MaxAttempts}, retrying in {Delay}ms",
                            attempt, maxRetries, retryDelay.TotalMilliseconds);
                        await Task.Delay(retryDelay, cancellationToken);
                        retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
                        continue;
                    }

                    // Perform the actual scan
                    scanResult = await clamClient.SendAndScanFileAsync(fileBytes, cancellationToken);
                    break; // Success, exit retry loop
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransientClamAVError(ex))
                {
                    _logger.LogWarning(ex,
                        "Transient ClamAV error on attempt {Attempt}/{MaxAttempts}, retrying in {Delay}ms",
                        attempt, maxRetries, retryDelay.TotalMilliseconds);
                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff

                    if (attempt == maxRetries)
                        throw; // Re-throw on final attempt to hit outer catch
                }
            }

            // If we get here and scanResult is null, all retries failed (shouldn't happen due to throw above)
            if (scanResult == null)
            {
                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,  // Fail-open
                    ResultType = ScanResultType.Error,
                    ErrorMessage = "All scan attempts failed",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

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
    /// Determine if an exception is a transient ClamAV error that should be retried
    /// </summary>
    private static bool IsTransientClamAVError(Exception ex)
    {
        // Network errors that indicate temporary connectivity issues
        return ex is System.Net.Sockets.SocketException
            || ex is System.IO.IOException
            || ex is TimeoutException
            || (ex.InnerException != null && IsTransientClamAVError(ex.InnerException));
    }

    /// <summary>
    /// Check ClamAV daemon health and get version/signature info (Phase 4.22)
    /// </summary>
    /// <returns>Health check result with version and signature count</returns>
    public async Task<ClamAVHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var clamClient = CreateClamClient();
            var config = Config;

            // Ping ClamAV daemon
            var pingResult = await clamClient.PingAsync(cancellationToken);
            if (!pingResult)
            {
                return new ClamAVHealthResult
                {
                    IsHealthy = false,
                    ErrorMessage = $"ClamAV daemon not responding at {config.Tier1.ClamAV.Host}:{config.Tier1.ClamAV.Port}"
                };
            }

            // Get version information (includes signature count)
            var versionResult = await clamClient.GetVersionAsync(cancellationToken);

            return new ClamAVHealthResult
            {
                IsHealthy = true,
                Version = versionResult ?? "Unknown",
                Host = config.Tier1.ClamAV.Host,
                Port = config.Tier1.ClamAV.Port
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
