namespace TelegramGroupsAdmin;

public sealed class GoogleSafeBrowsingService(HttpClient client) : IThreatIntelService
{
    private readonly string? _apiKey = Environment.GetEnvironmentVariable("GOOGLE_SAFE_BROWSING_API_KEY");

    public async Task<bool> IsThreatAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return false;

        var request = new
        {
            client = new { clientId = "tg-spam", clientVersion = "1.0" },
            threatInfo = new
            {
                threatTypes = new[] { "MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION" },
                platformTypes = new[] { "ANY_PLATFORM" },
                threatEntryTypes = new[] { "URL" },
                threatEntries = new[] { new { url } }
            }
        };

        var response = await client.PostAsJsonAsync($"https://safebrowsing.googleapis.com/v4/threatMatches:find?key={_apiKey}", request, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var body = await response.Content.ReadAsStringAsync(ct);
        return body.Contains("threatType", StringComparison.OrdinalIgnoreCase);
    }
}