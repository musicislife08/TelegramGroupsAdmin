namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration for backup file encryption (metadata only)
/// Stored in configs.backup_encryption_config JSONB column
/// Note: Passphrase stored separately in configs.passphrase_encrypted TEXT column
/// </summary>
public class BackupEncryptionConfig
{
    /// <summary>
    /// Whether backup encryption is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Encryption algorithm identifier (for future extensibility)
    /// Current: AES256-GCM
    /// </summary>
    public string Algorithm { get; set; } = "AES256-GCM";

    /// <summary>
    /// PBKDF2 iteration count (higher = slower but more secure)
    /// Default: 100,000 iterations
    /// </summary>
    public int Iterations { get; set; } = 100000;

    /// <summary>
    /// When encryption was first configured (UTC)
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Last time passphrase was rotated (UTC)
    /// </summary>
    public DateTimeOffset? LastRotatedAt { get; set; }
}
