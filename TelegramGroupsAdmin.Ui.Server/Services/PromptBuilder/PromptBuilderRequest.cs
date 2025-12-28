namespace TelegramGroupsAdmin.Ui.Server.Services.PromptBuilder;

/// <summary>
/// Request model for AI-powered prompt generation
/// Phase 4.X: Prompt builder
/// </summary>
public class PromptBuilderRequest
{
    /// <summary>
    /// Group topic/focus (e.g., "Crypto", "Trading", "Tech")
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what the group discusses
    /// </summary>
    public string GroupDescription { get; set; } = string.Empty;

    /// <summary>
    /// Specific rules for what's allowed vs spam in this group
    /// </summary>
    public string? CustomRules { get; set; }

    /// <summary>
    /// Common spam patterns observed in this group
    /// </summary>
    public string? CommonSpamPatterns { get; set; }

    /// <summary>
    /// Examples of legitimate content that should NOT be flagged
    /// </summary>
    public string? LegitimateExamples { get; set; }

    /// <summary>
    /// Strictness level for spam detection
    /// </summary>
    public StrictnessLevel Strictness { get; set; } = StrictnessLevel.Balanced;

    /// <summary>
    /// Number of recent messages to analyze for context (0-50)
    /// </summary>
    public int MessageHistoryCount { get; set; } = 20;

    /// <summary>
    /// Chat ID to pull message history from
    /// </summary>
    public long ChatId { get; set; }
}
