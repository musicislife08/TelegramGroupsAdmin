namespace TelegramGroupsAdmin.Services.PromptBuilder;

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

/// <summary>
/// Response model from AI-powered prompt generation
/// </summary>
public class PromptBuilderResponse
{
    /// <summary>
    /// The generated custom prompt text
    /// </summary>
    public string GeneratedPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Metadata about the generation (for storage in database)
    /// Serialized JSON of the request parameters
    /// </summary>
    public string GenerationMetadata { get; set; } = string.Empty;

    /// <summary>
    /// Whether generation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if generation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Strictness levels for spam detection
/// </summary>
public enum StrictnessLevel
{
    Conservative = 0, // Prefer false negatives (let spam through rather than block legit)
    Balanced = 1,     // Middle ground
    Aggressive = 2    // Prefer false positives (block questionable content)
}
