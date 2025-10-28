namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Stop words check configuration
/// </summary>
public class StopWordsConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether stop words check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 50;
}
