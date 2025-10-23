namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Application-level configuration options
/// </summary>
public class AppOptions
{
    /// <summary>
    /// Base directory for all application data (images, media, encryption keys, etc.)
    /// Default: /data (for Docker) or ./data (for local development)
    /// Subdirectories: images/, media/, keys/
    /// </summary>
    public string DataPath { get; set; } = "/data";

    /// <summary>
    /// Base URL for the application (e.g., "https://yourdomain.com" or "http://localhost:5161")
    /// Used for constructing links in emails (verification, password reset, etc.)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5161";
}
