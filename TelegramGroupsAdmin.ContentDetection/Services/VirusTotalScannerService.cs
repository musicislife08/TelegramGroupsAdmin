using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// VirusTotal cloud scanner service (Tier 2)
/// Implements hash-first optimization: check hash reputation before uploading file
/// API v3: https://developers.virustotal.com/reference/overview
/// Free tier: 500 requests/day, 4 requests/minute
/// </summary>
public class VirusTotalScannerService : ICloudScannerService
{
    private readonly ILogger<VirusTotalScannerService> _logger;
    private readonly IFileScanningConfigRepository _configRepository;
    private readonly IFileScanQuotaRepository _quotaRepository;
    private readonly IHttpClientFactory _httpClientFactory;

    private const int MinEngineThreshold = 2;  // Consider malicious if >= 2 engines detect it

    public string ServiceName => "VirusTotal";

    // IsEnabled will be checked by loading config at scan time
    // TODO: Replace with IOptionsSnapshot when switching to provider pattern
    public bool IsEnabled => true;  // Actual check happens in methods after loading config

    public VirusTotalScannerService(
        ILogger<VirusTotalScannerService> logger,
        IFileScanningConfigRepository configRepository,
        IFileScanQuotaRepository quotaRepository,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configRepository = configRepository;
        _quotaRepository = quotaRepository;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CloudHashLookupResult?> LookupHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default)
    {
        // Load config from database
        var config = await _configRepository.GetAsync(chatId: null, cancellationToken);

        if (!config.Tier2.VirusTotal.Enabled)
        {
            _logger.LogDebug("VirusTotal is disabled, skipping hash lookup");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check quota before making API call
            bool quotaAvailable = await IsQuotaAvailableAsync(cancellationToken);
            if (!quotaAvailable)
            {
                _logger.LogInformation("VirusTotal daily quota exhausted, skipping hash lookup");
                return new CloudHashLookupResult
                {
                    Status = HashLookupStatus.Error,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // GET /files/{hash} - hash lookup (doesn't require file upload)
            // Use named "VirusTotal" HttpClient (configured with BaseUrl, API key, and rate limiting)
            var client = _httpClientFactory.CreateClient("VirusTotal");

            var requestUrl = $"files/{fileHash}";
            _logger.LogDebug("VirusTotal hash lookup: GET {Url}", requestUrl);

            var response = await client.GetAsync(requestUrl, cancellationToken);

            stopwatch.Stop();

            // Handle rate limiting (429)
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogInformation("VirusTotal rate limit hit (429), marking service as unavailable");
                return new CloudHashLookupResult
                {
                    Status = HashLookupStatus.Error,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Hash not found (404) = unknown file
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("VirusTotal hash lookup: File not found (must upload to scan)");

                // Increment quota (hash lookup counts as API call)
                await _quotaRepository.IncrementQuotaUsageAsync(
                    ServiceName,
                    "daily",
                    config.Tier2.VirusTotal.DailyLimit,
                    cancellationToken);

                return new CloudHashLookupResult
                {
                    Status = HashLookupStatus.Unknown,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Other errors
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("VirusTotal hash lookup failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                return new CloudHashLookupResult
                {
                    Status = HashLookupStatus.Error,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Parse response
            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonContent);
            var data = doc.RootElement.GetProperty("data");
            var attributes = data.GetProperty("attributes");
            var stats = attributes.GetProperty("last_analysis_stats");

            int malicious = stats.GetProperty("malicious").GetInt32();
            int suspicious = stats.GetProperty("suspicious").GetInt32();
            int undetected = stats.GetProperty("undetected").GetInt32();
            int harmless = stats.GetProperty("harmless").GetInt32();

            int totalEngines = malicious + suspicious + undetected + harmless;
            int detectionCount = malicious + suspicious;

            // Increment quota
            await _quotaRepository.IncrementQuotaUsageAsync(
                ServiceName,
                "daily",
                config.Tier2.VirusTotal.DailyLimit,
                cancellationToken);

            // Determine status based on detection count
            HashLookupStatus status;
            string? threatName = null;

            if (detectionCount >= MinEngineThreshold)
            {
                status = HashLookupStatus.Malicious;

                // Try to extract a representative threat name
                if (attributes.TryGetProperty("popular_threat_classification", out var threatClassification) &&
                    threatClassification.TryGetProperty("suggested_threat_label", out var threatLabel))
                {
                    threatName = threatLabel.GetString();
                }
                else if (attributes.TryGetProperty("last_analysis_results", out var results))
                {
                    // Find first malicious result
                    foreach (var engine in results.EnumerateObject())
                    {
                        if (engine.Value.TryGetProperty("category", out var category) &&
                            category.GetString() == "malicious" &&
                            engine.Value.TryGetProperty("result", out var result))
                        {
                            threatName = result.GetString();
                            break;
                        }
                    }
                }

                _logger.LogWarning("VirusTotal hash lookup: MALICIOUS - {DetectionCount}/{TotalEngines} engines detected {ThreatName}",
                    detectionCount, totalEngines, threatName ?? "unknown threat");
            }
            else
            {
                status = HashLookupStatus.Clean;
                _logger.LogInformation("VirusTotal hash lookup: CLEAN - {DetectionCount}/{TotalEngines} engines (below threshold)",
                    detectionCount, totalEngines);
            }

            return new CloudHashLookupResult
            {
                Status = status,
                ThreatName = threatName,
                DetectionCount = detectionCount,
                TotalEngines = totalEngines,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Metadata = new Dictionary<string, object>
                {
                    ["malicious"] = malicious,
                    ["suspicious"] = suspicious,
                    ["undetected"] = undetected,
                    ["harmless"] = harmless
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception during VirusTotal hash lookup");

            return new CloudHashLookupResult
            {
                Status = HashLookupStatus.Error,
                DurationMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    public async Task<CloudScanResult> ScanFileAsync(
        string filePath,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        // Load config from database
        var config = await _configRepository.GetAsync(chatId: null, cancellationToken);

        if (!config.Tier2.VirusTotal.Enabled)
        {
            return new CloudScanResult
            {
                ServiceName = ServiceName,
                IsClean = true,
                ResultType = CloudScanResultType.Error,
                ErrorMessage = "VirusTotal is disabled",
                ScanDurationMs = 0,
                QuotaConsumed = false
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get file size for logging
            var fileSize = new FileInfo(filePath).Length;

            // Check quota
            bool quotaAvailable = await IsQuotaAvailableAsync(cancellationToken);
            if (!quotaAvailable)
            {
                _logger.LogInformation("VirusTotal daily quota exhausted, cannot upload file");
                return new CloudScanResult
                {
                    ServiceName = ServiceName,
                    IsClean = true,  // Fail-open
                    ResultType = CloudScanResultType.Error,
                    ErrorMessage = "Daily quota exhausted",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                    QuotaConsumed = false
                };
            }

            // POST /files - upload file for scanning
            // Use named "VirusTotal" HttpClient (configured with BaseUrl, API key, and rate limiting)
            var client = _httpClientFactory.CreateClient("VirusTotal");

            using var content = new MultipartFormDataContent();

            // Open file stream for upload (Phase 6: streaming instead of loading full file into memory)
            await using var fileStream = File.OpenRead(filePath);
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName ?? "unknown");

            var requestUrl = "files";
            _logger.LogDebug("VirusTotal file upload: POST {Url} (size: {Size} bytes)", requestUrl, fileSize);

            var response = await client.PostAsync(requestUrl, content, cancellationToken);

            // Handle rate limiting
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                stopwatch.Stop();
                _logger.LogInformation("VirusTotal rate limit hit during file upload");

                return new CloudScanResult
                {
                    ServiceName = ServiceName,
                    IsClean = true,  // Fail-open
                    ResultType = CloudScanResultType.RateLimited,
                    ErrorMessage = "Rate limit exceeded",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                    QuotaConsumed = false
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("VirusTotal file upload failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                stopwatch.Stop();

                return new CloudScanResult
                {
                    ServiceName = ServiceName,
                    IsClean = true,  // Fail-open
                    ResultType = CloudScanResultType.Error,
                    ErrorMessage = $"Upload failed: {response.StatusCode}",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                    QuotaConsumed = false
                };
            }

            // Parse upload response to get analysis ID
            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonContent);
            var data = doc.RootElement.GetProperty("data");
            var analysisId = data.GetProperty("id").GetString();

            _logger.LogDebug("VirusTotal file uploaded, analysis ID: {AnalysisId}", analysisId);

            // Increment quota (file upload consumed quota)
            await _quotaRepository.IncrementQuotaUsageAsync(
                ServiceName,
                "daily",
                config.Tier2.VirusTotal.DailyLimit,
                cancellationToken);

            // Poll for analysis results (with timeout)
            // Be VERY conservative with API quota - each poll costs 1 API request
            // VirusTotal FREE TIER: Analysis can take 2-10+ minutes during queue backlog
            // Premium users get instant analysis, free tier is deprioritized
            // This is a background job - no user waiting, so prioritize quota conservation
            // Goal: 2-3 total requests per file (1 upload + 1-2 polls)
            const int maxPolls = 5;  // Up to 5 poll attempts
            var pollDelay = TimeSpan.FromMinutes(2);  // Wait 2 minutes between polls (10 min total timeout)

            for (int i = 0; i < maxPolls; i++)
            {
                await Task.Delay(pollDelay, cancellationToken);

                var analysisResponse = await client.GetAsync($"analyses/{analysisId}", cancellationToken);

                // Increment quota for polling request
                await _quotaRepository.IncrementQuotaUsageAsync(
                    ServiceName,
                    "daily",
                    config.Tier2.VirusTotal.DailyLimit,
                    cancellationToken);

                if (!analysisResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("VirusTotal analysis polling failed: {StatusCode}", analysisResponse.StatusCode);
                    continue;
                }

                var analysisJson = await analysisResponse.Content.ReadAsStringAsync(cancellationToken);
                using var analysisDoc = JsonDocument.Parse(analysisJson);
                var analysisData = analysisDoc.RootElement.GetProperty("data");
                var analysisAttrs = analysisData.GetProperty("attributes");
                var analysisStatus = analysisAttrs.GetProperty("status").GetString();

                if (analysisStatus == "completed")
                {
                    // Analysis complete, parse results
                    var stats = analysisAttrs.GetProperty("stats");
                    int malicious = stats.GetProperty("malicious").GetInt32();
                    int suspicious = stats.GetProperty("suspicious").GetInt32();
                    int undetected = stats.GetProperty("undetected").GetInt32();
                    int harmless = stats.GetProperty("harmless").GetInt32();

                    int totalEngines = malicious + suspicious + undetected + harmless;
                    int detectionCount = malicious + suspicious;

                    stopwatch.Stop();

                    if (detectionCount >= MinEngineThreshold)
                    {
                        // Extract threat name
                        string? threatName = null;
                        if (analysisAttrs.TryGetProperty("results", out var results))
                        {
                            foreach (var engine in results.EnumerateObject())
                            {
                                if (engine.Value.TryGetProperty("category", out var category) &&
                                    category.GetString() == "malicious" &&
                                    engine.Value.TryGetProperty("result", out var result))
                                {
                                    threatName = result.GetString();
                                    break;
                                }
                            }
                        }

                        _logger.LogWarning("VirusTotal scan: INFECTED - {DetectionCount}/{TotalEngines} engines detected {ThreatName}",
                            detectionCount, totalEngines, threatName ?? "unknown");

                        return new CloudScanResult
                        {
                            ServiceName = ServiceName,
                            IsClean = false,
                            ResultType = CloudScanResultType.Infected,
                            ThreatName = threatName,
                            DetectionCount = detectionCount,
                            TotalEngines = totalEngines,
                            ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                            QuotaConsumed = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["malicious"] = malicious,
                                ["suspicious"] = suspicious,
                                ["undetected"] = undetected,
                                ["harmless"] = harmless
                            }
                        };
                    }
                    else
                    {
                        _logger.LogInformation("VirusTotal scan: CLEAN - {DetectionCount}/{TotalEngines} engines",
                            detectionCount, totalEngines);

                        return new CloudScanResult
                        {
                            ServiceName = ServiceName,
                            IsClean = true,
                            ResultType = CloudScanResultType.Clean,
                            DetectionCount = detectionCount,
                            TotalEngines = totalEngines,
                            ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                            QuotaConsumed = true
                        };
                    }
                }

                _logger.LogDebug("VirusTotal analysis status: {Status}, polling again...", analysisStatus);
            }

            // Timeout waiting for analysis
            stopwatch.Stop();
            _logger.LogWarning("VirusTotal analysis timeout after {Polls} polls ({Duration}ms)",
                maxPolls, stopwatch.ElapsedMilliseconds);

            return new CloudScanResult
            {
                ServiceName = ServiceName,
                IsClean = true,  // Fail-open on timeout
                ResultType = CloudScanResultType.Error,
                ErrorMessage = "Analysis timeout",
                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                QuotaConsumed = true  // Quota was consumed by upload
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception during VirusTotal file scan");

            return new CloudScanResult
            {
                ServiceName = ServiceName,
                IsClean = true,  // Fail-open
                ResultType = CloudScanResultType.Error,
                ErrorMessage = ex.Message,
                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                QuotaConsumed = false
            };
        }
    }

    public async Task<bool> IsQuotaAvailableAsync(CancellationToken cancellationToken = default)
    {
        return await _quotaRepository.IsQuotaAvailableAsync(ServiceName, "daily", cancellationToken);
    }
}
