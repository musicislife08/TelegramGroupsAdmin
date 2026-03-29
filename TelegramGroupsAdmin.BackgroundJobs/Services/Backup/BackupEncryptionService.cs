using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.BackgroundJobs.Constants;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Handles encryption and decryption of backup files using AES-256-GCM.
/// Supports both chunked AEAD streaming (TGAEC2) and legacy single-shot (TGAENC) formats.
/// </summary>
public class BackupEncryptionService(ILogger<BackupEncryptionService> logger) : IBackupEncryptionService
{

    /// <summary>
    /// Encrypts backup JSON bytes with passphrase-derived key (legacy single-shot format).
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
        var header = EncryptionConstants.LegacyMagicHeader;
        var encryptedBackup = new byte[header.Length + EncryptionConstants.SaltSizeBytes + EncryptionConstants.NonceSizeBytes + ciphertext.Length + EncryptionConstants.TagSizeBytes];
        var span = encryptedBackup.AsSpan();

        var offset = 0;
        header.CopyTo(span[offset..]);
        offset += header.Length;

        salt.CopyTo(span[offset..]);
        offset += EncryptionConstants.SaltSizeBytes;

        nonce.CopyTo(span[offset..]);
        offset += EncryptionConstants.NonceSizeBytes;

        ciphertext.CopyTo(span[offset..]);
        offset += ciphertext.Length;

        tag.CopyTo(span[offset..]);

        logger.LogDebug("Encrypted backup: {OriginalSize} bytes → {EncryptedSize} bytes (overhead: {Overhead} bytes)",
            jsonBytes.Length, encryptedBackup.Length, encryptedBackup.Length - jsonBytes.Length);

        return encryptedBackup;
    }

    /// <summary>
    /// Decrypts backup file with passphrase (legacy single-shot format).
    /// Also handles chunked format by detecting the magic header.
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
            throw new InvalidOperationException("File does not have encrypted backup header (TGAENC or TGAEC2)");
        }

        // Detect chunked format and delegate to stream-based decryption
        if (encryptedBytes.AsSpan()[..EncryptionConstants.ChunkedMagicHeader.Length]
            .SequenceEqual(EncryptionConstants.ChunkedMagicHeader))
        {
            using var cipherInput = new MemoryStream(encryptedBytes);
            using var plainOutput = new MemoryStream();
            DecryptBackup(cipherInput, plainOutput, passphrase);
            return plainOutput.ToArray();
        }

        // Legacy single-shot format
        var header = EncryptionConstants.LegacyMagicHeader;
        var minSize = header.Length + EncryptionConstants.SaltSizeBytes + EncryptionConstants.NonceSizeBytes + EncryptionConstants.TagSizeBytes;
        if (encryptedBytes.Length < minSize)
        {
            throw new InvalidOperationException($"Encrypted backup file is too small (minimum {minSize} bytes)");
        }

        // Extract components from file
        var span = encryptedBytes.AsSpan();
        var offset = header.Length; // Skip magic header

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
            logger.LogWarning(ex, "Decryption failed - likely incorrect passphrase or corrupted data");
            throw new CryptographicException("Failed to decrypt backup. Incorrect passphrase or corrupted file.", ex);
        }

        logger.LogDebug("Decrypted backup: {EncryptedSize} bytes → {DecryptedSize} bytes",
            encryptedBytes.Length, plaintext.Length);

        return plaintext;
    }

    /// <summary>
    /// Checks if backup file is encrypted by looking for legacy or chunked magic header
    /// </summary>
    public bool IsEncrypted(byte[] backupBytes)
    {
        if (backupBytes == null || backupBytes.Length < EncryptionConstants.LegacyMagicHeader.Length)
            return false;

        var header = backupBytes.AsSpan()[..EncryptionConstants.LegacyMagicHeader.Length];
        return header.SequenceEqual(EncryptionConstants.LegacyMagicHeader)
            || header.SequenceEqual(EncryptionConstants.ChunkedMagicHeader);
    }

    /// <summary>
    /// Encrypts backup data using chunked AEAD streaming encryption (1 MB chunks).
    /// Each chunk is independently encrypted with AES-GCM using a per-chunk nonce
    /// derived by XOR-ing a 64-bit big-endian counter into the last 8 bytes of the base nonce.
    /// Peak memory usage is ~2 MB regardless of backup size.
    /// </summary>
    /// <param name="plaintext">Stream containing unencrypted backup data</param>
    /// <param name="cipherOutput">Stream to write encrypted output to</param>
    /// <param name="passphrase">User passphrase (min 12 chars recommended)</param>
    public void EncryptBackup(Stream plaintext, Stream cipherOutput, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(cipherOutput);

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        // CRITICAL: Fresh salt and base nonce per encryption call — never cached
        var salt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSizeBytes);
        var baseNonce = RandomNumberGenerator.GetBytes(EncryptionConstants.NonceSizeBytes);

        // Derive key ONCE per backup via PBKDF2
        var key = DeriveKey(passphrase, salt);

        // Write header: [TGAEC2\0 (7)][version (1)][salt (32)][base nonce (12)] = 52 bytes
        cipherOutput.Write(EncryptionConstants.ChunkedMagicHeader);
        cipherOutput.WriteByte(EncryptionConstants.ChunkedFormatVersion);
        cipherOutput.Write(salt);
        cipherOutput.Write(baseNonce);

        var chunkBuffer = new byte[EncryptionConstants.ChunkSize];
        var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize];
        var chunkNonce = new byte[EncryptionConstants.NonceSizeBytes];
        var tag = new byte[EncryptionConstants.TagSizeBytes];
        var lengthBuffer = new byte[4];
        long chunkCounter = 0;
        long totalPlaintextBytes = 0;

        using var aesGcm = new AesGcm(key, EncryptionConstants.TagSizeBytes);

        while (true)
        {
            // Read up to ChunkSize bytes from plaintext
            var bytesRead = plaintext.ReadAtLeast(chunkBuffer.AsSpan(0, EncryptionConstants.ChunkSize), EncryptionConstants.ChunkSize, throwOnEndOfStream: false);
            if (bytesRead == 0)
                break;

            // Derive per-chunk nonce: copy base nonce, XOR 64-bit big-endian counter into last 8 bytes
            DeriveChunkNonce(baseNonce, chunkCounter, chunkNonce);

            // Write chunk length as 4-byte big-endian int
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, bytesRead);
            cipherOutput.Write(lengthBuffer);

            // Encrypt chunk with AES-GCM
            var plaintextSlice = chunkBuffer.AsSpan(0, bytesRead);
            var ciphertextSlice = ciphertextBuffer.AsSpan(0, bytesRead);
            aesGcm.Encrypt(chunkNonce, plaintextSlice, ciphertextSlice, tag);

            // Write ciphertext + tag
            cipherOutput.Write(ciphertextSlice);
            cipherOutput.Write(tag);

            totalPlaintextBytes += bytesRead;
            chunkCounter++;
        }

        // Write sentinel: 4-byte zero (chunk length = 0) to prevent truncation attacks
        BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, 0);
        cipherOutput.Write(lengthBuffer);

        logger.LogDebug(
            "Chunked encryption complete: {PlaintextBytes} bytes in {ChunkCount} chunks → {CipherBytes} bytes",
            totalPlaintextBytes, chunkCounter, cipherOutput.Position);
    }

    /// <summary>
    /// Decrypts backup data from a stream. Auto-detects chunked (TGAEC2) vs legacy (TGAENC) format.
    /// For chunked format, decrypts one chunk at a time for constant memory usage.
    /// For legacy format, falls back to single-shot byte[] decryption.
    /// </summary>
    /// <param name="cipherInput">Stream containing encrypted backup data</param>
    /// <param name="plainOutput">Stream to write decrypted output to</param>
    /// <param name="passphrase">User passphrase used during encryption</param>
    /// <exception cref="CryptographicException">If passphrase is incorrect or data corrupted</exception>
    public void DecryptBackup(Stream cipherInput, Stream plainOutput, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(cipherInput);
        ArgumentNullException.ThrowIfNull(plainOutput);

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

        // Read first 7 bytes to detect format
        var magicBuffer = new byte[EncryptionConstants.LegacyMagicHeader.Length];
        cipherInput.ReadExactly(magicBuffer, 0, magicBuffer.Length);

        if (magicBuffer.AsSpan().SequenceEqual(EncryptionConstants.LegacyMagicHeader))
        {
            // Legacy format: read entire remaining stream, prepend header, delegate to byte[] overload
            using var fullStream = new MemoryStream();
            fullStream.Write(magicBuffer);
            cipherInput.CopyTo(fullStream);
            var decrypted = DecryptBackup(fullStream.ToArray(), passphrase);
            plainOutput.Write(decrypted);
            return;
        }

        if (!magicBuffer.AsSpan().SequenceEqual(EncryptionConstants.ChunkedMagicHeader))
        {
            throw new InvalidOperationException("File does not have a recognized encrypted backup header (TGAENC or TGAEC2)");
        }

        // Chunked format: read version byte, salt, base nonce from header
        var versionByte = new byte[1];
        cipherInput.ReadExactly(versionByte, 0, 1);
        if (versionByte[0] != EncryptionConstants.ChunkedFormatVersion)
        {
            throw new InvalidOperationException($"Unsupported chunked format version: {versionByte[0]}");
        }

        var salt = new byte[EncryptionConstants.SaltSizeBytes];
        cipherInput.ReadExactly(salt, 0, salt.Length);

        var baseNonce = new byte[EncryptionConstants.NonceSizeBytes];
        cipherInput.ReadExactly(baseNonce, 0, baseNonce.Length);

        // Derive key ONCE via PBKDF2
        var key = DeriveKey(passphrase, salt);

        var chunkNonce = new byte[EncryptionConstants.NonceSizeBytes];
        var tag = new byte[EncryptionConstants.TagSizeBytes];
        var lengthBuffer = new byte[4];
        var ciphertextBuffer = new byte[EncryptionConstants.ChunkSize];
        var decryptedBuffer = new byte[EncryptionConstants.ChunkSize];
        long chunkCounter = 0;
        long totalDecryptedBytes = 0;

        using var aesGcm = new AesGcm(key, EncryptionConstants.TagSizeBytes);

        while (true)
        {
            // Read 4-byte big-endian chunk length
            cipherInput.ReadExactly(lengthBuffer, 0, 4);
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);

            // Sentinel: chunk length = 0 means end of data
            if (chunkLength == 0)
                break;

            if (chunkLength < 0 || chunkLength > EncryptionConstants.ChunkSize)
            {
                throw new InvalidOperationException(
                    $"Invalid chunk length {chunkLength} at chunk {chunkCounter} (max {EncryptionConstants.ChunkSize})");
            }

            // Derive per-chunk nonce (same XOR scheme as encryption)
            DeriveChunkNonce(baseNonce, chunkCounter, chunkNonce);

            // Read ciphertext (chunkLength bytes) + tag (16 bytes)
            var ciphertextSlice = ciphertextBuffer.AsSpan(0, chunkLength);
            cipherInput.ReadExactly(ciphertextBuffer, 0, chunkLength);
            cipherInput.ReadExactly(tag, 0, EncryptionConstants.TagSizeBytes);

            // Decrypt chunk
            var decryptedSlice = decryptedBuffer.AsSpan(0, chunkLength);
            try
            {
                aesGcm.Decrypt(chunkNonce, ciphertextSlice, tag, decryptedSlice);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Chunk {ChunkIndex} decryption failed - likely incorrect passphrase or corrupted data", chunkCounter);
                throw new CryptographicException("Failed to decrypt backup. Incorrect passphrase or corrupted file.", ex);
            }

            plainOutput.Write(decryptedSlice);
            totalDecryptedBytes += chunkLength;
            chunkCounter++;
        }

        logger.LogDebug(
            "Chunked decryption complete: {ChunkCount} chunks → {DecryptedBytes} bytes",
            chunkCounter, totalDecryptedBytes);
    }

    /// <summary>
    /// Checks if a stream contains encrypted backup data by reading the magic header bytes.
    /// Restores the stream position after reading.
    /// </summary>
    public bool IsEncrypted(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead)
            return false;

        var savedPosition = input.Position;
        try
        {
            var header = new byte[EncryptionConstants.LegacyMagicHeader.Length];
            var bytesRead = input.ReadAtLeast(header.AsSpan(0, header.Length), header.Length, throwOnEndOfStream: false);

            if (bytesRead < header.Length)
                return false;

            return header.AsSpan().SequenceEqual(EncryptionConstants.LegacyMagicHeader)
                || header.AsSpan().SequenceEqual(EncryptionConstants.ChunkedMagicHeader);
        }
        finally
        {
            input.Position = savedPosition;
        }
    }

    /// <summary>
    /// Derives per-chunk nonce by XOR-ing a 64-bit big-endian chunk counter into the last 8 bytes
    /// of the 12-byte base nonce. This ensures each chunk uses a unique nonce without additional
    /// random generation, while the first 4 bytes of the base nonce remain untouched.
    /// </summary>
    private static void DeriveChunkNonce(byte[] baseNonce, long chunkCounter, byte[] chunkNonce)
    {
        // Copy base nonce
        baseNonce.AsSpan().CopyTo(chunkNonce);

        // XOR 64-bit big-endian counter into last 8 bytes (indices 4..11)
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, chunkCounter);

        for (var i = 0; i < 8; i++)
        {
            chunkNonce[4 + i] ^= counterBytes[i];
        }
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
