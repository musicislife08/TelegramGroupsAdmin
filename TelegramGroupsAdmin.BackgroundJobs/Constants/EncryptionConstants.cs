namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for backup encryption (AES-256-GCM with PBKDF2 key derivation).
/// </summary>
public static class EncryptionConstants
{
    /// <summary>
    /// Size of the random salt in bytes for PBKDF2 key derivation.
    /// </summary>
    public const int SaltSizeBytes = 32;

    /// <summary>
    /// Size of the nonce (initialization vector) in bytes for AES-GCM encryption.
    /// AES-GCM standard nonce size.
    /// </summary>
    public const int NonceSizeBytes = 12;

    /// <summary>
    /// Size of the authentication tag in bytes for AES-GCM encryption.
    /// </summary>
    public const int TagSizeBytes = 16;

    /// <summary>
    /// Number of PBKDF2 iterations for key derivation from passphrase.
    /// Higher values increase security but also computation time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OWASP 2023 recommends 600K+ iterations for PBKDF2-SHA256. However, 100K is acceptable
    /// for this homelab use case because:
    /// </para>
    /// <list type="bullet">
    ///   <item>Passphrases are admin-controlled (not user-generated weak passwords)</item>
    ///   <item>Backups are stored locally on admin-controlled infrastructure</item>
    ///   <item>Single-user system with no multi-tenant concerns</item>
    ///   <item>Higher iterations would significantly slow backup/restore operations</item>
    /// </list>
    /// <para>
    /// For multi-tenant or cloud-hosted deployments with user-generated passphrases,
    /// consider increasing to 600K+ iterations.
    /// </para>
    /// <para>
    /// This constant is the only source of the iteration count. It is not stored in the backup file
    /// header or in the encryption config, so changing it makes every existing backup undecryptable.
    /// A change has to come with a new format version that maps to the new count.
    /// </para>
    /// </remarks>
    public const int Pbkdf2Iterations = 100000;

    /// <summary>
    /// Size of the derived encryption key in bytes (256 bits for AES-256).
    /// </summary>
    public const int KeySizeBytes = 32;

    /// <summary>
    /// Minimum recommended passphrase length in characters.
    /// </summary>
    public const int MinimumPassphraseLengthChars = 12;

    /// <summary>
    /// Plaintext chunk size in bytes for chunked AEAD streaming encryption.
    /// Each chunk is independently encrypted with AES-GCM using a derived per-chunk nonce.
    /// </summary>
    public const int ChunkSize = 1024 * 1024; // 1 MB

    /// <summary>
    /// Magic header for the chunked AEAD format (version 2).
    /// Format: TGAEC2 + null terminator = 7 bytes.
    /// </summary>
    public static readonly byte[] ChunkedMagicHeader = "TGAEC2\0"u8.ToArray();

    /// <summary>
    /// Magic header for the legacy single-shot AES-GCM format (version 1).
    /// Format: TGAENC + null terminator = 7 bytes.
    /// </summary>
    public static readonly byte[] LegacyMagicHeader = "TGAENC\0"u8.ToArray();

    /// <summary>
    /// Chunked format version byte. Allows future format evolution.
    /// </summary>
    public const byte ChunkedFormatVersion = 0x01;

    /// <summary>
    /// Total header size for the chunked format:
    /// magic (7) + version (1) + salt (32) + base nonce (12) = 52 bytes.
    /// The decryptor uses it to reject a file too short to hold a complete header.
    /// </summary>
    public const int ChunkedHeaderSize = 7 + 1 + SaltSizeBytes + NonceSizeBytes;
}
