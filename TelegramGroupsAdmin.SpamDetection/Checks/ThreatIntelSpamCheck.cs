using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that validates URLs/files against threat intelligence services
/// Currently supports VirusTotal (disabled by default for URLs due to 15s latency)
/// TODO: Add ClamAV for local virus scanning (files/images)
/// </summary>
public partial class ThreatIntelSpamCheck : ISpamCheck
{
    private readonly ILogger<ThreatIntelSpamCheck> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly Regex UrlRegex = CompiledUrlRegex();

    public string CheckName => "ThreatIntel";

    public ThreatIntelSpamCheck(
        ILogger<ThreatIntelSpamCheck> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Check if threat intel check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
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
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequestBase request)
    {
        var req = (ThreatIntelCheckRequest)request;

        try
        {
            foreach (var url in req.Urls)
            {
                // Check VirusTotal if API key is provided
                if (!string.IsNullOrEmpty(req.VirusTotalApiKey))
                {
                    var virusTotalResult = await CheckVirusTotalAsync(url, req.VirusTotalApiKey, req.CancellationToken);
                    if (virusTotalResult.IsThreat)
                    {
                        _logger.LogDebug("ThreatIntel check for user {UserId}: VirusTotal flagged {Url}",
                            req.UserId, url);

                        return new SpamCheckResponse
                        {
                            CheckName = CheckName,
                            Result = SpamCheckResultType.Spam,
                            Details = $"VirusTotal flagged URL as malicious: {url}",
                            Confidence = req.ConfidenceThreshold
                        };
                    }
                }
            }

            _logger.LogDebug("ThreatIntel check for user {UserId}: No threats found for {UrlCount} URLs",
                req.UserId, req.Urls.Count);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = SpamCheckResultType.Clean,
                Details = $"No threats detected for {req.Urls.Count} URLs",
                Confidence = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ThreatIntel check failed for user {UserId}", req.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = SpamCheckResultType.Clean, // Fail open
                Details = "ThreatIntel check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Check URL against VirusTotal
    /// </summary>
    private async Task<ThreatResult> CheckVirusTotalAsync(string url, string apiKey, CancellationToken ct)
    {
        try
        {
            // Use named client "VirusTotal" - will configure API key in headers
            var client = _httpClientFactory.CreateClient("VirusTotal");

            // Add API key to headers
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-apikey", apiKey);

            // Base64 encode URL for VirusTotal API
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            // Step 1: Try to fetch an existing report
            var response = await client.GetAsync($"urls/{b64}", ct);
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
            var submitResponse = await client.PostAsync("urls", submitContent, ct);
            if (!submitResponse.IsSuccessStatusCode)
            {
                return new ThreatResult(false, $"Submit failed: {submitResponse.StatusCode}");
            }

            // Step 3: Wait and retry (simplified - no polling)
            await Task.Delay(TimeSpan.FromSeconds(15), ct);

            var retryResponse = await client.GetAsync($"urls/{b64}", ct);
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

    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex CompiledUrlRegex();
}

/// <summary>
/// Result of threat intelligence check
/// </summary>
internal record ThreatResult(bool IsThreat, string Details);