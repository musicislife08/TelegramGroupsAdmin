namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Abnormal spacing detection configuration
/// </summary>
public class SpacingConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether spacing check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum number of words required for spacing analysis
    /// </summary>
    public int MinWordsCount { get; set; } = 5;

    /// <summary>
    /// Maximum length for "short" words
    /// </summary>
    public int ShortWordLength { get; set; } = 3;

    /// <summary>
    /// Threshold for short word ratio (0.0 - 1.0)
    /// </summary>
    public double ShortWordRatioThreshold { get; set; } = 0.7;

    /// <summary>
    /// Threshold for space ratio (0.0 - 1.0)
    /// </summary>
    public double SpaceRatioThreshold { get; set; } = 0.3;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 70;
}
