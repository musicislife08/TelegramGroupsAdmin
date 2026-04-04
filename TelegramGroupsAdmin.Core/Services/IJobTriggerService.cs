namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for manually triggering background jobs
/// </summary>
public interface IJobTriggerService
{
    /// <summary>
    /// Trigger a job immediately with the specified payload
    /// Returns immediately after scheduling (does not wait for job completion)
    /// </summary>
    /// <param name="jobName">Job name (must match registered job identity)</param>
    /// <param name="payload">Job-specific payload object</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unique trigger ID</returns>
    Task<string> TriggerNowAsync(string jobName, object payload, CancellationToken cancellationToken = default);

}
