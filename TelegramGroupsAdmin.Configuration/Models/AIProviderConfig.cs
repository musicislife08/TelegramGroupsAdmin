namespace TelegramGroupsAdmin.Configuration.Models;

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
        [AIFeatureType.PromptBuilder] = new(),
        [AIFeatureType.ProfileScan] = new() { RequiresVision = true }
    };
}
