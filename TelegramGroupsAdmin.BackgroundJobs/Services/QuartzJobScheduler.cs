using Microsoft.Extensions.Logging;
using Quartz;
using System.Text.Json;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Quartz.NET implementation of ad-hoc job scheduling
/// Schedules one-time jobs with payloads serialized to JobDataMap
/// </summary>
public class QuartzJobScheduler : IJobScheduler
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzJobScheduler> _logger;

    public QuartzJobScheduler(
        ISchedulerFactory schedulerFactory,
        ILogger<QuartzJobScheduler> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task<string> ScheduleJobAsync<TPayload>(
        string jobName,
        TPayload payload,
        int delaySeconds,
        CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        // Generate unique job ID (includes timestamp for uniqueness)
        var jobId = $"{jobName}_{Guid.NewGuid():N}";

        // Serialize payload to JSON
        var payloadJson = JsonSerializer.Serialize(payload);

        // Create job detail with payload in JobDataMap
        var job = JobBuilder.Create(GetJobType(jobName))
            .WithIdentity(jobId, "AdHoc")
            .UsingJobData(JobDataKeys.PayloadJson, payloadJson)
            .UsingJobData(JobDataKeys.PayloadType, typeof(TPayload).AssemblyQualifiedName!)
            .Build();

        // Create trigger with delay
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobId}_trigger", "AdHoc")
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(delaySeconds))
            .Build();

        // Schedule the job
        await scheduler.ScheduleJob(job, trigger, cancellationToken);

        _logger.LogDebug("Scheduled ad-hoc job {JobName} with ID {JobId} (delay: {DelaySeconds}s)",
            jobName, jobId, delaySeconds);

        return jobId;
    }

    public async Task<bool> CancelJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        var jobKey = new JobKey(jobId, "AdHoc");
        var deleted = await scheduler.DeleteJob(jobKey, cancellationToken);

        if (deleted)
        {
            _logger.LogDebug("Cancelled ad-hoc job {JobId}", jobId);
        }
        else
        {
            _logger.LogDebug("Could not cancel job {JobId} (already executed or not found)", jobId);
        }

        return deleted;
    }

    public async Task<bool> IsScheduledAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        var jobKey = new JobKey(jobId, "AdHoc");
        return await scheduler.CheckExists(jobKey, cancellationToken);
    }

    /// <summary>
    /// Map job name to IJob implementation type
    /// Uses BackgroundJobNames constants for compile-time safety
    /// </summary>
    private static Type GetJobType(string jobName)
    {
        return jobName switch
        {
            BackgroundJobNames.ChatHealthCheck => typeof(Jobs.ChatHealthCheckJob),
            BackgroundJobNames.DeleteMessage => typeof(Jobs.DeleteMessageJob),
            BackgroundJobNames.DeleteUserMessages => typeof(Jobs.DeleteUserMessagesJob),
            BackgroundJobNames.FetchUserPhoto => typeof(Jobs.FetchUserPhotoJob),
            BackgroundJobNames.FileScan => typeof(Jobs.FileScanJob),
            BackgroundJobNames.WelcomeTimeout => typeof(Jobs.WelcomeTimeoutJob),
            BackgroundJobNames.TempbanExpiry => typeof(Jobs.TempbanExpiryJob),
            BackgroundJobNames.RotateBackupPassphrase => typeof(Jobs.RotateBackupPassphraseJob),
            _ => throw new ArgumentException($"Unknown job name: {jobName}", nameof(jobName))
        };
    }
}
