namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Service for creating and restoring full system backups
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Export all system data to a tar.gz file streamed directly to disk.
    /// Uses passphrase from database config for encryption.
    /// Writes to a temp file first, then atomically renames on success.
    /// </summary>
    /// <param name="filepath">Destination file path for the backup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExportToFileAsync(string filepath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export all system data to a tar.gz file with explicit passphrase (for CLI usage).
    /// Writes to a temp file first, then atomically renames on success.
    /// </summary>
    /// <param name="filepath">Destination file path for the backup</param>
    /// <param name="passphraseOverride">Passphrase to use (overrides DB config)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExportToFileAsync(string filepath, string passphraseOverride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore system from backup file on disk (WIPES ALL DATA FIRST).
    /// Auto-detects encryption from tar entry names. Uses passphrase from DB config.
    /// </summary>
    /// <param name="filepath">Path to backup .tar.gz file</param>
    Task RestoreAsync(string filepath);

    /// <summary>
    /// Restore system from backup file on disk with explicit passphrase.
    /// Falls back to DB config passphrase if explicit passphrase fails decryption.
    /// </summary>
    /// <param name="filepath">Path to backup .tar.gz file</param>
    /// <param name="passphrase">Passphrase to try first for decryption</param>
    Task RestoreAsync(string filepath, string passphrase);

    /// <summary>
    /// Get backup metadata by streaming from disk without loading the entire file into memory.
    /// </summary>
    /// <param name="filepath">Path to the backup .tar.gz file</param>
    Task<BackupMetadata> GetMetadataAsync(string filepath);

    /// <summary>
    /// Check if backup file on disk contains an encrypted database by streaming only the tar entry names.
    /// Avoids loading the entire file into memory.
    /// </summary>
    /// <param name="filepath">Path to the backup .tar.gz file</param>
    /// <returns>True if encrypted, false if plain</returns>
    Task<bool> IsEncryptedAsync(string filepath);

    /// <summary>
    /// Create a backup and save to disk with retention cleanup
    /// Used by both scheduled backups and manual "Backup Now" button
    /// </summary>
    /// <param name="backupDirectory">Directory to save backup</param>
    /// <param name="retentionConfig">Retention policy configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with filename, path, size, and cleanup count</returns>
    Task<BackupResult> CreateBackupWithRetentionAsync(
        string backupDirectory,
        RetentionConfig retentionConfig,
        CancellationToken cancellationToken);
}
