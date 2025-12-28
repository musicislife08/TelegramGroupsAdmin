namespace TelegramGroupsAdmin.Ui.Server.Services.PromptBuilder;

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
