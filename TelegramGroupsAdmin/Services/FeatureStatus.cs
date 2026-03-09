namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Comprehensive status of all external service features
/// </summary>
public record FeatureStatus(
    bool EmailConfigured,
    bool OpenAIConfigured,
    bool VirusTotalConfigured,
    string? EmailWarning,
    string? OpenAIWarning,
    string? VirusTotalWarning
);
