using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Generic interface for AI chat completions
/// Provider-agnostic abstraction using Semantic Kernel
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Get a text-based chat completion for a specific feature
    /// </summary>
    /// <param name="feature">The AI feature type (determines which connection/model to use)</param>
    /// <param name="systemPrompt">System prompt defining AI behavior</param>
    /// <param name="userPrompt">User message/query</param>
    /// <param name="options">Optional parameters for the completion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion result or null if the feature is not configured or the request failed</returns>
    Task<ChatCompletionResult?> GetCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a vision-based chat completion for a specific feature (for models that support images)
    /// </summary>
    /// <param name="feature">The AI feature type (determines which connection/model to use)</param>
    /// <param name="systemPrompt">System prompt defining AI behavior</param>
    /// <param name="userPrompt">User message/query about the image</param>
    /// <param name="imageData">Image bytes</param>
    /// <param name="mimeType">Image MIME type (e.g., "image/jpeg", "image/png")</param>
    /// <param name="options">Optional parameters for the completion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion result or null if the feature is not configured, vision not supported, or request failed</returns>
    Task<ChatCompletionResult?> GetVisionCompletionAsync(
        AIFeatureType feature,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a feature is configured and ready to use
    /// </summary>
    /// <param name="feature">The AI feature type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the feature is configured with a valid, enabled connection</returns>
    Task<bool> IsFeatureAvailableAsync(AIFeatureType feature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidate cached kernel for a connection (call when connection config changes)
    /// </summary>
    /// <param name="connectionId">Connection ID to invalidate, or null to clear all</param>
    void InvalidateCache(string? connectionId = null);

    /// <summary>
    /// Test a chat completion with a specific connection and model (for Settings UI testing)
    /// Used by FeatureTestService to validate configurations before saving
    /// </summary>
    /// <param name="connectionId">Connection ID to test</param>
    /// <param name="model">Model name to test</param>
    /// <param name="azureDeploymentName">Azure deployment name (only for Azure OpenAI)</param>
    /// <param name="systemPrompt">System prompt for the test</param>
    /// <param name="userPrompt">User prompt for the test</param>
    /// <param name="options">Optional parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion result or null if the test failed</returns>
    Task<ChatCompletionResult?> TestCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test a vision completion with a specific connection and model (for Settings UI testing)
    /// Used by FeatureTestService to validate vision capabilities before saving
    /// </summary>
    /// <param name="connectionId">Connection ID to test</param>
    /// <param name="model">Model name to test</param>
    /// <param name="azureDeploymentName">Azure deployment name (only for Azure OpenAI)</param>
    /// <param name="systemPrompt">System prompt for the test</param>
    /// <param name="userPrompt">User prompt for the test</param>
    /// <param name="imageData">Test image bytes</param>
    /// <param name="mimeType">Image MIME type</param>
    /// <param name="options">Optional parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion result or null if the test failed</returns>
    Task<ChatCompletionResult?> TestVisionCompletionAsync(
        string connectionId,
        string model,
        string? azureDeploymentName,
        string systemPrompt,
        string userPrompt,
        byte[] imageData,
        string mimeType,
        ChatCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}
