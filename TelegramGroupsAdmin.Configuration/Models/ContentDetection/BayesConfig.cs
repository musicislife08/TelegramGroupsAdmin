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
    /// Minimum spam probability to trigger (0.0 - 100.0)
    /// </summary>
    public double MinSpamProbability { get; set; } = 50.0;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 75;

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
