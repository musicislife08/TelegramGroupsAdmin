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
            var retentionService = scope.ServiceProvider.GetRequiredService<BackupRetentionService>();

            // Generate backup
            var backupBytes = await backupService.ExportAsync();
            var backupSizeMb = backupBytes.Length / 1024.0 / 1024.0;

            // Determine backup directory
            var backupDir = payload.BackupDirectory ?? Path.Combine("data", "backups");
            Directory.CreateDirectory(backupDir); // Ensure directory exists

            // Save backup with timestamp (updated extension to .tar.gz for encrypted backups)
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"backup_{timestamp}.tar.gz";
            var filepath = Path.Combine(backupDir, filename);

            await File.WriteAllBytesAsync(filepath, backupBytes, cancellationToken);
            _logger.LogInformation("Backup saved to {Filepath} ({SizeMB:F2} MB)", filepath, backupSizeMb);

            // Clean up old backups using granular retention strategy
            var backupFiles = Directory.GetFiles(backupDir, "backup_*.tar.gz")
                .Select(f => new BackupFileInfo
                {
                    FilePath = f,
                    CreatedAt = File.GetCreationTimeUtc(f),
                    FileSizeBytes = new FileInfo(f).Length
                })
                .ToList();

            var retentionConfig = new RetentionConfig
            {
                RetainHourlyBackups = payload.RetainHourlyBackups,
                RetainDailyBackups = payload.RetainDailyBackups,
                RetainWeeklyBackups = payload.RetainWeeklyBackups,
                RetainMonthlyBackups = payload.RetainMonthlyBackups,
                RetainYearlyBackups = payload.RetainYearlyBackups
            };

            var toDelete = retentionService.GetBackupsToDelete(backupFiles, retentionConfig);
            var deletedCount = 0;

            foreach (var backup in toDelete)
            {
                try
                {
                    File.Delete(backup.FilePath);
                    deletedCount++;
                    _logger.LogDebug("Deleted old backup: {Filename}", Path.GetFileName(backup.FilePath));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old backup: {Filename}", Path.GetFileName(backup.FilePath));
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {Count} old backups via granular retention policy", deletedCount);
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
