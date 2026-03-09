namespace TelegramGroupsAdmin.Configuration.Models;

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
