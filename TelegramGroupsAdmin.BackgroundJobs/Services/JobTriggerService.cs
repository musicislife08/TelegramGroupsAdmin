using Microsoft.Extensions.Logging;
using Quartz;
using System.Text.Json;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Implementation of manual job triggering service
/// Creates SimpleTriggers for immediate or scheduled one-time execution
/// Phase 9: Manual trigger API
/// </summary>
public class JobTriggerService(
    ILogger<JobTriggerService> logger,
    ISchedulerFactory schedulerFactory) : IJobTriggerService
{
    private readonly ILogger<JobTriggerService> _logger = logger;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;

    public async Task<string> TriggerNowAsync(string jobName, object payload, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = new JobKey(jobName);

        // Verify job exists
        var jobExists = await scheduler.CheckExists(jobKey, cancellationToken);
        if (!jobExists)
        {
            throw new InvalidOperationException($"Job '{jobName}' is not registered in Quartz scheduler");
        }

        // Create unique trigger ID
        var triggerId = $"{jobName}_Manual_{Guid.NewGuid():N}";
        var triggerKey = new TriggerKey(triggerId, "ManualTriggers");

        // Build job data map with payload (serialize to JSON string for Quartz compatibility)
        var payloadJson = JsonSerializer.Serialize(payload);
        var jobData = new JobDataMap
        {
            { "payload", payloadJson }
        };

        // Create trigger that fires immediately
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow() // Fire immediately
            .UsingJobData(jobData)
            .WithDescription($"Manual trigger for {jobName}")
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken);

        _logger.LogInformation(
            "Triggered job {JobName} immediately with trigger {TriggerId}",
            jobName,
            triggerId);

        return triggerId;
    }

    public async Task<string> ScheduleOnceAsync(
        string jobName,
        object payload,
        DateTimeOffset runAt,
        CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var jobKey = new JobKey(jobName);

        // Verify job exists
        var jobExists = await scheduler.CheckExists(jobKey, cancellationToken);
        if (!jobExists)
        {
            throw new InvalidOperationException($"Job '{jobName}' is not registered in Quartz scheduler");
        }

        // Validate run time is in the future
        if (runAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("Run time must be in the future", nameof(runAt));
        }

        // Create unique trigger ID
        var triggerId = $"{jobName}_Scheduled_{Guid.NewGuid():N}";
        var triggerKey = new TriggerKey(triggerId, "ScheduledOnceTriggers");

        // Build job data map with payload (serialize to JSON string for Quartz compatibility)
        var payloadJson = JsonSerializer.Serialize(payload);
        var jobData = new JobDataMap
        {
            { "payload", payloadJson }
        };

        // Create trigger that fires once at specified time
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(runAt)
            .UsingJobData(jobData)
            .WithDescription($"Scheduled once trigger for {jobName} at {runAt:yyyy-MM-dd HH:mm:ss UTC}")
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken);

        _logger.LogInformation(
            "Scheduled job {JobName} to run once at {RunAt} with trigger {TriggerId}",
            jobName,
            runAt.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            triggerId);

        return triggerId;
    }

    public async Task<bool> CancelScheduledJobAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        // Try both trigger groups (manual and scheduled once)
        var triggerKey1 = new TriggerKey(triggerId, "ManualTriggers");
        var triggerKey2 = new TriggerKey(triggerId, "ScheduledOnceTriggers");

        var cancelled1 = await scheduler.UnscheduleJob(triggerKey1, cancellationToken);
        var cancelled2 = await scheduler.UnscheduleJob(triggerKey2, cancellationToken);

        var cancelled = cancelled1 || cancelled2;

        if (cancelled)
        {
            _logger.LogInformation("Cancelled trigger {TriggerId}", triggerId);
        }
        else
        {
            _logger.LogWarning("Trigger {TriggerId} not found or already completed", triggerId);
        }

        return cancelled;
    }
}
