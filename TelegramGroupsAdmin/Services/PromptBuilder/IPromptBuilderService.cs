namespace TelegramGroupsAdmin.Services.PromptBuilder;

/// <summary>
/// Service for generating AI-powered custom spam detection prompts
/// Phase 4.X: Prompt builder
/// </summary>
public interface IPromptBuilderService
{
    /// <summary>
    /// Generate a custom spam detection prompt using OpenAI
    /// Analyzes group context, message history, and user requirements
    /// </summary>
    Task<PromptBuilderResponse> GeneratePromptAsync(
        PromptBuilderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Improve an existing prompt based on user feedback
    /// Uses OpenAI to refine the prompt according to specific improvement requests
    /// </summary>
    Task<PromptBuilderResponse> ImprovePromptAsync(
        string currentPrompt,
        string improvementFeedback,
        CancellationToken cancellationToken = default);
}
