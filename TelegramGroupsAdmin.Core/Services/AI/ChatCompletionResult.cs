namespace TelegramGroupsAdmin.Core.Services.AI;

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
    public int? TotalTokens { get; init; }

    /// <summary>
    /// Prompt tokens used if available
    /// </summary>
    public int? PromptTokens { get; init; }

    /// <summary>
    /// Completion tokens used if available
    /// </summary>
    public int? CompletionTokens { get; init; }

    /// <summary>
    /// Model that was used for the completion
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Finish reason if available (e.g., "stop", "length", "content_filter")
    /// </summary>
    public string? FinishReason { get; init; }
}

/// <summary>
/// Options for chat completion requests
/// </summary>
public record ChatCompletionOptions
{
    /// <summary>
    /// Maximum tokens to generate in the response
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Temperature for randomness (0.0 = deterministic, 2.0 = very random)
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Request JSON format response (if supported by model)
    /// </summary>
    public bool JsonMode { get; init; }
}
