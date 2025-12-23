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
    /// Encryption algorithm identifier stored in backup metadata.
    /// </summary>
    public const string EncryptionAlgorithm = "AES-256-GCM";
}
