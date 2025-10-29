namespace TelegramGroupsAdmin.Services.Backup;

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
    /// Check if backup file is encrypted (checks for TGAENC magic header)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <returns>True if encrypted, false if plain gzip</returns>
    Task<bool> IsEncryptedAsync(byte[] backupBytes);
}
