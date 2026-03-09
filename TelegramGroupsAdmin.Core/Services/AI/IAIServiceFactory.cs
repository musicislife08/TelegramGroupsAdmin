using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Service for AI configuration management (used by Settings UI)
/// For making AI calls, use IChatService directly
/// </summary>
public interface IAIServiceFactory
{
    /// <summary>
    /// Get the status of an AI feature's configuration
    /// </summary>
    /// <param name="feature">The AI feature type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Feature status</returns>
    Task<AIFeatureStatus> GetFeatureStatusAsync(AIFeatureType feature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all configured connections (for Settings UI)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of connections</returns>
    Task<IReadOnlyList<AIConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get feature configuration (for Settings UI)
    /// </summary>
    /// <param name="feature">The AI feature type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Feature config or null if not configured</returns>
    Task<AIFeatureConfig?> GetFeatureConfigAsync(AIFeatureType feature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch available models from a provider and cache in the connection
    /// </summary>
    /// <param name="connectionId">Connection ID to refresh models for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available models</returns>
    Task<IReadOnlyList<AIModelInfo>> RefreshModelsAsync(string connectionId, CancellationToken cancellationToken = default);
}
