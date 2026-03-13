namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Connection to an AI provider (the "pipe" - endpoint + auth)
/// Multiple features can share one connection with different models
/// </summary>
public class AIConnection
{
    /// <summary>
    /// Unique identifier for this connection (e.g., "openai", "azure-prod", "local-ollama")
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Provider type for this connection
    /// </summary>
    public AIProviderType Provider { get; set; } = AIProviderType.OpenAI;

    /// <summary>
    /// Whether this connection is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Azure OpenAI endpoint URL (required for AzureOpenAI provider)
    /// </summary>
    public string? AzureEndpoint { get; set; }

    /// <summary>
    /// Azure OpenAI API version (default: 2024-10-21)
    /// </summary>
    public string? AzureApiVersion { get; set; } = "2024-10-21";

    /// <summary>
    /// Local endpoint URL for OpenAI-compatible providers (e.g., http://localhost:11434/v1)
    /// </summary>
    public string? LocalEndpoint { get; set; }

    /// <summary>
    /// Whether the local provider requires an API key
    /// </summary>
    public bool LocalRequiresApiKey { get; set; } = false;

    /// <summary>
    /// Cached available models (fetched via "Refresh Models" button)
    /// </summary>
    public List<AIModelInfo> AvailableModels { get; set; } = [];

    /// <summary>
    /// When the available models were last fetched
    /// </summary>
    public DateTimeOffset? ModelsLastFetched { get; set; }
}
