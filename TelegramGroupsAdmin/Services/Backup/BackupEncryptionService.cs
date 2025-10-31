using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Handles encryption and decryption of backup files using AES-256-GCM
/// </summary>
public class BackupEncryptionService : IBackupEncryptionService
{
    private readonly ILogger<BackupEncryptionService> _logger;

    // File format constants
    private static readonly byte[] MagicHeader = "TGAENC\0"u8.ToArray(); // 7 bytes
    private const int SaltSize = 32;
    private const int NonceSize = 12; // AES-GCM standard nonce size
    private const int TagSize = 16; // AES-GCM authentication tag size
    private const int Pbkdf2Iterations = 100000;
    private const int KeySize = 32; // 256 bits

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
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        // Derive encryption key from passphrase using PBKDF2
        var key = DeriveKey(passphrase, salt);

        // Encrypt using AES-256-GCM
        using var aesGcm = new AesGcm(key, TagSize);
        var ciphertext = new byte[jsonBytes.Length];
        var tag = new byte[TagSize];

        aesGcm.Encrypt(nonce, jsonBytes, ciphertext, tag);

        // Build final encrypted backup file
        // Format: [TGAENC\0 (7)][salt (32)][nonce (12)][ciphertext (variable)][tag (16)]
        var encryptedBackup = new byte[MagicHeader.Length + SaltSize + NonceSize + ciphertext.Length + TagSize];
        var span = encryptedBackup.AsSpan();

        var offset = 0;
        MagicHeader.CopyTo(span[offset..]);
        offset += MagicHeader.Length;

        salt.CopyTo(span[offset..]);
        offset += SaltSize;

        nonce.CopyTo(span[offset..]);
        offset += NonceSize;

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

        var minSize = MagicHeader.Length + SaltSize + NonceSize + TagSize;
        if (encryptedBytes.Length < minSize)
        {
            throw new InvalidOperationException($"Encrypted backup file is too small (minimum {minSize} bytes)");
        }

        // Extract components from file
        var span = encryptedBytes.AsSpan();
        var offset = MagicHeader.Length; // Skip magic header

        var salt = span.Slice(offset, SaltSize).ToArray();
        offset += SaltSize;

        var nonce = span.Slice(offset, NonceSize).ToArray();
        offset += NonceSize;

        var ciphertextLength = encryptedBytes.Length - offset - TagSize;
        var ciphertext = span.Slice(offset, ciphertextLength).ToArray();
        offset += ciphertextLength;

        var tag = span.Slice(offset, TagSize).ToArray();

        // Derive key from passphrase (must match encryption key)
        var key = DeriveKey(passphrase, salt);

        // Decrypt using AES-256-GCM
        using var aesGcm = new AesGcm(key, TagSize);
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
            iterationCount: Pbkdf2Iterations,
            numBytesRequested: KeySize
        );
    }
}
