namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// URL blocklist checking configuration
/// </summary>
public class UrlBlocklistConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether URL blocklist checking is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache duration for blocklist entries
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
