namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Service for manually triggering background jobs
/// Supports immediate execution and scheduled one-time execution
/// Phase 9: Manual trigger API
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

    /// <summary>
    /// Schedule a job to run once at a specific time in the future
    /// Used for delayed actions like tempban expiry, welcome timeouts
    /// </summary>
    /// <param name="jobName">Job name (must match registered job identity)</param>
    /// <param name="payload">Job-specific payload object</param>
    /// <param name="runAt">When to execute the job (UTC)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unique trigger ID</returns>
    Task<string> ScheduleOnceAsync(string jobName, object payload, DateTimeOffset runAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a scheduled one-time job by trigger ID
    /// Returns true if trigger was found and cancelled, false if not found
    /// </summary>
    /// <param name="triggerId">Trigger ID returned from ScheduleOnceAsync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<bool> CancelScheduledJobAsync(string triggerId, CancellationToken cancellationToken = default);
}
