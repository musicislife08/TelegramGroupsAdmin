namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Spam similarity check configuration
/// </summary>
public class SimilarityConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether similarity check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Similarity threshold (0.0 - 1.0)
    /// </summary>
    public double Threshold { get; set; } = 0.5;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 75;
}
