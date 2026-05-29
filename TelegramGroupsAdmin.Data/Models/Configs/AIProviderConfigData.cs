namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIProviderConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// Note: AIFeatureType enum keys serialize as named strings in JSONB (e.g., "SpamDetection"),
/// matching the domain model's Dictionary&lt;AIFeatureType, AIFeatureConfig&gt; serialization.
/// </summary>
public class AIProviderConfigData
{
    /// <summary>
    /// Reusable connections (define once, use by multiple features)
    /// </summary>
    public List<AIConnectionData> Connections { get; set; } = [];

    /// <summary>
    /// Per-feature configuration (connection + model + params).
    /// String keys match the named-string serialization of AIFeatureType enum values.
    /// </summary>
    public Dictionary<string, AIFeatureConfigData> Features { get; set; } = new()
    {
        ["SpamDetection"] = new(),
        ["Translation"] = new(),
        ["ImageAnalysis"] = new() { RequiresVision = true },
        ["VideoAnalysis"] = new() { RequiresVision = true },
        ["PromptBuilder"] = new(),
        ["ProfileScan"] = new() { RequiresVision = true }
    };
}
