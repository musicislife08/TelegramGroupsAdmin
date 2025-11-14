namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Service for encrypting and decrypting backup files
/// </summary>
public interface IBackupEncryptionService
{
    /// <summary>
    /// Encrypts backup JSON with passphrase-derived AES-256-GCM key
    /// </summary>
    /// <param name="jsonBytes">Unencrypted backup JSON data</param>
    /// <param name="passphrase">User passphrase (min 12 chars recommended)</param>
    /// <returns>Encrypted backup with magic header, salt, and nonce</returns>
    byte[] EncryptBackup(byte[] jsonBytes, string passphrase);

    /// <summary>
    /// Decrypts encrypted backup file with passphrase
    /// </summary>
    /// <param name="encryptedBytes">Encrypted backup file</param>
    /// <param name="passphrase">User passphrase used during encryption</param>
    /// <returns>Decrypted JSON backup data</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">If passphrase is incorrect or data corrupted</exception>
    byte[] DecryptBackup(byte[] encryptedBytes, string passphrase);

    /// <summary>
    /// Checks if backup file is encrypted (has TGAENC magic header)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <returns>True if encrypted, false if unencrypted format</returns>
    bool IsEncrypted(byte[] backupBytes);
}
