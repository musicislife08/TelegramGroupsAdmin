using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service to check which external service features are configured and available
/// Used by UI for conditional rendering and by services for graceful degradation
/// </summary>
public class FeatureAvailabilityService : IFeatureAvailabilityService
{
    private readonly ISystemConfigRepository _configRepo;
    private readonly ILogger<FeatureAvailabilityService> _logger;

    public FeatureAvailabilityService(
        ISystemConfigRepository configRepo,
        ILogger<FeatureAvailabilityService> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
    }

    public async Task<bool> IsEmailConfiguredAsync()
        => await GetEmailConfigurationStateAsync() == EmailConfigurationState.Enabled;

    public async Task<EmailConfigurationState> GetEmailConfigurationStateAsync()
    {
        try
        {
            // Check if SendGrid is enabled in database config
            var sendGridConfig = await _configRepo.GetSendGridConfigAsync();
            if (sendGridConfig?.Enabled != true)
            {
                return EmailConfigurationState.Disabled;
            }

            // Check if required fields are configured
            if (string.IsNullOrWhiteSpace(sendGridConfig.FromAddress))
            {
                return EmailConfigurationState.Disabled;
            }

            // Check if API key is configured in database. Stored-but-undecryptable keys are not "no key":
            // the service may well be configured, so the answer is unknown and callers must fail closed.
            var apiKeys = await _configRepo.ReadApiKeysAsync();
            if (apiKeys.Status == ApiKeysReadStatus.Undecryptable)
            {
                _logger.LogError("Email configuration state is indeterminate: stored API keys could not be decrypted");
                return EmailConfigurationState.Indeterminate;
            }

            return string.IsNullOrWhiteSpace(apiKeys.Keys?.SendGrid)
                ? EmailConfigurationState.Disabled
                : EmailConfigurationState.Enabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check email configuration status");
            return EmailConfigurationState.Indeterminate;
        }
    }

    public async Task<bool> IsOpenAIConfiguredAsync()
    {
        try
        {
            // Check if any AI connection is configured and has an API key
            var aiProviderConfig = await _configRepo.GetAIProviderConfigAsync();
            if (aiProviderConfig?.Connections == null || aiProviderConfig.Connections.Count == 0)
            {
                return false;
            }

            // Check if any enabled connection has an API key
            var apiKeys = await _configRepo.GetApiKeysAsync();
            if (apiKeys?.AIConnectionKeys == null)
            {
                return false;
            }

            // Return true if any enabled connection has a non-empty API key configured
            return aiProviderConfig.Connections.Any(c =>
                c.Enabled &&
                apiKeys.AIConnectionKeys.TryGetValue(c.Id, out var key) &&
                !string.IsNullOrWhiteSpace(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check AI provider configuration status");
            return false;
        }
    }

    public async Task<bool> IsVirusTotalConfiguredAsync()
    {
        try
        {
            // Check if API key is configured in database
            var apiKeys = await _configRepo.GetApiKeysAsync();
            return !string.IsNullOrWhiteSpace(apiKeys?.VirusTotal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check VirusTotal configuration status");
            return false;
        }
    }

    public Task<bool> IsPasswordResetEnabledAsync()
    {
        // Password reset requires email service
        return IsEmailConfiguredAsync();
    }

    public Task<bool> IsEmailVerificationEnabledAsync()
    {
        // Email verification requires email service
        return IsEmailConfiguredAsync();
    }

    public async Task<FeatureStatus> GetFeatureStatusAsync()
    {
        var emailConfigured = await IsEmailConfiguredAsync();
        var openAIConfigured = await IsOpenAIConfiguredAsync();
        var virusTotalConfigured = await IsVirusTotalConfiguredAsync();

        string? emailWarning = emailConfigured
            ? null
            : "Email service not configured. Password reset and email verification are unavailable.";

        string? openAIWarning = openAIConfigured
            ? null
            : "OpenAI API not configured. Image spam detection and translation features are disabled.";

        string? virusTotalWarning = virusTotalConfigured
            ? null
            : "VirusTotal API not configured. Cloud file scanning is disabled.";

        return new FeatureStatus(
            EmailConfigured: emailConfigured,
            OpenAIConfigured: openAIConfigured,
            VirusTotalConfigured: virusTotalConfigured,
            EmailWarning: emailWarning,
            OpenAIWarning: openAIWarning,
            VirusTotalWarning: virusTotalWarning
        );
    }
}
