namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIConnection for EF Core JSON column mapping.
/// Note: AIProviderType enum stored as int (0=OpenAI, 1=AzureOpenAI, 2=LocalOpenAI)
/// </summary>
public class AIConnectionData
{
    /// <summary>
    /// Unique identifier for this connection
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Provider type (stored as int, maps to AIProviderType enum)
    /// </summary>
    public int Provider { get; set; } // OpenAI = 0

    /// <summary>
    /// Whether this connection is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Azure OpenAI endpoint URL
    /// </summary>
    public string? AzureEndpoint { get; set; }

    /// <summary>
    /// Azure OpenAI API version
    /// </summary>
    public string? AzureApiVersion { get; set; } = "2024-10-21";

    /// <summary>
    /// Local endpoint URL for OpenAI-compatible providers
    /// </summary>
    public string? LocalEndpoint { get; set; }

    /// <summary>
    /// Whether the local provider requires an API key
    /// </summary>
    public bool LocalRequiresApiKey { get; set; }

    /// <summary>
    /// Cached available models
    /// </summary>
    public List<AIModelInfoData> AvailableModels { get; set; } = [];

    /// <summary>
    /// When the available models were last fetched
    /// </summary>
    public DateTimeOffset? ModelsLastFetched { get; set; }
}
