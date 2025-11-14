using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Background service that syncs database job configs to Quartz.NET scheduler
/// Creates/updates/removes triggers based on BackgroundJobConfig.CronExpression
/// Phase 8: QuartzSchedulingSyncService
/// </summary>
public class QuartzSchedulingSyncService(
    ILogger<QuartzSchedulingSyncService> logger,
    ISchedulerFactory schedulerFactory,
    IBackgroundJobConfigService jobConfigService) : BackgroundService
{
    private readonly ILogger<QuartzSchedulingSyncService> _logger = logger;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IBackgroundJobConfigService _jobConfigService = jobConfigService;

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

                if (config.Enabled && !string.IsNullOrEmpty(config.CronExpression))
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
    /// Creates or updates a Quartz trigger for the specified job
    /// </summary>
    private async Task CreateOrUpdateTriggerAsync(
        JobKey jobKey,
        TriggerKey triggerKey,
        Core.Models.BackgroundJobConfig config,
        CancellationToken cancellationToken)
    {
        if (_scheduler == null) return;

        var existingTrigger = await _scheduler.GetTrigger(triggerKey, cancellationToken);

        if (existingTrigger != null)
        {
            // Check if cron expression changed
            if (existingTrigger is ICronTrigger cronTrigger &&
                cronTrigger.CronExpressionString == config.CronExpression)
            {
                _logger.LogDebug(
                    "Trigger for {JobName} already exists with same cron expression, skipping",
                    config.JobName);
                return;
            }

            // Cron expression changed - remove old trigger
            _logger.LogInformation(
                "Cron expression changed for {JobName}, updating trigger",
                config.JobName);
            await _scheduler.UnscheduleJob(triggerKey, cancellationToken);
        }

        // Create new trigger with cron schedule
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithCronSchedule(config.CronExpression, builder =>
            {
                // Configure misfire behavior: fire immediately if missed during downtime
                builder.WithMisfireHandlingInstructionFireAndProceed();
            })
            .WithDescription($"Scheduled trigger for {config.DisplayName}")
            .Build();

        await _scheduler.ScheduleJob(trigger, cancellationToken);

        var nextFireTime = trigger.GetNextFireTimeUtc();
        _logger.LogInformation(
            "Scheduled {JobName} with cron '{CronExpression}'. Next run: {NextRun}",
            config.JobName,
            config.CronExpression,
            nextFireTime?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "unknown");
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

                        _logger.LogDebug(
                            "Updated NextRunAt for {JobName}: {NextRun}",
                            jobName,
                            nextFireTime.Value.ToString("yyyy-MM-dd HH:mm:ss UTC"));
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("QuartzSchedulingSyncService stopping...");
        await base.StopAsync(cancellationToken);
    }
}
