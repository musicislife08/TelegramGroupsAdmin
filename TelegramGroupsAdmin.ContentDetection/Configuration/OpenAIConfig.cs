namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// OpenAI integration configuration
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether OpenAI check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether OpenAI runs in veto mode (confirm spam) or enhancement mode (find spam)
    /// </summary>
    public bool VetoMode { get; set; } = true;

    /// <summary>
    /// Confidence threshold for triggering OpenAI veto (0-100)
    /// OpenAI veto only runs if spam is detected with confidence below this threshold
    /// Higher values = more vetos (more conservative), Lower values = fewer vetos (more aggressive)
    /// </summary>
    public int VetoThreshold { get; set; } = 95;

    /// <summary>
    /// Whether to check short messages with OpenAI
    /// </summary>
    public bool CheckShortMessages { get; set; } = false;

    /// <summary>
    /// Custom system prompt for this group (topic-specific)
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 85;
}
