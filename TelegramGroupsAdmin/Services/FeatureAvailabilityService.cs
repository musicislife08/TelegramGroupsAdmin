using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service to check which external service features are configured and available
/// Used by UI for conditional rendering and by services for graceful degradation
/// </summary>
public class FeatureAvailabilityService : IFeatureAvailabilityService
{
    private readonly IFileScanningConfigRepository _configRepo;
    private readonly ILogger<FeatureAvailabilityService> _logger;

    public FeatureAvailabilityService(
        IFileScanningConfigRepository configRepo,
        ILogger<FeatureAvailabilityService> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
    }

    public async Task<bool> IsEmailConfiguredAsync()
    {
        try
        {
            // Check if SendGrid is enabled in database config
            var sendGridConfig = await _configRepo.GetSendGridConfigAsync();
            if (sendGridConfig?.Enabled != true)
            {
                return false;
            }

            // Check if required fields are configured
            if (string.IsNullOrWhiteSpace(sendGridConfig.FromAddress))
            {
                return false;
            }

            // Check if API key is configured in database
            var apiKeys = await _configRepo.GetApiKeysAsync();
            return !string.IsNullOrWhiteSpace(apiKeys?.SendGrid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check email configuration status");
            return false;
        }
    }

    public async Task<bool> IsOpenAIConfiguredAsync()
    {
        try
        {
            // Check if OpenAI is enabled in database config
            var openAIConfig = await _configRepo.GetOpenAIConfigAsync();
            if (openAIConfig?.Enabled != true)
            {
                return false;
            }

            // Check if API key is configured in database
            var apiKeys = await _configRepo.GetApiKeysAsync();
            return !string.IsNullOrWhiteSpace(apiKeys?.OpenAI);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check OpenAI configuration status");
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
