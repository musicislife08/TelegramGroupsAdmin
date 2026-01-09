namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIModelInfo for EF Core JSON column mapping.
/// </summary>
public class AIModelInfoData
{
    /// <summary>
    /// Model identifier
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Model size in bytes (from Ollama API, null for cloud providers)
    /// </summary>
    public long? SizeBytes { get; set; }
}
