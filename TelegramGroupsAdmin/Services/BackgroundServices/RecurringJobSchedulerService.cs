using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCrontab;
using System.Text.Json;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

namespace TelegramGroupsAdmin.Services.BackgroundServices;

/// <summary>
/// Generic scheduler for recurring background jobs
/// Reads job configurations and schedules them via TickerQ
/// Supports both interval-based (friendly durations like "30m", "1h") and cron-based schedules
/// </summary>
public class RecurringJobSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringJobSchedulerService> _logger;

    public RecurringJobSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<RecurringJobSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecurringJobSchedulerService started");

        // Wait for app to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Check every 1 minute for jobs that need scheduling
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckAndScheduleJobsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RecurringJobSchedulerService stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RecurringJobSchedulerService encountered fatal error");
        }
    }

    private async Task CheckAndScheduleJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var jobConfig = scope.ServiceProvider.GetRequiredService<IBackgroundJobConfigService>();

            var jobs = await jobConfig.GetAllJobsAsync(ct);
            var now = DateTimeOffset.UtcNow;

            foreach (var job in jobs.Values.Where(j => j.Enabled))
            {
                // Skip if not time to run yet
                if (job.NextRunAt.HasValue && job.NextRunAt.Value > now)
                    continue;

                // Calculate next run time
                DateTimeOffset nextRun;
                if (job.ScheduleType == "interval" && !string.IsNullOrEmpty(job.IntervalDuration))
                {
                    if (!TimeSpanUtilities.TryParseDuration(job.IntervalDuration, out var interval))
                    {
                        _logger.LogWarning("Invalid interval duration for {JobName}: {Duration}",
                            job.JobName, job.IntervalDuration);
                        continue;
                    }
                    nextRun = now.Add(interval);
                }
                else if (job.ScheduleType == "cron" && !string.IsNullOrEmpty(job.CronExpression))
                {
                    try
                    {
                        var cron = CrontabSchedule.Parse(job.CronExpression);
                        nextRun = cron.GetNextOccurrence(now.UtcDateTime);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Invalid cron expression for {JobName}: {Cron}",
                            job.JobName, job.CronExpression);
                        continue;
                    }
                }
                else
                {
                    _logger.LogWarning("Job {JobName} has invalid schedule configuration", job.JobName);
                    continue;
                }

                // Build payload based on job type
                object payload = job.JobName switch
                {
                    BackgroundJobNames.ChatHealthCheck => new ChatHealthCheckPayload { ChatId = null },
                    BackgroundJobNames.ScheduledBackup => BuildBackupPayload(job),
                    BackgroundJobNames.DatabaseMaintenance => BuildMaintenancePayload(job),
                    BackgroundJobNames.UserPhotoRefresh => BuildUserPhotoRefreshPayload(job),
                    BackgroundJobNames.BlocklistSync => BuildBlocklistSyncPayload(),
                    BackgroundJobNames.MessageCleanup => null!, // BackgroundService, not a TickerQ job
                    _ => new { }
                };

                // Skip message cleanup (it's a BackgroundService, not a TickerQ job)
                if (job.JobName == BackgroundJobNames.MessageCleanup)
                    continue;

                // Schedule via TickerQ
                var jobId = await TickerQUtilities.ScheduleJobAsync(
                    _serviceProvider,
                    _logger,
                    job.JobName,
                    payload,
                    delaySeconds: 0);

                if (jobId.HasValue)
                {
                    _logger.LogInformation("Scheduled {JobName} for execution, next run: {NextRun}",
                        job.JobName, nextRun);

                    // Update next run time
                    await jobConfig.UpdateJobStatusAsync(job.JobName, now, nextRun, null, ct);
                }
                else
                {
                    _logger.LogWarning("Failed to schedule {JobName}", job.JobName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking and scheduling jobs");
        }
    }

    // Payload builders (same as BackgroundJobs.razor)
    private static ScheduledBackupPayload BuildBackupPayload(BackgroundJobConfig job)
    {
        // Granular retention settings (5-tier)
        var retainHourly = 24;
        var retainDaily = 7;
        var retainWeekly = 4;
        var retainMonthly = 12;
        var retainYearly = 3;
        string? backupDir = null;

        if (job.Settings != null)
        {
            if (job.Settings.TryGetValue(BackgroundJobSettings.RetainHourlyBackups, out var hourly))
                retainHourly = Convert.ToInt32(((JsonElement)hourly).GetInt32());
            if (job.Settings.TryGetValue(BackgroundJobSettings.RetainDailyBackups, out var daily))
                retainDaily = Convert.ToInt32(((JsonElement)daily).GetInt32());
            if (job.Settings.TryGetValue(BackgroundJobSettings.RetainWeeklyBackups, out var weekly))
                retainWeekly = Convert.ToInt32(((JsonElement)weekly).GetInt32());
            if (job.Settings.TryGetValue(BackgroundJobSettings.RetainMonthlyBackups, out var monthly))
                retainMonthly = Convert.ToInt32(((JsonElement)monthly).GetInt32());
            if (job.Settings.TryGetValue(BackgroundJobSettings.RetainYearlyBackups, out var yearly))
                retainYearly = Convert.ToInt32(((JsonElement)yearly).GetInt32());
            if (job.Settings.TryGetValue(BackgroundJobSettings.BackupDirectory, out var dir))
                backupDir = ((JsonElement)dir).GetString();
        }

        return new ScheduledBackupPayload
        {
            RetainHourlyBackups = retainHourly,
            RetainDailyBackups = retainDaily,
            RetainWeeklyBackups = retainWeekly,
            RetainMonthlyBackups = retainMonthly,
            RetainYearlyBackups = retainYearly,
            BackupDirectory = backupDir
        };
    }

    private static DatabaseMaintenancePayload BuildMaintenancePayload(BackgroundJobConfig job)
    {
        var runVacuum = true;
        var runAnalyze = true;

        if (job.Settings != null)
        {
            if (job.Settings.TryGetValue(BackgroundJobSettings.RunVacuum, out var vacuum))
                runVacuum = ((JsonElement)vacuum).GetBoolean();
            if (job.Settings.TryGetValue(BackgroundJobSettings.RunAnalyze, out var analyze))
                runAnalyze = ((JsonElement)analyze).GetBoolean();
        }

        return new DatabaseMaintenancePayload
        {
            RunVacuum = runVacuum,
            RunAnalyze = runAnalyze,
            RunVacuumFull = false
        };
    }

    private static RefreshUserPhotosPayload BuildUserPhotoRefreshPayload(BackgroundJobConfig job)
    {
        var daysBack = 7;

        if (job.Settings != null)
        {
            if (job.Settings.TryGetValue(BackgroundJobSettings.DaysBack, out var days))
                daysBack = Convert.ToInt32(((JsonElement)days).GetInt32());
        }

        return new RefreshUserPhotosPayload
        {
            DaysBack = daysBack
        };
    }

    private static BlocklistSyncJobPayload BuildBlocklistSyncPayload()
    {
        return new BlocklistSyncJobPayload(
            SubscriptionId: null, // Sync all
            ChatId: 0, // Global
            ForceRebuild: false
        );
    }
}
