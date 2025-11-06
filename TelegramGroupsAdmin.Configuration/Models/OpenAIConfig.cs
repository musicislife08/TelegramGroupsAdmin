namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// OpenAI service configuration stored in configs.openai_config JSONB column
/// API key is stored separately in configs.api_keys (encrypted)
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// OpenAI model to use for image analysis and text generation
    /// Default: gpt-4o-mini (cost-effective for most use cases)
    /// Options: gpt-4o, gpt-4-turbo, gpt-3.5-turbo
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Maximum tokens in API responses
    /// Affects cost and response length
    /// </summary>
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Temperature for API requests (0.0-2.0)
    /// Lower = more deterministic, Higher = more creative
    /// Default: 0.2 for spam detection (consistency preferred)
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Whether OpenAI features are enabled
    /// Set to false to disable image spam detection and translation
    /// </summary>
    public bool Enabled { get; set; } = false;
}
