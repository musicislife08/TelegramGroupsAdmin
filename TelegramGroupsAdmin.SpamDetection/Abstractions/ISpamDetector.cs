using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Abstractions;

/// <summary>
/// Main spam detection orchestrator that runs all configured checks
/// </summary>
public interface ISpamDetector
{
    /// <summary>
    /// Execute all configured spam checks for the given request
    /// </summary>
    /// <param name="request">The spam check request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated result from all spam checks</returns>
    Task<SpamCheckResult> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of all configured checks for the given group
    /// </summary>
    /// <param name="groupId">Group ID</param>
    /// <returns>List of check names</returns>
    IEnumerable<string> GetConfiguredChecks(int groupId);
}