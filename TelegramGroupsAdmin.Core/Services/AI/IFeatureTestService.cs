using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Service for testing AI feature configurations before saving
/// Validates that a connection + model combination works for a specific feature
/// </summary>
public interface IFeatureTestService
{
    /// <summary>
    /// Test if the specified connection and model work for the given feature type
    /// </summary>
    /// <param name="featureType">The AI feature to test</param>
    /// <param name="connectionId">Connection ID to use</param>
    /// <param name="model">Model name to test</param>
    /// <param name="azureDeploymentName">Azure deployment name (required for Azure connections)</param>
    /// <param name="maxTokens">Max tokens from feature config (defaults to 500 if not specified)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test result with success status and message</returns>
    Task<FeatureTestResult> TestFeatureAsync(
        AIFeatureType featureType,
        string connectionId,
        string model,
        string? azureDeploymentName = null,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);
}
