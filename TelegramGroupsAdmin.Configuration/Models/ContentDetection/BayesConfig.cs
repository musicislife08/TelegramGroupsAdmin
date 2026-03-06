namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// Naive Bayes classifier configuration
/// </summary>
public class BayesConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether Bayes check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Confidence threshold for spam classification (0.0-5.0)
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 3.75;

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
