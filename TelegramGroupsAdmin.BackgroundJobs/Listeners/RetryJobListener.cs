using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Listeners;

/// <summary>
/// Job listener that implements retry logic with exponential backoff
/// Reschedules failed jobs up to MaxRetries times with increasing delays
/// Phase 7: Retry policy (3 retries with exponential backoff)
/// </summary>
public class RetryJobListener(ILogger<RetryJobListener> logger, ISchedulerFactory schedulerFactory) : IJobListener
{
    public string Name => "RetryJobListener";

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // No action needed before job execution
        return Task.CompletedTask;
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // No action needed if job execution is vetoed
        return Task.CompletedTask;
    }

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        // Only handle failed jobs
        if (jobException == null)
        {
            // Job succeeded - clear retry count if it exists
            if (context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.RetryCount))
            {
                logger.LogInformation(
                    "Job {JobName} succeeded after previous retry attempts",
                    context.JobDetail.Key.Name);
            }
            return;
        }

        // Get current retry count (defaults to 0 for first failure)
        var retryCount = context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.RetryCount)
            ? context.JobDetail.JobDataMap.GetInt(JobDataKeys.RetryCount)
            : 0;

        if (retryCount >= RetryConstants.MaxRetries)
        {
            logger.LogError(
                jobException,
                "Job {JobName} failed after {MaxRetries} retry attempts. Giving up.",
                context.JobDetail.Key.Name,
                RetryConstants.MaxRetries);
            return;
        }

        // Calculate exponential backoff delay: baseInterval * 2^retryCount
        var baseBackoffInterval = TimeSpan.FromSeconds(RetryConstants.BaseBackoffSeconds);
        var backoffDelay = TimeSpan.FromTicks(baseBackoffInterval.Ticks * (long)Math.Pow(2, retryCount));
        var nextRetryCount = retryCount + 1;

        logger.LogWarning(
            jobException,
            "Job {JobName} failed (attempt {CurrentAttempt}/{MaxAttempts}). Retrying in {Delay}...",
            context.JobDetail.Key.Name,
            nextRetryCount,
            RetryConstants.MaxRetries + 1, // Total attempts = MaxRetries + initial attempt
            backoffDelay);

        // Create new trigger for retry with exponential backoff
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        // Build new job data map with incremented retry count and preserved payload
        // CRITICAL: Must preserve PayloadJson and PayloadType from original job for ad-hoc jobs
        var retryJobData = new JobDataMap
        {
            { JobDataKeys.RetryCount, nextRetryCount }
        };

        // Preserve original payload if present (ad-hoc jobs)
        if (context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.PayloadJson))
        {
            retryJobData.Put(JobDataKeys.PayloadJson, context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson));
            retryJobData.Put(JobDataKeys.PayloadType, context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadType));
        }

        var retryTrigger = TriggerBuilder.Create()
            .ForJob(context.JobDetail)
            .WithIdentity($"{context.JobDetail.Key.Name}_Retry_{nextRetryCount}", context.Trigger.Key.Group)
            .StartAt(DateTimeOffset.UtcNow.Add(backoffDelay))
            .UsingJobData(retryJobData)
            .Build();

        // Remove stale retry trigger if it exists from a previous failure cycle
        // (e.g., job failed yesterday, retry trigger was created but never cleaned up)
        var triggerKey = retryTrigger.Key;
        if (await scheduler.GetTrigger(triggerKey, cancellationToken) != null)
        {
            await scheduler.UnscheduleJob(triggerKey, cancellationToken);
        }

        await scheduler.ScheduleJob(retryTrigger, cancellationToken);

        logger.LogInformation(
            "Scheduled retry {RetryCount}/{MaxRetries} for job {JobName} at {RetryTime}",
            nextRetryCount,
            RetryConstants.MaxRetries,
            context.JobDetail.Key.Name,
            retryTrigger.StartTimeUtc);
    }
}
