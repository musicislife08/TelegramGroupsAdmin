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
