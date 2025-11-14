using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to automatically backup database on a cron schedule
/// Saves backups to disk and manages retention (deletes old backups)
/// </summary>
public class ScheduledBackupJob : IJob
{
    private readonly ILogger<ScheduledBackupJob> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledBackupJob(
        ILogger<ScheduledBackupJob> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        // Scheduled triggers don't have payloads, manual triggers do
        ScheduledBackupPayload payload;

        if (context.JobDetail.JobDataMap.ContainsKey("payload"))
        {
            // Manual trigger - deserialize provided payload
            var payloadJson = context.JobDetail.JobDataMap.GetString("payload")!;
            payload = JsonSerializer.Deserialize<ScheduledBackupPayload>(payloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize ScheduledBackupPayload");
        }
        else
        {
            // Scheduled trigger - use default payload (standard retention: 24h/7d/4w/12m/3y)
            payload = new ScheduledBackupPayload
            {
                RetainHourlyBackups = 24,
                RetainDailyBackups = 7,
                RetainWeeklyBackups = 4,
                RetainMonthlyBackups = 12,
                RetainYearlyBackups = 3,
                BackupDirectory = null // Uses default /data/backups
            };
        }

        await ExecuteAsync(payload, context.CancellationToken);
    }

    private async Task ExecuteAsync(ScheduledBackupPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "ScheduledBackup";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            try
            {
                if (payload == null)
                {
                    _logger.LogError("ScheduledBackupJob received null payload");
                    return;
                }

                _logger.LogInformation("Starting scheduled backup (retention: {Hourly}h/{Daily}d/{Weekly}w/{Monthly}m/{Yearly}y)",
                    payload.RetainHourlyBackups, payload.RetainDailyBackups, payload.RetainWeeklyBackups,
                    payload.RetainMonthlyBackups, payload.RetainYearlyBackups);

                using var scope = _scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

                // Determine backup directory
                var backupDir = payload.BackupDirectory ?? Path.Combine("data", "backups");

                // Build retention config
                var retentionConfig = new RetentionConfig
                {
                    RetainHourlyBackups = payload.RetainHourlyBackups,
                    RetainDailyBackups = payload.RetainDailyBackups,
                    RetainWeeklyBackups = payload.RetainWeeklyBackups,
                    RetainMonthlyBackups = payload.RetainMonthlyBackups,
                    RetainYearlyBackups = payload.RetainYearlyBackups
                };

                // Create backup with retention (shared logic)
                var result = await backupService.CreateBackupWithRetentionAsync(backupDir, retentionConfig, cancellationToken);
                var backupSizeMb = result.SizeBytes / 1024.0 / 1024.0;

                _logger.LogInformation("Backup saved to {Filepath} ({SizeMB:F2} MB)", result.FilePath, backupSizeMb);

                if (result.DeletedCount > 0)
                {
                    _logger.LogInformation("Deleted {Count} old backups via retention policy", result.DeletedCount);
                }

                _logger.LogInformation("Scheduled backup completed successfully");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled backup");
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
