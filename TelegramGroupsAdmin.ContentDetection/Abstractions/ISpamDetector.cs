using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Abstractions;

/// <summary>
/// Main content detection orchestrator that runs all configured checks
/// </summary>
public interface IContentDetector
{
    /// <summary>
    /// Execute all configured content checks for the given request
    /// </summary>
    /// <param name="request">The content check request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated result from all content checks</returns>
    Task<ContentCheckResult> CheckAsync(ContentCheckRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of all configured checks for the given group
    /// </summary>
    /// <param name="groupId">Group ID</param>
    /// <returns>List of check names</returns>
    IEnumerable<string> GetConfiguredChecks(int groupId);
}