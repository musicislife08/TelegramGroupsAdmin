namespace TelegramGroupsAdmin.AI.Services;

/// <summary>
/// Result from a chat completion request
/// Provider-agnostic representation of AI response
/// </summary>
public record ChatCompletionResult
{
    /// <summary>
    /// The text content of the AI response
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Total tokens used (prompt + completion) if available
    /// </summary>
    public long? TotalTokens { get; init; }

    /// <summary>
    /// Prompt tokens used if available
    /// </summary>
    public long? PromptTokens { get; init; }

    /// <summary>
    /// Completion tokens used if available
    /// </summary>
    public long? CompletionTokens { get; init; }

    /// <summary>
    /// Model that was used for the completion
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Finish reason if available (e.g., "stop", "length", "content_filter")
    /// </summary>
    public string? FinishReason { get; init; }
}
