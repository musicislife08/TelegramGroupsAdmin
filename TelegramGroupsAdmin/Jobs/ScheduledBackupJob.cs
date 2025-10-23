using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

            _logger.LogInformation("Starting scheduled backup (retention: {RetentionDays} days)", payload.RetentionDays);

            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

            // Generate backup
            var backupBytes = await backupService.ExportAsync();
            var backupSizeMB = backupBytes.Length / 1024.0 / 1024.0;

            // Determine backup directory
            var backupDir = payload.BackupDirectory ?? Path.Combine("data", "backups");
            Directory.CreateDirectory(backupDir); // Ensure directory exists

            // Save backup with timestamp
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"backup_{timestamp}.json.gz";
            var filepath = Path.Combine(backupDir, filename);

            await File.WriteAllBytesAsync(filepath, backupBytes, cancellationToken);
            _logger.LogInformation("Backup saved to {Filepath} ({SizeMB:F2} MB)", filepath, backupSizeMB);

            // Clean up old backups (retention management)
            var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-payload.RetentionDays);
            var deletedCount = 0;

            var backupFiles = Directory.GetFiles(backupDir, "backup_*.json.gz");
            foreach (var file in backupFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < retentionCutoff.UtcDateTime)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                        _logger.LogDebug("Deleted old backup: {Filename}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old backup: {Filename}", Path.GetFileName(file));
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {Count} old backups (retention: {RetentionDays} days)", deletedCount, payload.RetentionDays);
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
