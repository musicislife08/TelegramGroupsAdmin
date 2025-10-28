namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Threat intelligence configuration
/// </summary>
public class ThreatIntelConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether threat intelligence checking is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use VirusTotal (disabled by default - slow for URL checks, better for file scanning)
    /// </summary>
    public bool UseVirusTotal { get; set; } = false;

    /// <summary>
    /// Timeout for threat intel API calls
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
