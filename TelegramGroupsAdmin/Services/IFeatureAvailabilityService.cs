namespace TelegramGroupsAdmin.Services;

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
    /// Strict email-service check for security decisions: Enabled, Disabled (genuinely not configured) or
    /// Indeterminate (the config read threw, or stored API keys could not be decrypted). Never throws.
    /// </summary>
    Task<EmailConfigurationState> GetEmailConfigurationStateAsync();

    /// <summary>
    /// Gets comprehensive feature status for all external services
    /// </summary>
    Task<FeatureStatus> GetFeatureStatusAsync();
}
