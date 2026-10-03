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
    /// When encryption was first configured (UTC)
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Last time passphrase was rotated (UTC)
    /// </summary>
    public DateTimeOffset? LastRotatedAt { get; set; }
}
