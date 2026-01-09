using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.BackgroundJobs.Constants;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Handles encryption and decryption of backup files using AES-256-GCM
/// </summary>
public class BackupEncryptionService : IBackupEncryptionService
{
    private readonly ILogger<BackupEncryptionService> _logger;

    // File format constants
    private static readonly byte[] MagicHeader = "TGAENC\0"u8.ToArray(); // 7 bytes

    public BackupEncryptionService(ILogger<BackupEncryptionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Encrypts backup JSON bytes with passphrase-derived key
    /// </summary>
    /// <param name="jsonBytes">Unencrypted JSON backup data</param>
    /// <param name="passphrase">User passphrase (min 12 chars recommended)</param>
    /// <returns>Encrypted backup with format: [header][salt][nonce][ciphertext+tag]</returns>
    public byte[] EncryptBackup(byte[] jsonBytes, string passphrase)
    {
        if (jsonBytes == null || jsonBytes.Length == 0)
            throw new ArgumentException("Backup data cannot be empty", nameof(jsonBytes));

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        // Generate random salt and nonce (cryptographically secure)
        var salt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSizeBytes);
        var nonce = RandomNumberGenerator.GetBytes(EncryptionConstants.NonceSizeBytes);

        // Derive encryption key from passphrase using PBKDF2
        var key = DeriveKey(passphrase, salt);

        // Encrypt using AES-256-GCM
        using var aesGcm = new AesGcm(key, EncryptionConstants.TagSizeBytes);
        var ciphertext = new byte[jsonBytes.Length];
        var tag = new byte[EncryptionConstants.TagSizeBytes];

        aesGcm.Encrypt(nonce, jsonBytes, ciphertext, tag);

        // Build final encrypted backup file
        // Format: [TGAENC\0 (7)][salt (32)][nonce (12)][ciphertext (variable)][tag (16)]
        var encryptedBackup = new byte[MagicHeader.Length + EncryptionConstants.SaltSizeBytes + EncryptionConstants.NonceSizeBytes + ciphertext.Length + EncryptionConstants.TagSizeBytes];
        var span = encryptedBackup.AsSpan();

        var offset = 0;
        MagicHeader.CopyTo(span[offset..]);
        offset += MagicHeader.Length;

        salt.CopyTo(span[offset..]);
        offset += EncryptionConstants.SaltSizeBytes;

        nonce.CopyTo(span[offset..]);
        offset += EncryptionConstants.NonceSizeBytes;

        ciphertext.CopyTo(span[offset..]);
        offset += ciphertext.Length;

        tag.CopyTo(span[offset..]);

        _logger.LogDebug("Encrypted backup: {OriginalSize} bytes → {EncryptedSize} bytes (overhead: {Overhead} bytes)",
            jsonBytes.Length, encryptedBackup.Length, encryptedBackup.Length - jsonBytes.Length);

        return encryptedBackup;
    }

    /// <summary>
    /// Decrypts backup file with passphrase
    /// </summary>
    /// <param name="encryptedBytes">Encrypted backup file bytes</param>
    /// <param name="passphrase">User passphrase used for encryption</param>
    /// <returns>Decrypted JSON backup data</returns>
    /// <exception cref="CryptographicException">If passphrase is wrong or data is corrupted</exception>
    public byte[] DecryptBackup(byte[] encryptedBytes, string passphrase)
    {
        if (encryptedBytes == null || encryptedBytes.Length == 0)
            throw new ArgumentException("Encrypted data cannot be empty", nameof(encryptedBytes));

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        // Validate file format
        if (!IsEncrypted(encryptedBytes))
        {
            throw new InvalidOperationException("File does not have encrypted backup header (TGAENC)");
        }

        var minSize = MagicHeader.Length + EncryptionConstants.SaltSizeBytes + EncryptionConstants.NonceSizeBytes + EncryptionConstants.TagSizeBytes;
        if (encryptedBytes.Length < minSize)
        {
            throw new InvalidOperationException($"Encrypted backup file is too small (minimum {minSize} bytes)");
        }

        // Extract components from file
        var span = encryptedBytes.AsSpan();
        var offset = MagicHeader.Length; // Skip magic header

        var salt = span.Slice(offset, EncryptionConstants.SaltSizeBytes).ToArray();
        offset += EncryptionConstants.SaltSizeBytes;

        var nonce = span.Slice(offset, EncryptionConstants.NonceSizeBytes).ToArray();
        offset += EncryptionConstants.NonceSizeBytes;

        var ciphertextLength = encryptedBytes.Length - offset - EncryptionConstants.TagSizeBytes;
        var ciphertext = span.Slice(offset, ciphertextLength).ToArray();
        offset += ciphertextLength;

        var tag = span.Slice(offset, EncryptionConstants.TagSizeBytes).ToArray();

        // Derive key from passphrase (must match encryption key)
        var key = DeriveKey(passphrase, salt);

        // Decrypt using AES-256-GCM
        using var aesGcm = new AesGcm(key, EncryptionConstants.TagSizeBytes);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Decryption failed - likely incorrect passphrase or corrupted data");
            throw new CryptographicException("Failed to decrypt backup. Incorrect passphrase or corrupted file.", ex);
        }

        _logger.LogDebug("Decrypted backup: {EncryptedSize} bytes → {DecryptedSize} bytes",
            encryptedBytes.Length, plaintext.Length);

        return plaintext;
    }

    /// <summary>
    /// Checks if backup file is encrypted by looking for magic header
    /// </summary>
    public bool IsEncrypted(byte[] backupBytes)
    {
        if (backupBytes == null || backupBytes.Length < MagicHeader.Length)
            return false;

        return backupBytes.AsSpan()[..MagicHeader.Length].SequenceEqual(MagicHeader);
    }

    /// <summary>
    /// Derives encryption key from passphrase using PBKDF2-HMAC-SHA256
    /// </summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        return KeyDerivation.Pbkdf2(
            password: passphrase,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: EncryptionConstants.Pbkdf2Iterations,
            numBytesRequested: EncryptionConstants.KeySizeBytes
        );
    }
}
