using HumanCron.Quartz.Abstractions;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Default <see cref="IQuartzScheduleSynchronizer"/>. The scheduler is supplied as a
/// per-call parameter — this service holds no scheduler-related lifecycle state, which
/// is what makes it directly testable with a real RAM-store scheduler.
/// </summary>
public class QuartzScheduleSynchronizer(
    ILogger<QuartzScheduleSynchronizer> logger,
    IBackgroundJobConfigService jobConfigService,
    IQuartzScheduleConverter scheduleConverter) : IQuartzScheduleSynchronizer
{
    public async Task SyncAsync(IScheduler scheduler, CancellationToken cancellationToken)
    {
        logger.LogInformation("Syncing job schedules from database to Quartz...");

        var allJobs = await jobConfigService.GetAllJobsAsync(cancellationToken);

        logger.LogInformation("Found {JobCount} job configurations in database", allJobs.Count);

        foreach (var (jobName, config) in allJobs)
        {
            try
            {
                var jobKey = new JobKey(jobName);

                var jobExists = await scheduler.CheckExists(jobKey, cancellationToken);
                if (!jobExists)
                {
                    logger.LogWarning(
                        "Job {JobName} found in database config but not registered in Quartz. Skipping.",
                        jobName);
                    continue;
                }

                var triggerKey = new TriggerKey($"{jobName}_Trigger", "ScheduledJobs");

                if (config.Enabled && !string.IsNullOrEmpty(config.Schedule))
                {
                    await CreateOrUpdateTriggerAsync(scheduler, jobKey, triggerKey, config, cancellationToken);
                }
                else
                {
                    await RemoveTriggerIfExistsAsync(scheduler, triggerKey, jobName, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Swallow per-job failures so one bad config doesn't abort the whole sync pass.
                logger.LogError(ex, "Failed to sync schedule for job {JobName}", jobName);
            }
        }

        // Clean up orphaned Quartz jobs whose types were removed (e.g., after job merges/renames).
        // Use BackgroundJobNames.AllRegisteredNames — the authoritative CLR-registered set — instead
        // of allJobs.Keys (DB-config keyset). This prevents legitimate ad-hoc jobs (DeleteMessage,
        // FileScan, etc.) from being deleted on every startup. (#459)
        await RemoveOrphanedJobsAsync(scheduler, BackgroundJobNames.AllRegisteredNames, cancellationToken);

        logger.LogInformation("Job schedule sync complete");
    }

    public async Task UpdateNextRunTimesAsync(IScheduler scheduler, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating NextRunAt from Quartz scheduler...");

        var allJobs = await jobConfigService.GetAllJobsAsync(cancellationToken);

        foreach (var (jobName, config) in allJobs)
        {
            try
            {
                var triggerKey = new TriggerKey($"{jobName}_Trigger", "ScheduledJobs");
                var trigger = await scheduler.GetTrigger(triggerKey, cancellationToken);

                if (trigger != null)
                {
                    var nextFireTime = trigger.GetNextFireTimeUtc();
                    if (nextFireTime.HasValue)
                    {
                        config.NextRunAt = nextFireTime.Value.UtcDateTime;
                        await jobConfigService.UpdateJobConfigAsync(jobName, config, cancellationToken: cancellationToken);

                        var nextFireTimeFormatted = FormatNextFireTime(trigger, nextFireTime);
                        logger.LogDebug(
                            "Updated NextRunAt for {JobName}: {NextRun}",
                            jobName,
                            nextFireTimeFormatted);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update NextRunAt for job {JobName}", jobName);
            }
        }

        logger.LogInformation("NextRunAt update complete");
    }

    /// <summary>
    /// Removes Quartz jobs and triggers that reference types no longer registered.
    /// Prevents TypeLoadException on startup when jobs are renamed or merged.
    /// </summary>
    private async Task RemoveOrphanedJobsAsync(
        IScheduler scheduler,
        IReadOnlySet<string> registeredJobNames,
        CancellationToken cancellationToken)
    {
        var allQuartzJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken);

        foreach (var jobKey in allQuartzJobs)
        {
            if (!registeredJobNames.Contains(jobKey.Name))
            {
                await scheduler.DeleteJob(jobKey, cancellationToken);
                logger.LogWarning("Removed orphaned Quartz job {JobName} — type no longer registered", jobKey.Name);
            }
        }
    }

    /// <summary>
    /// Creates or updates a Quartz trigger for the specified job using NaturalCron schedule parsing.
    /// Supports both cron-based (daily, weekly) and calendar interval (every N minutes/hours) schedules.
    /// </summary>
    private async Task CreateOrUpdateTriggerAsync(
        IScheduler scheduler,
        JobKey jobKey,
        TriggerKey triggerKey,
        BackgroundJobConfig config,
        CancellationToken cancellationToken)
    {
        // ClassifierRetrainingJob trains fresh on startup, so missed cron executions
        // are not worth catching up on — use DoNothing instead of the SmartPolicy default.
        var misfireInstruction = config.JobName == BackgroundJobNames.ClassifierRetraining
            ? MisfireInstruction.CronTrigger.DoNothing
            : MisfireInstruction.SmartPolicy;

        var parseResult = scheduleConverter.CreateTriggerBuilder(config.Schedule, misfireInstruction);

        if (parseResult is not HumanCron.Models.ParseResult<TriggerBuilder>.Success successResult)
        {
            var errorMessage = parseResult is HumanCron.Models.ParseResult<TriggerBuilder>.Error errorResult
                ? errorResult.Message
                : "Unknown parse error";

            logger.LogError(
                "Failed to parse schedule '{Schedule}' for job {JobName}: {Error}. Job will not be scheduled.",
                config.Schedule,
                config.JobName,
                errorMessage);
            return;
        }

        var existingTrigger = await scheduler.GetTrigger(triggerKey, cancellationToken);

        if (existingTrigger != null)
        {
            if (existingTrigger.JobDataMap.TryGetString("NaturalLanguageSchedule", out var existingSchedule) &&
                existingSchedule == config.Schedule)
            {
                logger.LogDebug(
                    "Trigger for {JobName} already exists with same schedule '{Schedule}', skipping",
                    config.JobName,
                    config.Schedule);
                return;
            }

            logger.LogInformation(
                "Schedule changed for {JobName} from '{OldSchedule}' to '{NewSchedule}', updating trigger",
                config.JobName,
                existingSchedule ?? "unknown",
                config.Schedule);
            await scheduler.UnscheduleJob(triggerKey, cancellationToken);
        }

        var trigger = successResult.Value
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithDescription($"Scheduled trigger for {config.DisplayName}")
            .UsingJobData("NaturalLanguageSchedule", config.Schedule) // persisted so the next sync can detect schedule changes
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken);

        var nextFireTime = trigger.GetNextFireTimeUtc();
        var nextFireTimeFormatted = FormatNextFireTime(trigger, nextFireTime);
        logger.LogInformation(
            "Scheduled {JobName} with schedule '{Schedule}'. Next run: {NextRun}",
            config.JobName,
            config.Schedule,
            nextFireTimeFormatted);
    }

    /// <summary>
    /// Removes a trigger if it exists (for disabled jobs).
    /// </summary>
    private async Task RemoveTriggerIfExistsAsync(
        IScheduler scheduler,
        TriggerKey triggerKey,
        string jobName,
        CancellationToken cancellationToken)
    {
        var triggerExists = await scheduler.CheckExists(triggerKey, cancellationToken);
        if (triggerExists)
        {
            await scheduler.UnscheduleJob(triggerKey, cancellationToken);
            logger.LogInformation(
                "Removed trigger for disabled job {JobName}",
                jobName);
        }
    }

    /// <summary>
    /// Format next fire time in local timezone for display.
    /// Converts UTC to trigger's timezone if available, otherwise system local time.
    /// </summary>
    private static string FormatNextFireTime(ITrigger trigger, DateTimeOffset? nextFireTimeUtc)
    {
        if (!nextFireTimeUtc.HasValue)
            return "unknown";

        TimeZoneInfo? triggerTimeZone = trigger switch
        {
            ICronTrigger cronTrigger => cronTrigger.TimeZone,
            ICalendarIntervalTrigger calendarTrigger => calendarTrigger.TimeZone,
            _ => null
        };

        var localTime = triggerTimeZone != null
            ? TimeZoneInfo.ConvertTimeFromUtc(nextFireTimeUtc.Value.UtcDateTime, triggerTimeZone)
            : nextFireTimeUtc.Value.ToLocalTime();

        return localTime.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }
}
