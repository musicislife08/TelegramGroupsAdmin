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
    /// Checks if backup file is encrypted (has TGAENC or TGAEC2 magic header)
    /// </summary>
    /// <param name="backupBytes">Backup file bytes</param>
    /// <returns>True if encrypted, false if unencrypted format</returns>
    bool IsEncrypted(byte[] backupBytes);

    /// <summary>
    /// Encrypts backup data using chunked AEAD streaming encryption (1 MB chunks).
    /// Writes the chunked format header followed by individually encrypted chunks.
    /// Peak memory usage is ~2 MB regardless of backup size.
    /// </summary>
    /// <param name="plaintext">Stream containing unencrypted backup data</param>
    /// <param name="cipherOutput">Stream to write encrypted output to</param>
    /// <param name="passphrase">User passphrase (min 12 chars recommended)</param>
    void EncryptBackup(Stream plaintext, Stream cipherOutput, string passphrase);

    /// <summary>
    /// Decrypts backup data from a stream. Auto-detects chunked (TGAEC2) vs legacy (TGAENC) format.
    /// For chunked format, decrypts one chunk at a time for constant memory usage.
    /// For legacy format, falls back to single-shot decryption.
    /// </summary>
    /// <param name="cipherInput">Stream containing encrypted backup data</param>
    /// <param name="plainOutput">Stream to write decrypted output to</param>
    /// <param name="passphrase">User passphrase used during encryption</param>
    /// <exception cref="System.Security.Cryptography.CryptographicException">If passphrase is incorrect or data corrupted</exception>
    void DecryptBackup(Stream cipherInput, Stream plainOutput, string passphrase);

    /// <summary>
    /// Checks if a stream contains encrypted backup data by reading the magic header bytes.
    /// Restores the stream position after reading.
    /// </summary>
    /// <param name="input">Stream to check (position is saved and restored)</param>
    /// <returns>True if encrypted (TGAENC or TGAEC2 header), false otherwise</returns>
    bool IsEncrypted(Stream input);
}
