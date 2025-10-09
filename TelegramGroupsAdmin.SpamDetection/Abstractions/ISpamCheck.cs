using TelegramGroupsAdmin.SpamDetection.Models;

namespace TelegramGroupsAdmin.SpamDetection.Abstractions;

/// <summary>
/// Interface for individual spam detection checks
/// </summary>
public interface ISpamCheck
{
    /// <summary>
    /// Name of this spam check for identification
    /// </summary>
    string CheckName { get; }

    /// <summary>
    /// Execute the spam check on the provided request
    /// </summary>
    /// <param name="request">The spam check request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of this spam check</returns>
    Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this check should be executed for the given request
    /// Used to skip checks based on message length, configuration, etc.
    /// </summary>
    /// <param name="request">The spam check request</param>
    /// <returns>True if this check should run</returns>
    bool ShouldExecute(SpamCheckRequest request);
}