namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Image spam detection configuration
/// </summary>
public class ImageSpamConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether image spam detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use OpenAI Vision for image analysis
    /// </summary>
    public bool UseOpenAIVision { get; set; } = true;

    /// <summary>
    /// Timeout for image analysis requests
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
