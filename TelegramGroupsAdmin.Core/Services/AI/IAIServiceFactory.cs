using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Status of an AI feature's configuration
/// </summary>
/// <param name="IsConfigured">Whether the feature has a connection and model assigned</param>
/// <param name="ConnectionEnabled">Whether the assigned connection is enabled</param>
/// <param name="RequiresVision">Whether the feature requires vision capability</param>
/// <param name="ConnectionId">ID of the assigned connection (if any)</param>
/// <param name="ModelName">Name of the assigned model (if any)</param>
public record AIFeatureStatus(
    bool IsConfigured,
    bool ConnectionEnabled,
    bool RequiresVision,
    string? ConnectionId,
    string? ModelName);

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
