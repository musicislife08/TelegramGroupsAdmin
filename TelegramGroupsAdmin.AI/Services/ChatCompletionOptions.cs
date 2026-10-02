namespace TelegramGroupsAdmin.AI.Services;

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
    /// Request JSON format response (if supported by model)
    /// </summary>
    public bool JsonMode { get; init; }
}
