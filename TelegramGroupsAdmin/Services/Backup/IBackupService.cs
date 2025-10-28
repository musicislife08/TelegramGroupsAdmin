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

    /// <summary>
    /// Rotate backup encryption passphrase by queuing a background job.
    /// The job will re-encrypt all existing backups with the new passphrase using atomic file operations.
    /// Progress updates sent via SignalR if user stays on page, final notification if they navigate away.
    /// </summary>
    /// <param name="backupDirectory">Directory containing backups (default: /data/backups)</param>
    /// <param name="userId">User initiating rotation (for audit logging) - web user GUID</param>
    /// <returns>The newly generated passphrase (user must save this)</returns>
    Task<string> RotatePassphraseAsync(string backupDirectory, string userId);

    /// <summary>
    /// Sets up initial backup encryption configuration with a new passphrase.
    /// Creates new BackupEncryptionConfig with CreatedAt timestamp.
    /// Encrypts passphrase using Data Protection before storing in database.
    /// </summary>
    /// <param name="passphrase">Plain-text passphrase (will be encrypted before storage)</param>
    Task SaveEncryptionConfigAsync(string passphrase);

    /// <summary>
    /// Updates existing backup encryption configuration with a new passphrase.
    /// Updates BackupEncryptionConfig with LastRotatedAt timestamp.
    /// Encrypts passphrase using Data Protection before storing in database.
    /// </summary>
    /// <param name="passphrase">New plain-text passphrase (will be encrypted before storage)</param>
    Task UpdateEncryptionConfigAsync(string passphrase);

    /// <summary>
    /// Gets the current decrypted backup encryption passphrase from database.
    /// Used by rotation job to decrypt existing backups before re-encrypting.
    /// </summary>
    /// <returns>Decrypted passphrase</returns>
    /// <exception cref="InvalidOperationException">If encryption not configured</exception>
    Task<string> GetDecryptedPassphraseAsync();
}
