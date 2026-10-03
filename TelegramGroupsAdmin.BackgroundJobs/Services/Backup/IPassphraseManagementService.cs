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
    /// Schedules re-encryption of existing backups with <paramref name="newPassphrase"/>, which must be
    /// the passphrase the user was shown and saved. The stored passphrase changes only once every backup
    /// has been re-encrypted.
    /// </summary>
    Task RotatePassphraseAsync(string newPassphrase, string backupDirectory, string userId);
}
