namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for password hashing using PBKDF2-HMAC-SHA256.
/// </summary>
public static class PasswordHashingConstants
{
    /// <summary>
    /// PBKDF2 iteration count (100,000 iterations).
    /// Balances security and performance for password hashing.
    /// </summary>
    public const int Pbkdf2IterationCount = 100000;

    /// <summary>
    /// PBKDF2 derived key length (32 bytes = 256 bits).
    /// </summary>
    public const int Pbkdf2SubkeyLength = 32;

    /// <summary>
    /// Salt size for password hashing (16 bytes = 128 bits).
    /// </summary>
    public const int SaltSize = 16;

    /// <summary>
    /// Version marker byte for hash format versioning.
    /// </summary>
    public const byte VersionMarker = 0x01;

    /// <summary>
    /// Total hash output size (1 version byte + 16 salt bytes + 32 subkey bytes = 49 bytes).
    /// </summary>
    public const int TotalHashSize = 1 + SaltSize + Pbkdf2SubkeyLength;
}
