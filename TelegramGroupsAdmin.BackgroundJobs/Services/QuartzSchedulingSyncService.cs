using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HumanCron.Quartz.Abstractions;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Background service that syncs database job configs to Quartz.NET scheduler
/// Creates/updates/removes triggers based on BackgroundJobConfig.Schedule (natural language)
/// Phase 8: QuartzSchedulingSyncService
/// </summary>
public class QuartzSchedulingSyncService(
    ILogger<QuartzSchedulingSyncService> logger,
    ISchedulerFactory schedulerFactory,
    IBackgroundJobConfigService jobConfigService,
    IQuartzScheduleConverter scheduleConverter) : BackgroundService
{
    private readonly ILogger<QuartzSchedulingSyncService> _logger = logger;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IBackgroundJobConfigService _jobConfigService = jobConfigService;
    private readonly IQuartzScheduleConverter _scheduleConverter = scheduleConverter;

    private IScheduler? _scheduler;
    private readonly SemaphoreSlim _resyncSignal = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QuartzSchedulingSyncService starting...");

        try
        {
            // Get the scheduler instance
            _scheduler = await _schedulerFactory.GetScheduler(stoppingToken);

            // Ensure default job configs exist
            await _jobConfigService.EnsureDefaultConfigsAsync(stoppingToken);

            // Register this service with BackgroundJobConfigService for live re-sync notifications
            if (_jobConfigService is BackgroundJobConfigService configService)
            {
                configService.SetSyncService(this);
                _logger.LogDebug("Registered with BackgroundJobConfigService for live config re-sync");
            }

            // Perform initial sync on startup
            await SyncSchedulesAsync(stoppingToken);

            // Update NextRunAt for all jobs after initial sync
            await UpdateNextRunTimesAsync(stoppingToken);

            _logger.LogInformation("QuartzSchedulingSyncService initial sync complete - listening for config changes");

            // Wait for config change notifications (event-driven re-sync)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Block until TriggerResync() is called or cancellation requested
                    await _resyncSignal.WaitAsync(stoppingToken);

                    _logger.LogInformation("Config change detected - re-syncing job schedules");

                    // Re-sync all schedules
                    await SyncSchedulesAsync(stoppingToken);
                    await UpdateNextRunTimesAsync(stoppingToken);

                    _logger.LogInformation("Config re-sync complete");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during config re-sync - will retry on next change");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuartzSchedulingSyncService failed to start");
            throw;
        }
    }

    /// <summary>
    /// Trigger immediate re-sync of job schedules from database
    /// Called by BackgroundJobConfigService after configuration changes
    /// </summary>
    public void TriggerResync()
    {
        // Release the semaphore to wake up the monitoring loop
        // CurrentCount check prevents multiple releases (semaphore has maxCount=1)
        if (_resyncSignal.CurrentCount == 0)
        {
            _resyncSignal.Release();
            _logger.LogDebug("Config re-sync triggered");
        }
    }

    /// <summary>
    /// Syncs all job configurations from database to Quartz scheduler
    /// Creates triggers for enabled jobs, removes triggers for disabled jobs
    /// </summary>
    private async Task SyncSchedulesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Syncing job schedules from database to Quartz...");

        if (_scheduler == null)
        {
            _logger.LogError("Scheduler is null, cannot sync schedules");
            return;
        }

        // Get all job configs from database
        var allJobs = await _jobConfigService.GetAllJobsAsync(cancellationToken);

        _logger.LogInformation("Found {JobCount} job configurations in database", allJobs.Count);

        foreach (var (jobName, config) in allJobs)
        {
            try
            {
                // Map job name to Quartz JobKey (must match registered job identities)
                var jobKey = new JobKey(jobName);

                // Check if job exists in Quartz
                var jobExists = await _scheduler.CheckExists(jobKey, cancellationToken);
                if (!jobExists)
                {
                    _logger.LogWarning(
                        "Job {JobName} found in database config but not registered in Quartz. Skipping.",
                        jobName);
                    continue;
                }

                // Generate trigger key (unique per job)
                var triggerKey = new TriggerKey($"{jobName}_Trigger", "ScheduledJobs");

                if (config.Enabled && !string.IsNullOrEmpty(config.Schedule))
                {
                    // Create or update trigger for enabled job
                    await CreateOrUpdateTriggerAsync(jobKey, triggerKey, config, cancellationToken);
                }
                else
                {
                    // Remove trigger for disabled jobs
                    await RemoveTriggerIfExistsAsync(triggerKey, jobName, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync schedule for job {JobName}", jobName);
                // Continue processing other jobs
            }
        }

        _logger.LogInformation("Job schedule sync complete");
    }

    /// <summary>
    /// Creates or updates a Quartz trigger for the specified job using NaturalCron schedule parsing
    /// Supports both cron-based (daily, weekly) and calendar interval (every N minutes/hours) schedules
    /// </summary>
    private async Task CreateOrUpdateTriggerAsync(
        JobKey jobKey,
        TriggerKey triggerKey,
        Core.Models.BackgroundJobConfig config,
        CancellationToken cancellationToken)
    {
        if (_scheduler == null) return;

        // Create pre-configured trigger builder (schedule + start time already set)
        var parseResult = _scheduleConverter.CreateTriggerBuilder(config.Schedule);

        if (parseResult is not HumanCron.Models.ParseResult<TriggerBuilder>.Success successResult)
        {
            // Parse failed - log error and skip this job
            var errorMessage = parseResult is HumanCron.Models.ParseResult<TriggerBuilder>.Error errorResult
                ? errorResult.Message
                : "Unknown parse error";

            _logger.LogError(
                "Failed to parse schedule '{Schedule}' for job {JobName}: {Error}. Job will not be scheduled.",
                config.Schedule,
                config.JobName,
                errorMessage);
            return;
        }

        var existingTrigger = await _scheduler.GetTrigger(triggerKey, cancellationToken);

        if (existingTrigger != null)
        {
            // Check if schedule changed by comparing stored natural language schedule
            if (existingTrigger.JobDataMap.TryGetString("NaturalLanguageSchedule", out var existingSchedule) &&
                existingSchedule == config.Schedule)
            {
                _logger.LogDebug(
                    "Trigger for {JobName} already exists with same schedule '{Schedule}', skipping",
                    config.JobName,
                    config.Schedule);
                return;
            }

            // Schedule changed - remove old trigger
            _logger.LogInformation(
                "Schedule changed for {JobName} from '{OldSchedule}' to '{NewSchedule}', updating trigger",
                config.JobName,
                existingSchedule ?? "unknown",
                config.Schedule);
            await _scheduler.UnscheduleJob(triggerKey, cancellationToken);
        }

        // Complete the trigger with job-specific metadata
        var trigger = successResult.Value
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithDescription($"Scheduled trigger for {config.DisplayName}")
            .UsingJobData("NaturalLanguageSchedule", config.Schedule) // Store for change detection
            .Build();

        await _scheduler.ScheduleJob(trigger, cancellationToken);

        var nextFireTime = trigger.GetNextFireTimeUtc();
        var nextFireTimeFormatted = FormatNextFireTime(trigger, nextFireTime);
        _logger.LogInformation(
            "Scheduled {JobName} with schedule '{Schedule}'. Next run: {NextRun}",
            config.JobName,
            config.Schedule,
            nextFireTimeFormatted);
    }

    /// <summary>
    /// Removes a trigger if it exists (for disabled jobs)
    /// </summary>
    private async Task RemoveTriggerIfExistsAsync(
        TriggerKey triggerKey,
        string jobName,
        CancellationToken cancellationToken)
    {
        if (_scheduler == null) return;

        var triggerExists = await _scheduler.CheckExists(triggerKey, cancellationToken);
        if (triggerExists)
        {
            await _scheduler.UnscheduleJob(triggerKey, cancellationToken);
            _logger.LogInformation(
                "Removed trigger for disabled job {JobName}",
                jobName);
        }
    }

    /// <summary>
    /// Updates NextRunAt for all jobs by querying Quartz scheduler
    /// Called after initial sync and after config changes
    /// </summary>
    private async Task UpdateNextRunTimesAsync(CancellationToken cancellationToken)
    {
        if (_scheduler == null) return;

        _logger.LogInformation("Updating NextRunAt from Quartz scheduler...");

        var allJobs = await _jobConfigService.GetAllJobsAsync(cancellationToken);

        foreach (var (jobName, config) in allJobs)
        {
            try
            {
                var triggerKey = new TriggerKey($"{jobName}_Trigger", "ScheduledJobs");
                var trigger = await _scheduler.GetTrigger(triggerKey, cancellationToken);

                if (trigger != null)
                {
                    var nextFireTime = trigger.GetNextFireTimeUtc();
                    if (nextFireTime.HasValue)
                    {
                        config.NextRunAt = nextFireTime.Value.UtcDateTime;
                        await _jobConfigService.UpdateJobConfigAsync(jobName, config, cancellationToken);

                        var nextFireTimeFormatted = FormatNextFireTime(trigger, nextFireTime);
                        _logger.LogDebug(
                            "Updated NextRunAt for {JobName}: {NextRun}",
                            jobName,
                            nextFireTimeFormatted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update NextRunAt for job {JobName}", jobName);
            }
        }

        _logger.LogInformation("NextRunAt update complete");
    }

    /// <summary>
    /// Format next fire time in local timezone for display
    /// Converts UTC to trigger's timezone if available, otherwise system local time
    /// </summary>
    private static string FormatNextFireTime(ITrigger trigger, DateTimeOffset? nextFireTimeUtc)
    {
        if (!nextFireTimeUtc.HasValue)
            return "unknown";

        // Get trigger's timezone if it's a cron or calendar interval trigger
        TimeZoneInfo? triggerTimeZone = trigger switch
        {
            ICronTrigger cronTrigger => cronTrigger.TimeZone,
            ICalendarIntervalTrigger calendarTrigger => calendarTrigger.TimeZone,
            _ => null
        };

        // Convert UTC to trigger's timezone (or local if not specified)
        var localTime = triggerTimeZone != null
            ? TimeZoneInfo.ConvertTimeFromUtc(nextFireTimeUtc.Value.UtcDateTime, triggerTimeZone)
            : nextFireTimeUtc.Value.ToLocalTime();

        // Format with timezone offset
        return localTime.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QuartzSchedulingSyncService stopping...");
        await base.StopAsync(cancellationToken);
    }
}
