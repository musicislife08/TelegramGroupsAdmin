namespace TelegramGroupsAdmin.Core.Services.AI;

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
