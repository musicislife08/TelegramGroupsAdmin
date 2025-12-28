namespace TelegramGroupsAdmin.Ui.Api;

/// <summary>
/// Named HttpClient constants for IHttpClientFactory.
/// Ensures consistent client names across the WASM application.
/// </summary>
public static class HttpClientNames
{
    /// <summary>
    /// Main API client configured with the application's base address.
    /// Automatically includes cookies for same-origin requests.
    /// </summary>
    public const string Api = "Api";
}
