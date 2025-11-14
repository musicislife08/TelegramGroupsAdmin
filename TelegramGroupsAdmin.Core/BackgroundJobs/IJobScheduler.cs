namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Service for scheduling ad-hoc background jobs (one-time, delayed execution)
/// Used for user-triggered actions like message deletion, welcome timeouts, etc.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Schedule a one-time job to execute after a delay
    /// </summary>
    /// <typeparam name="TPayload">Job payload type</typeparam>
    /// <param name="jobName">Unique job name (used for job type identification)</param>
    /// <param name="payload">Job payload data</param>
    /// <param name="delaySeconds">Delay before execution (0 = immediate)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unique job ID for tracking/cancellation</returns>
    Task<string> ScheduleJobAsync<TPayload>(
        string jobName,
        TPayload payload,
        int delaySeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a scheduled job by ID
    /// </summary>
    /// <param name="jobId">Job ID returned from ScheduleJobAsync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if job was cancelled, false if already executed or not found</returns>
    Task<bool> CancelJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a job is still scheduled (not yet executed or cancelled)
    /// </summary>
    /// <param name="jobId">Job ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if job exists and is scheduled</returns>
    Task<bool> IsScheduledAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
