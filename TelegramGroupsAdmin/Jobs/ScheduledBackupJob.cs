using TelegramGroupsAdmin.Services.Backup;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Scheduled job to automatically backup database on a cron schedule
/// Saves backups to disk and manages retention (deletes old backups)
/// </summary>
public class ScheduledBackupJob
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

    [TickerFunction("scheduled_backup")]
    public async Task ExecuteAsync(TickerFunctionContext<ScheduledBackupPayload> context, CancellationToken cancellationToken)
    {
        try
        {
            var payload = context.Request;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled backup");
            throw; // Re-throw for TickerQ retry logic
        }
    }
}
