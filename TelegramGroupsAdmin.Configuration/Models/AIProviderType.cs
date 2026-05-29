namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// AI provider types supported by the application
/// </summary>
public enum AIProviderType
{
    /// <summary>
    /// OpenAI API (api.openai.com)
    /// </summary>
    OpenAI = 0,

    /// <summary>
    /// Azure OpenAI Service (custom endpoint + deployment)
    /// </summary>
    AzureOpenAI = 1,

    /// <summary>
    /// OpenAI-compatible endpoints (Ollama, LM Studio, vLLM, …)
    /// </summary>
    OpenAICompatible = 2,

    /// <summary>
    /// OpenRouter aggregator (https://openrouter.ai/api/v1)
    /// </summary>
    OpenRouter = 3,

    /// <summary>
    /// Anthropic (Claude), direct (api.anthropic.com)
    /// </summary>
    Anthropic = 4
}
