namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIProviderConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// Note: AIFeatureType enum stored as int in dictionary keys (0=SpamDetection, 1=Translation, etc.)
/// </summary>
public class AIProviderConfigData
{
    /// <summary>
    /// Reusable connections (define once, use by multiple features)
    /// </summary>
    public List<AIConnectionData> Connections { get; set; } = [];

    /// <summary>
    /// Per-feature configuration (connection + model + params)
    /// Key is int representing AIFeatureType enum value
    /// </summary>
    public Dictionary<int, AIFeatureConfigData> Features { get; set; } = new()
    {
        [0] = new(), // SpamDetection
        [1] = new(), // Translation
        [2] = new() { RequiresVision = true }, // ImageAnalysis
        [3] = new() { RequiresVision = true }, // VideoAnalysis
        [4] = new() // PromptBuilder
    };
}
