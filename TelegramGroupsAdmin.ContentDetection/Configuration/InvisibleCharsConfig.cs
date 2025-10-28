namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Invisible character detection configuration
/// </summary>
public class InvisibleCharsConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether invisible character detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}
