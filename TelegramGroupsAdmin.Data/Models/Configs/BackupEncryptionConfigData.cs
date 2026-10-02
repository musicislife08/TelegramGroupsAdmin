namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BackupEncryptionConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class BackupEncryptionConfigData
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
