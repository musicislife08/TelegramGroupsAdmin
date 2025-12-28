namespace TelegramGroupsAdmin.Ui.Server.Services;

/// <summary>
/// Service to check which external service features are configured and available
/// Used by UI for conditional rendering and by services for graceful degradation
/// </summary>
public interface IFeatureAvailabilityService
{
    /// <summary>
    /// Checks if SendGrid email service is configured and enabled
    /// </summary>
    Task<bool> IsEmailConfiguredAsync();

    /// <summary>
    /// Checks if OpenAI API is configured
    /// </summary>
    Task<bool> IsOpenAIConfiguredAsync();

    /// <summary>
    /// Checks if VirusTotal API is configured
    /// </summary>
    Task<bool> IsVirusTotalConfiguredAsync();

    /// <summary>
    /// Checks if password reset feature is available (requires email)
    /// </summary>
    Task<bool> IsPasswordResetEnabledAsync();

    /// <summary>
    /// Checks if email verification feature is available (requires email)
    /// </summary>
    Task<bool> IsEmailVerificationEnabledAsync();

    /// <summary>
    /// Gets comprehensive feature status for all external services
    /// </summary>
    Task<FeatureStatus> GetFeatureStatusAsync();
}

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
