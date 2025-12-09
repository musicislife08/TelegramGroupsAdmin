namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Generic interface for AI chat completions
/// Provider-agnostic abstraction that could be implemented with Semantic Kernel, direct HTTP calls, or any other AI client
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Get a text-based chat completion
    /// </summary>
    /// <param name="systemPrompt">System prompt defining AI behavior</param>
    /// <param name="userPrompt">User message/query</param>
    /// <param name="options">Optional parameters for the completion</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Completion result or null if the request failed</returns>
    Task<ChatCompletionResult?> GetCompletionAsync(
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get a vision-based chat completion (for models that support images)
    /// </summary>
    /// <param name="systemPrompt">System prompt defining AI behavior</param>
    /// <param name="userPrompt">User message/query about the image</param>
    /// <param name="imageData">Image bytes</param>
    /// <param name="mimeType">Image MIME type (e.g., "image/jpeg", "image/png")</param>
    /// <param name="options">Optional parameters for the completion</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Completion result or null if the request failed or vision not supported</returns>
    Task<ChatCompletionResult?> GetVisionCompletionAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default);
}
