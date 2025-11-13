using System.Diagnostics;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Services.Backup;
using TelegramGroupsAdmin.Telegram.Abstractions;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Job logic to automatically backup database on a cron schedule
/// Saves backups to disk and manages retention (deletes old backups)
/// </summary>
public class ScheduledBackupJobLogic
{
    private readonly ILogger<ScheduledBackupJobLogic> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledBackupJobLogic(
        ILogger<ScheduledBackupJobLogic> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task ExecuteAsync(ScheduledBackupPayload payload, CancellationToken cancellationToken)
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
                    _logger.LogError("ScheduledBackupJobLogic received null payload");
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
