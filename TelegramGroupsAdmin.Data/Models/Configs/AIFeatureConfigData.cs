namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIFeatureConfig for EF Core JSON column mapping.
/// </summary>
public class AIFeatureConfigData
{
    /// <summary>
    /// Connection ID to use for this feature (null = feature disabled)
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Model to use for this feature
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
    public bool RequiresVision { get; set; }
}
