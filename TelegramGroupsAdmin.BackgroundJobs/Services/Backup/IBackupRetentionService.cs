namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Service for managing backup retention with grandfather-father-son strategy.
/// </summary>
public interface IBackupRetentionService
{
    /// <summary>
    /// Determines which backup files should be deleted based on retention policy.
    /// Uses grandfather-father-son (5-tier) strategy.
    /// </summary>
    /// <param name="backupFiles">All backup files with their metadata.</param>
    /// <param name="retentionConfig">Retention configuration.</param>
    /// <returns>List of backup files to delete.</returns>
    List<BackupFileInfo> GetBackupsToDelete(List<BackupFileInfo> backupFiles, RetentionConfig retentionConfig);

    /// <summary>
    /// Determines the primary (highest) tier for a backup and whether it will be kept.
    /// </summary>
    /// <param name="backup">The backup to analyze.</param>
    /// <param name="allBackups">All backups for context.</param>
    /// <param name="retentionConfig">Retention configuration.</param>
    /// <returns>Retention info including tier and keep status.</returns>
    BackupRetentionInfo GetBackupRetentionInfo(
        BackupFileInfo backup, List<BackupFileInfo> allBackups, RetentionConfig retentionConfig);
}
