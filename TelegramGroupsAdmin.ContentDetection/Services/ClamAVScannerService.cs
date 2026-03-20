using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using nClam;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.Core.Telemetry;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// ClamAV file scanner service (Tier 1)
/// Connects to clamd via TCP for malware scanning
/// </summary>
public class ClamAVScannerService : IFileScannerService
{
    private readonly ILogger<ClamAVScannerService> _logger;
    private readonly ISystemConfigRepository _configRepository;

    /// <summary>
    /// Guards the one-time INFO log for env var override. Static because this service is Scoped
    /// and the "log once per process" semantic must survive across request scopes.
    /// Uses int + Interlocked.CompareExchange for atomic check-and-set (volatile bool has a
    /// benign TOCTOU race where two concurrent calls can both log).
    /// </summary>
    private static int _hasLoggedOverride;

    public string ScannerName => "ClamAV";

    public ClamAVScannerService(
        ILogger<ClamAVScannerService> logger,
        ISystemConfigRepository configRepository)
    {
        _logger = logger;
        _configRepository = configRepository;
    }

    // Helper method to get current config from database
    private async Task<FileScanningConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        return await _configRepository.GetAsync(chatId: null, cancellationToken);
    }

    /// <summary>
    /// Checks whether CLAMAV_HOST and CLAMAV_PORT env vars provide a valid override.
    /// Logs once per process lifetime on first successful override detection.
    /// </summary>
    private bool TryGetEnvVarEndpoint(out string host, out int port)
    {
        var envHost = Environment.GetEnvironmentVariable("CLAMAV_HOST");
        var envPort = Environment.GetEnvironmentVariable("CLAMAV_PORT");

        if (!string.IsNullOrWhiteSpace(envHost)
            && !string.IsNullOrWhiteSpace(envPort)
            && int.TryParse(envPort, out var parsedPort)
            && parsedPort > 0 && parsedPort <= 65535)
        {
            if (Interlocked.CompareExchange(ref _hasLoggedOverride, 1, 0) == 0)
            {
                _logger.LogInformation(
                    "ClamAV env var override active -- using {Host}:{Port} (CLAMAV_HOST / CLAMAV_PORT)",
                    envHost, parsedPort);
            }

            host = envHost;
            port = parsedPort;
            return true;
        }

        host = string.Empty;
        port = 0;
        return false;
    }

    /// <summary>
    /// Returns the effective (host, port) for ClamAV connections from an already-loaded config.
    /// Env vars take precedence; falls back to the config's host/port.
    /// Used by ScanFileAsync which already loaded config for the Enabled flag check.
    /// </summary>
    private (string host, int port) GetEffectiveEndpoint(FileScanningConfig config)
    {
        if (TryGetEnvVarEndpoint(out var host, out var port))
            return (host, port);

        return (config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
    }

    /// <summary>
    /// Async wrapper for callers that don't already have the config loaded (e.g. GetHealthAsync).
    /// Checks env vars first — skips DB entirely if override is active.
    /// </summary>
    private async Task<(string host, int port)> GetEffectiveEndpointAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetEnvVarEndpoint(out var host, out var port))
            return (host, port);

        var config = await GetConfigAsync(cancellationToken);
        return (config.Tier1.ClamAV.Host, config.Tier1.ClamAV.Port);
    }

    public async Task<FileScanResult> ScanFileAsync(
        string filePath,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = TelemetryConstants.FileScanning.StartActivity("file_scanning.clamav.scan");
        activity?.SetTag("file_scanner.tier", "tier1");
        activity?.SetTag("file_scanner.name", ScannerName);
        activity?.SetTag("file_scanner.file_name", fileName);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            // Get current config from database (needed for Enabled flag check)
            var config = await GetConfigAsync(cancellationToken);

            // Check if ClamAV is enabled
            if (!config.Tier1.ClamAV.Enabled)
            {
                _logger.LogDebug("ClamAV scanner is disabled, returning clean result");
                var disabledResult = new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,
                    ResultType = ScanResultType.Clean,
                    ScanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
                RecordScanMetrics(startTimestamp, disabledResult, activity);
                return disabledResult;
            }

            // Resolve effective endpoint once (env var override or DB config) for use in retry loop + error logs
            var (effectiveHost, effectivePort) = GetEffectiveEndpoint(config);

            // Get file size for limit checking
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;

            // ClamAV has a hard 2GB limit - skip files larger than this
            if (fileSize > FileScanningConstants.MaxClamAVSizeBytes)
            {
                _logger.LogWarning("File size ({Size} bytes) exceeds ClamAV's 2GB limit, skipping ClamAV scan (will use VirusTotal only)",
                    fileSize);

                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,  // Skip, rely on VirusTotal
                    ResultType = ScanResultType.Skipped,
                    ErrorMessage = $"File too large for ClamAV ({fileSize} bytes > 2GB limit)",
                    ScanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Read file into memory for nClam (library requires byte[])
            // This is acceptable because we've already validated size < 2GB above
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

            // Scan file bytes with retry logic for transient ClamAV failures
            // nClam library doesn't auto-reconnect, so retry on network errors
            _logger.LogDebug("Scanning file with ClamAV (size: {Size} bytes, name: {FileName})",
                fileBytes.Length, fileName ?? "unknown");

            ClamScanResult? scanResult = null;
            var maxRetries = FileScanningConstants.ClamAVMaxRetries;
            var retryDelay = TimeSpan.FromMilliseconds(FileScanningConstants.ClamAVInitialRetryDelayMs);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Ping ClamAV before scan attempt to detect connection issues early
                    var clamClient = new ClamClient(effectiveHost, effectivePort);
                    var pingResult = await clamClient.PingAsync(cancellationToken);
                    if (!pingResult)
                    {
                        if (attempt == maxRetries)
                        {
                            _logger.LogError("ClamAV daemon not responding to ping after {Attempts} attempts at {Host}:{Port}",
                                maxRetries, effectiveHost, effectivePort);

                            return new FileScanResult
                            {
                                Scanner = ScannerName,
                                IsClean = true,  // Fail-open
                                ResultType = ScanResultType.Error,
                                ErrorMessage = "ClamAV daemon not available after retries",
                                ScanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
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
                    ScanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            var scanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Process scan result
            switch (scanResult.Result)
            {
                case ClamScanResults.Clean:
                    _logger.LogDebug("ClamAV scan: File is clean (duration: {Duration}ms)",
                        scanDurationMs);

                    var cleanResult = new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,
                        ResultType = ScanResultType.Clean,
                        ScanDurationMs = scanDurationMs
                    };
                    RecordScanMetrics(startTimestamp, cleanResult, activity);
                    return cleanResult;

                case ClamScanResults.VirusDetected:
                    _logger.LogWarning("ClamAV scan: Virus detected - {VirusName} (duration: {Duration}ms)",
                        scanResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown",
                        scanDurationMs);

                    var infectedResult = new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = false,
                        ResultType = ScanResultType.Infected,
                        ThreatName = scanResult.InfectedFiles?.FirstOrDefault()?.VirusName,
                        ScanDurationMs = scanDurationMs,
                        Metadata = new Dictionary<string, object>
                        {
                            ["infected_files_count"] = scanResult.InfectedFiles?.Count ?? 0
                        }
                    };
                    RecordScanMetrics(startTimestamp, infectedResult, activity);
                    return infectedResult;

                case ClamScanResults.Error:
                    _logger.LogError("ClamAV scan error: {RawResult}", scanResult.RawResult);

                    var errorResult = new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,  // Fail-open
                        ResultType = ScanResultType.Error,
                        ErrorMessage = scanResult.RawResult,
                        ScanDurationMs = scanDurationMs
                    };
                    RecordScanMetrics(startTimestamp, errorResult, activity);
                    return errorResult;

                case ClamScanResults.Unknown:
                default:
                    _logger.LogWarning("ClamAV scan returned unknown result: {RawResult}", scanResult.RawResult);

                    var unknownResult = new FileScanResult
                    {
                        Scanner = ScannerName,
                        IsClean = true,  // Fail-open on unknown
                        ResultType = ScanResultType.Error,
                        ErrorMessage = $"Unknown scan result: {scanResult.RawResult}",
                        ScanDurationMs = scanDurationMs
                    };
                    RecordScanMetrics(startTimestamp, unknownResult, activity);
                    return unknownResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during ClamAV scan");

            var exceptionResult = new FileScanResult
            {
                Scanner = ScannerName,
                IsClean = true,  // Fail-open on exception
                ResultType = ScanResultType.Error,
                ErrorMessage = ex.Message,
                ScanDurationMs = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
            RecordScanMetrics(startTimestamp, exceptionResult, activity);
            return exceptionResult;
        }
    }

    /// <summary>
    /// Record telemetry metrics for file scan execution
    /// </summary>
    private static void RecordScanMetrics(long startTimestamp, FileScanResult result, Activity? activity)
    {
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        // Record duration histogram
        TelemetryConstants.FileScanDuration.Record(durationMs,
            new KeyValuePair<string, object?>("tier", "tier1"));

        // Record scan result counter
        var resultType = result.IsClean ? "clean" : "malicious";
        TelemetryConstants.FileScanResults.Add(1,
            new KeyValuePair<string, object?>("tier", "tier1"),
            new KeyValuePair<string, object?>("result", resultType));

        // Enrich activity with scan result details
        if (activity != null)
        {
            activity.SetTag("file_scanner.is_clean", result.IsClean);
            activity.SetTag("file_scanner.result_type", result.ResultType.ToString());
            activity.SetTag("file_scanner.duration_ms", durationMs);
            if (!string.IsNullOrEmpty(result.ThreatName))
            {
                activity.SetTag("file_scanner.threat_name", result.ThreatName);
            }
        }
    }

    /// <summary>
    /// Determine if an exception is a transient ClamAV error that should be retried
    /// </summary>
    private static bool IsTransientClamAVError(Exception ex)
    {
        // Network errors that indicate temporary connectivity issues
        return ex is SocketException or IOException or TimeoutException
            || (ex.InnerException is not null && IsTransientClamAVError(ex.InnerException));
    }

    /// <summary>
    /// Check ClamAV daemon health and get version/signature info (Phase 4.22)
    /// </summary>
    /// <returns>Health check result with version and signature count</returns>
    public async Task<FileScannerHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        string host = string.Empty;
        int port = 0;

        try
        {
            // Resolve effective endpoint (env var override or DB config)
            (host, port) = await GetEffectiveEndpointAsync(cancellationToken);
            _logger.LogDebug("ClamAV health check attempting connection to {Host}:{Port}", host, port);

            var clamClient = new ClamClient(host, port);

            // Ping ClamAV daemon
            var pingResult = await clamClient.PingAsync(cancellationToken);
            if (!pingResult)
            {
                return new FileScannerHealthResult
                {
                    IsHealthy = false,
                    Host = host,
                    Port = port,
                    ErrorMessage = $"ClamAV daemon not responding at {host}:{port}"
                };
            }

            // Get version information (includes signature count)
            var versionResult = await clamClient.GetVersionAsync(cancellationToken);

            _logger.LogInformation("ClamAV health check successful: {Version} at {Host}:{Port}",
                versionResult, host, port);

            return new FileScannerHealthResult
            {
                IsHealthy = true,
                Version = versionResult ?? "Unknown",
                Host = host,
                Port = port
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ClamAV health");
            return new FileScannerHealthResult
            {
                IsHealthy = false,
                Host = host,
                Port = port,
                ErrorMessage = ex.Message
            };
        }
    }
}
