namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Service for creating and restoring full system backups
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Export all system data to a gzip-compressed JSON file
    /// </summary>
    /// <returns>Compressed backup file bytes</returns>
    Task<byte[]> ExportAsync();

    /// <summary>
    /// Restore system from backup file (WIPES ALL DATA FIRST)
    /// </summary>
    /// <param name="backupBytes">gzip backup file bytes</param>
    Task RestoreAsync(byte[] backupBytes);

    /// <summary>
    /// Get backup metadata without restoring
    /// </summary>
    /// <param name="backupBytes">gzip backup file bytes</param>
    Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes);
}
