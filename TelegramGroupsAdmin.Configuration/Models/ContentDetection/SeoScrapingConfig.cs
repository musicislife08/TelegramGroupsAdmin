namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// SEO scraping configuration
/// </summary>
public class SeoScrapingConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether SEO scraping is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout for SEO scraping requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
