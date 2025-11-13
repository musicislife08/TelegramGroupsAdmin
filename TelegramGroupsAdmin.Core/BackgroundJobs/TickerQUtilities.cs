using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// TEMPORARY STUB: Placeholder for TickerQUtilities during migration to Quartz.NET
/// TODO: Remove this file in Phase 9 when IJobTriggerService is fully implemented
/// </summary>
public static class TickerQUtilities
{
    /// <summary>
    /// STUB: Previously scheduled jobs via TickerQ, now returns null (no-op)
    /// Will be replaced with Quartz.NET IJobTriggerService in Phase 9
    /// </summary>
    public static Task<Guid?> ScheduleJobAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        string jobName,
        object payload,
        int delaySeconds = 0,
        int retries = 3,
        int[]? retryIntervals = null)
    {
        // STUB: Job scheduling disabled during migration
        // Jobs will be re-enabled in Phase 9 with Quartz.NET
        logger.LogWarning(
            "Job scheduling temporarily disabled during migration: {JobName} (delay: {Delay}s, retries: {Retries}). " +
            "Jobs will be re-enabled after Quartz.NET migration completes.",
            jobName,
            delaySeconds,
            retries);

        return Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// STUB: Previously cancelled jobs via TickerQ, now no-op
    /// Will be replaced with Quartz.NET IJobTriggerService in Phase 9
    /// </summary>
    public static Task<bool> CancelJobAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        Guid jobId)
    {
        // STUB: Job cancellation disabled during migration
        logger.LogWarning(
            "Job cancellation temporarily disabled during migration: JobId {JobId}. " +
            "Jobs will be re-enabled after Quartz.NET migration completes.",
            jobId);

        return Task.FromResult(false);
    }
}
