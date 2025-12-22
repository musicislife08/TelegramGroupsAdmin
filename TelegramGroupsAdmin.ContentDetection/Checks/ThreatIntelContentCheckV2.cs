using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Spam check that validates URLs/files against threat intelligence services
/// Currently supports VirusTotal (disabled by default for URLs due to 15s latency)
/// Note: ClamAV virus scanning is handled separately by FileScanJob
/// </summary>
public partial class ThreatIntelContentCheckV2(
    ILogger<ThreatIntelContentCheckV2> logger,
    IHttpClientFactory httpClientFactory) : IContentCheckV2
{
    private static readonly Regex UrlRegex = CompiledUrlRegex();

    public CheckName CheckName => CheckName.ThreatIntel;

    /// <summary>
    /// Check if threat intel check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
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
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
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
                        logger.LogDebug("ThreatIntel check for user {UserId}: VirusTotal flagged {Url}",
                            req.UserId, url);

                        return new ContentCheckResponseV2
                        {
                            CheckName = CheckName,
                            Score = 3.0,
                            Abstained = false,
                            Details = $"VirusTotal flagged URL as malicious: {url}",
                            ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                        };
                    }
                }
            }

            logger.LogDebug("ThreatIntel check for user {UserId}: No threats found for {UrlCount} URLs",
                req.UserId, req.Urls.Count);

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"No threats detected for {req.Urls.Count} URLs",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ThreatIntel check failed for user {UserId}", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Threat intelligence check failed",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Check URL against VirusTotal
    /// </summary>
    private async Task<ThreatResult> CheckVirusTotalAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            // Use named client "VirusTotal" - will configure API key in headers
            var client = httpClientFactory.CreateClient("VirusTotal");

            // Add API key to headers
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-apikey", apiKey);

            // Base64 encode URL for VirusTotal API
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            // Step 1: Try to fetch an existing report
            var response = await client.GetAsync($"urls/{b64}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                return new ThreatResult(IsVirusTotalMalicious(json), "Existing scan result");
            }

            // Step 2: Submit the URL for scanning if not found (404)
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                return new ThreatResult(false, $"API error: {response.StatusCode}");
            }

            var submitContent = new FormUrlEncodedContent([new("url", url)]);
            var submitResponse = await client.PostAsync("urls", submitContent, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                return new ThreatResult(false, $"Submit failed: {submitResponse.StatusCode}");
            }

            // Step 3: Wait and retry (simplified - no polling)
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);

            var retryResponse = await client.GetAsync($"urls/{b64}", cancellationToken);
            if (!retryResponse.IsSuccessStatusCode)
            {
                return new ThreatResult(false, "Scan not ready");
            }

            var retryJson = await retryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return new ThreatResult(IsVirusTotalMalicious(retryJson), "New scan result");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "VirusTotal check failed for URL: {Url}", url);
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