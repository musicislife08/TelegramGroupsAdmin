using Microsoft.Extensions.Logging;
using Quartz;
using System.Text.Json;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Implementation of manual job triggering service
/// Creates SimpleTriggers for immediate execution
/// Phase 9: Manual trigger API
/// </summary>
public class JobTriggerService(
    ILogger<JobTriggerService> logger,
    ISchedulerFactory schedulerFactory) : IJobTriggerService
{
    public async Task<string> TriggerNowAsync(string jobName, object payload, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
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
            { JobDataKeys.PayloadJson, payloadJson }
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

        logger.LogInformation(
            "Triggered job {JobName} immediately with trigger {TriggerId}",
            jobName,
            triggerId);

        return triggerId;
    }

}
