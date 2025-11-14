using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Listeners;

/// <summary>
/// Job listener that implements retry logic with exponential backoff
/// Reschedules failed jobs up to MaxRetries times with increasing delays
/// Phase 7: Retry policy (3 retries with exponential backoff)
/// </summary>
public class RetryJobListener(ILogger<RetryJobListener> logger, ISchedulerFactory schedulerFactory) : IJobListener
{
    private readonly ILogger<RetryJobListener> _logger = logger;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;

    // Configuration
    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseBackoffInterval = TimeSpan.FromSeconds(10);

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
                _logger.LogInformation(
                    "Job {JobName} succeeded after previous retry attempts",
                    context.JobDetail.Key.Name);
            }
            return;
        }

        // Get current retry count (defaults to 0 for first failure)
        var retryCount = context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.RetryCount)
            ? context.JobDetail.JobDataMap.GetInt(JobDataKeys.RetryCount)
            : 0;

        if (retryCount >= MaxRetries)
        {
            _logger.LogError(
                jobException,
                "Job {JobName} failed after {MaxRetries} retry attempts. Giving up.",
                context.JobDetail.Key.Name,
                MaxRetries);
            return;
        }

        // Calculate exponential backoff delay: baseInterval * 2^retryCount
        var backoffDelay = TimeSpan.FromTicks(BaseBackoffInterval.Ticks * (long)Math.Pow(2, retryCount));
        var nextRetryCount = retryCount + 1;

        _logger.LogWarning(
            jobException,
            "Job {JobName} failed (attempt {CurrentAttempt}/{MaxAttempts}). Retrying in {Delay}...",
            context.JobDetail.Key.Name,
            nextRetryCount,
            MaxRetries + 1, // Total attempts = MaxRetries + initial attempt
            backoffDelay);

        // Create new trigger for retry with exponential backoff
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

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

        await scheduler.ScheduleJob(retryTrigger, cancellationToken);

        _logger.LogInformation(
            "Scheduled retry {RetryCount}/{MaxRetries} for job {JobName} at {RetryTime}",
            nextRetryCount,
            MaxRetries,
            context.JobDetail.Key.Name,
            retryTrigger.StartTimeUtc);
    }
}
