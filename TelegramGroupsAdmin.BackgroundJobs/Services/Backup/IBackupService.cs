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
    /// Get backup metadata without restoring
    /// </summary>
    /// <param name="backupBytes">gzip backup file bytes</param>
    Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes);

    /// <summary>
    /// Get backup metadata with explicit passphrase (for encrypted backups during first-run)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <param name="passphrase">Passphrase to decrypt (if encrypted)</param>
    Task<BackupMetadata> GetMetadataAsync(byte[] backupBytes, string passphrase);

    /// <summary>
    /// Check if backup file is encrypted (checks for TGAENC magic header)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <returns>True if encrypted, false if plain gzip</returns>
    Task<bool> IsEncryptedAsync(byte[] backupBytes);

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
