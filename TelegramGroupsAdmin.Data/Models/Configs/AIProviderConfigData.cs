namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of AIProviderConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// Note: feature keys are stored as the integer values of the AIFeatureType enum
/// (e.g., 0 = SpamDetection), so renaming an enum member never orphans stored config.
/// </summary>
public class AIProviderConfigData
{
    /// <summary>
    /// Reusable connections (define once, use by multiple features)
    /// </summary>
    public List<AIConnectionData> Connections { get; set; } = [];

    /// <summary>
    /// Per-feature configuration (connection + model + params).
    /// Integer keys are the AIFeatureType enum values (0..5).
    /// </summary>
    public Dictionary<int, AIFeatureConfigData> Features { get; set; } = new()
    {
        [0] = new(), // SpamDetection
        [1] = new(), // Translation
        [2] = new() { RequiresVision = true }, // ImageAnalysis
        [3] = new() { RequiresVision = true }, // VideoAnalysis
        [4] = new(), // PromptBuilder
        [5] = new() { RequiresVision = true } // ProfileScan
    };
}
