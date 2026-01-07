namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// AI veto configuration - AI always runs as veto to confirm/override spam detection
/// </summary>
public class AIVetoConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether AI veto check is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to check short messages with AI
    /// </summary>
    public bool CheckShortMessages { get; set; } = false;

    /// <summary>
    /// Custom system prompt for this group (topic-specific)
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Number of recent messages to include as context for AI analysis
    /// Provides conversation history to improve spam detection accuracy
    /// Higher values = more context but increased API costs
    /// </summary>
    public int MessageHistoryCount { get; set; } = 3;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 85;
}
