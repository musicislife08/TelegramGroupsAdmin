namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// CAS (Combot Anti-Spam) configuration
/// </summary>
public class CasConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether CAS check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CAS API URL
    /// </summary>
    public string ApiUrl { get; set; } = "https://api.cas.chat";

    /// <summary>
    /// HTTP timeout for CAS requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// User-Agent header for CAS requests
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
