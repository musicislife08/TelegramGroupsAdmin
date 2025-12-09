namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// AI provider types supported by the application
/// </summary>
public enum AIProviderType
{
    /// <summary>
    /// OpenAI API (api.openai.com)
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service (custom endpoint + deployment)
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Local/OpenAI-compatible endpoints (Ollama, LM Studio, vLLM)
    /// </summary>
    LocalOpenAI
}

/// <summary>
/// AI feature types that can be configured independently
/// </summary>
public enum AIFeatureType
{
    /// <summary>
    /// Text spam analysis
    /// </summary>
    SpamDetection,

    /// <summary>
    /// Message translation
    /// </summary>
    Translation,

    /// <summary>
    /// Vision API for images
    /// </summary>
    ImageAnalysis,

    /// <summary>
    /// Vision API for video frames
    /// </summary>
    VideoAnalysis,

    /// <summary>
    /// Meta-AI prompt generation
    /// </summary>
    PromptBuilder
}

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
    /// Azure OpenAI API version (default: 2024-02-01)
    /// </summary>
    public string? AzureApiVersion { get; set; } = "2024-02-01";

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

/// <summary>
/// Model info returned from provider's model listing API
/// Simplified structure - capability validation is done via test button, not inference
/// </summary>
public class AIModelInfo
{
    /// <summary>
    /// Model identifier (e.g., "gpt-4o-mini", "llama3.2")
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Model size in bytes (from Ollama API, null for cloud providers)
    /// </summary>
    public long? SizeBytes { get; set; }
}

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

/// <summary>
/// Root AI provider configuration stored in configs.ai_provider_config JSONB column
/// Supports multiple connections (providers) and per-feature configuration
/// </summary>
public class AIProviderConfig
{
    /// <summary>
    /// Reusable connections (define once, use by multiple features)
    /// </summary>
    public List<AIConnection> Connections { get; set; } = [];

    /// <summary>
    /// Per-feature configuration (connection + model + params)
    /// </summary>
    public Dictionary<AIFeatureType, AIFeatureConfig> Features { get; set; } = new()
    {
        [AIFeatureType.SpamDetection] = new(),
        [AIFeatureType.Translation] = new(),
        [AIFeatureType.ImageAnalysis] = new() { RequiresVision = true },
        [AIFeatureType.VideoAnalysis] = new() { RequiresVision = true },
        [AIFeatureType.PromptBuilder] = new()
    };
}
