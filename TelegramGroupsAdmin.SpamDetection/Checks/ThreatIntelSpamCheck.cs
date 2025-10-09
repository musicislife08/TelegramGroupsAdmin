using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that validates URLs against threat intelligence services (VirusTotal, Google Safe Browsing)
/// Based on existing VirusTotalService and GoogleSafeBrowsingService implementations
/// </summary>
public partial class ThreatIntelSpamCheck : ISpamCheck
{
    private readonly ILogger<ThreatIntelSpamCheck> _logger;
    private readonly SpamDetectionConfig _config;
    private readonly HttpClient _httpClient;

    private static readonly Regex UrlRegex = CompiledUrlRegex();

    public string CheckName => "ThreatIntel";

    public ThreatIntelSpamCheck(
        ILogger<ThreatIntelSpamCheck> logger,
        SpamDetectionConfig config,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClientFactory.CreateClient();

        // Configure HTTP client
        _httpClient.Timeout = _config.ThreatIntel.Timeout;
    }

    /// <summary>
    /// Check if threat intel check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Check if threat intel check is enabled
        if (!_config.ThreatIntel.Enabled)
        {
            return false;
        }

        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Only run if message contains URLs
        return UrlRegex.IsMatch(request.Message);
    }

    /// <summary>
    /// Execute threat intelligence spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var urls = ExtractUrls(request.Message);

            foreach (var url in urls)
            {
                // Check VirusTotal if enabled
                if (_config.ThreatIntel.UseVirusTotal)
                {
                    var virusTotalResult = await CheckVirusTotalAsync(url, cancellationToken);
                    if (virusTotalResult.IsThreat)
                    {
                        _logger.LogDebug("ThreatIntel check for user {UserId}: VirusTotal flagged {Url}",
                            request.UserId, url);

                        return new SpamCheckResponse
                        {
                            CheckName = CheckName,
                            IsSpam = true,
                            Details = $"VirusTotal flagged URL as malicious: {url}",
                            Confidence = 90
                        };
                    }
                }

                // Check Google Safe Browsing if enabled
                if (_config.ThreatIntel.UseGoogleSafeBrowsing)
                {
                    var safeBrowsingResult = await CheckGoogleSafeBrowsingAsync(url, cancellationToken);
                    if (safeBrowsingResult.IsThreat)
                    {
                        _logger.LogDebug("ThreatIntel check for user {UserId}: Google Safe Browsing flagged {Url}",
                            request.UserId, url);

                        return new SpamCheckResponse
                        {
                            CheckName = CheckName,
                            IsSpam = true,
                            Details = $"Google Safe Browsing flagged URL as unsafe: {url}",
                            Confidence = 85
                        };
                    }
                }
            }

            _logger.LogDebug("ThreatIntel check for user {UserId}: No threats found for {UrlCount} URLs",
                request.UserId, urls.Count);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = $"No threats detected for {urls.Count} URLs",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ThreatIntel check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "ThreatIntel check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Check URL against VirusTotal
    /// </summary>
    private async Task<ThreatResult> CheckVirusTotalAsync(string url, CancellationToken ct)
    {
        try
        {
            var apiKey = Environment.GetEnvironmentVariable("VIRUSTOTAL__APIKEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogDebug("VirusTotal API key not configured, skipping check");
                return new ThreatResult(false, "API key not configured");
            }

            // Base64 encode URL for VirusTotal API
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-apikey", apiKey);

            // Step 1: Try to fetch an existing report
            var response = await _httpClient.GetAsync($"https://www.virustotal.com/api/v3/urls/{b64}", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                return new ThreatResult(IsVirusTotalMalicious(json), "Existing scan result");
            }

            // Step 2: Submit the URL for scanning if not found (404)
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                return new ThreatResult(false, $"API error: {response.StatusCode}");
            }

            var submitContent = new FormUrlEncodedContent([new("url", url)]);
            var submitResponse = await _httpClient.PostAsync("https://www.virustotal.com/api/v3/urls", submitContent, ct);
            if (!submitResponse.IsSuccessStatusCode)
            {
                return new ThreatResult(false, $"Submit failed: {submitResponse.StatusCode}");
            }

            // Step 3: Wait and retry (simplified - no polling)
            await Task.Delay(TimeSpan.FromSeconds(15), ct);

            var retryResponse = await _httpClient.GetAsync($"https://www.virustotal.com/api/v3/urls/{b64}", ct);
            if (!retryResponse.IsSuccessStatusCode)
            {
                return new ThreatResult(false, "Scan not ready");
            }

            var retryJson = await retryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return new ThreatResult(IsVirusTotalMalicious(retryJson), "New scan result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VirusTotal check failed for URL: {Url}", url);
            return new ThreatResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check URL against Google Safe Browsing
    /// </summary>
    private async Task<ThreatResult> CheckGoogleSafeBrowsingAsync(string url, CancellationToken ct)
    {
        try
        {
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_SAFE_BROWSING_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogDebug("Google Safe Browsing API key not configured, skipping check");
                return new ThreatResult(false, "API key not configured");
            }

            var request = new
            {
                client = new { clientId = "TelegramGroupsAdmin", clientVersion = "1.0" },
                threatInfo = new
                {
                    threatTypes = new[] { "MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION" },
                    platformTypes = new[] { "ANY_PLATFORM" },
                    threatEntryTypes = new[] { "URL" },
                    threatEntries = new[] { new { url } }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"https://safebrowsing.googleapis.com/v4/threatMatches:find?key={apiKey}",
                request,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ThreatResult(false, $"API error: {response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var isThreat = body.Contains("threatType", StringComparison.OrdinalIgnoreCase);

            return new ThreatResult(isThreat, isThreat ? "Threat detected" : "No threat");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Safe Browsing check failed for URL: {Url}", url);
            return new ThreatResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse VirusTotal response to determine if URL is malicious
    /// </summary>
    private static bool IsVirusTotalMalicious(JsonElement report)
    {
        if (report.TryGetProperty("data", out var data) &&
            data.TryGetProperty("attributes", out var attributes) &&
            attributes.TryGetProperty("last_analysis_stats", out var stats) &&
            stats.TryGetProperty("malicious", out var malicious))
        {
            return malicious.GetInt32() > 0;
        }

        return false;
    }

    /// <summary>
    /// Extract URLs from message text
    /// </summary>
    private static List<string> ExtractUrls(string message)
    {
        var urls = new List<string>();

        foreach (Match match in UrlRegex.Matches(message))
        {
            urls.Add(match.Value);
        }

        return urls;
    }

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledUrlRegex();
}

/// <summary>
/// Result of threat intelligence check
/// </summary>
internal record ThreatResult(bool IsThreat, string Details);