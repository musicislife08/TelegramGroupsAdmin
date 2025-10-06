using System.Text.Json;

namespace TgSpam_PreFilterApi;

public class VirusTotalService(HttpClient client) : IThreatIntelService
{
    public async Task<bool> IsThreatAsync(string url, CancellationToken ct = default)
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        // Step 1: Try to fetch an existing report
        var response = await client.GetAsync($"urls/{b64}", ct);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return IsMalicious(json);
        }

        // Step 2: Submit the URL for scanning if not found (404)
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            return false;

        var content = new FormUrlEncodedContent([
            new("url", url)
        ]);

        var submitResponse = await client.PostAsync("urls", content, ct);
        if (!submitResponse.IsSuccessStatusCode)
            return false;

        // Step 3: Wait a fixed amount of time (instead of polling)
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        // Step 4: Try fetching the report again
        var retryResponse = await client.GetAsync($"urls/{b64}", ct);
        if (!retryResponse.IsSuccessStatusCode)
            return false;

        var retryJson = await retryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return IsMalicious(retryJson);
    }

    private static bool IsMalicious(JsonElement report)
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
}