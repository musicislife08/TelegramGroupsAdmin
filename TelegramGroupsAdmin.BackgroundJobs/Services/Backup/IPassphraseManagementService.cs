namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Manages backup encryption passphrases and configuration
/// Separated from IBackupService for single responsibility principle
/// </summary>
public interface IPassphraseManagementService
{
    /// <summary>
    /// Sets up initial backup encryption configuration with a passphrase
    /// </summary>
    Task SaveEncryptionConfigAsync(string passphrase);

    /// <summary>
    /// Updates existing backup encryption configuration with new passphrase
    /// </summary>
    Task UpdateEncryptionConfigAsync(string passphrase);

    /// <summary>
    /// Gets the current decrypted passphrase from database
    /// </summary>
    Task<string> GetDecryptedPassphraseAsync();

    /// <summary>
    /// Rotates the backup passphrase and schedules re-encryption of existing backups
    /// </summary>
    Task<string> RotatePassphraseAsync(string backupDirectory, string userId);
}
