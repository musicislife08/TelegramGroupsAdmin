namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Per-feature configuration (which connection + model + parameters)
/// </summary>
public class AIFeatureConfig
{
    /// <summary>
    /// Connection ID to use for this feature (null = feature disabled)
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Model to use for this feature (e.g., "gpt-4o-mini", "gpt-4o")
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Maximum tokens in API responses
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Temperature for API requests (0.0-2.0)
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Azure deployment name (required for AzureOpenAI connections)
    /// </summary>
    public string? AzureDeploymentName { get; set; }

    /// <summary>
    /// Whether this feature requires vision capability
    /// </summary>
    public bool RequiresVision { get; set; } = false;
}
