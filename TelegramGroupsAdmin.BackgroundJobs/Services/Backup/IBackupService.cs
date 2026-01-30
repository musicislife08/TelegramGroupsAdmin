namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Service for creating and restoring full system backups
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Export all system data to a gzip-compressed (and optionally encrypted) JSON file
    /// Uses passphrase from database config if encryption is enabled
    /// </summary>
    /// <returns>Compressed (and optionally encrypted) backup file bytes</returns>
    Task<byte[]> ExportAsync();

    /// <summary>
    /// Export all system data with explicit passphrase (for CLI usage)
    /// </summary>
    /// <param name="passphraseOverride">Passphrase to use (overrides DB config)</param>
    /// <returns>Encrypted and compressed backup file bytes</returns>
    Task<byte[]> ExportAsync(string passphraseOverride);

    /// <summary>
    /// Restore system from backup file (WIPES ALL DATA FIRST)
    /// Auto-detects encryption and uses passphrase from DB config
    /// </summary>
    /// <param name="backupBytes">Backup file bytes (gzipped or encrypted)</param>
    Task RestoreAsync(byte[] backupBytes);

    /// <summary>
    /// Restore system from backup file with explicit passphrase (for CLI/first-run usage)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <param name="passphrase">Passphrase to decrypt (if encrypted)</param>
    Task RestoreAsync(byte[] backupBytes, string passphrase);

    /// <summary>
    /// Get backup metadata without restoring.
    /// Metadata is always unencrypted in the tar archive — no passphrase needed.
    /// </summary>
    /// <param name="backupBytes">gzip backup file bytes</param>
    Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes);

    /// <summary>
    /// Get backup metadata by streaming from disk without loading the entire file into memory.
    /// </summary>
    /// <param name="filepath">Path to the backup .tar.gz file</param>
    Task<BackupMetadata> GetMetadataAsync(string filepath);

    /// <summary>
    /// Check if backup contains an encrypted database by inspecting tar entries.
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <returns>True if encrypted, false if plain</returns>
    Task<bool> IsEncryptedAsync(byte[] backupBytes);

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

/// <summary>
/// Result of creating a backup with retention cleanup
/// </summary>
public record BackupResult(
    string Filename,
    string FilePath,
    long SizeBytes,
    int DeletedCount);
