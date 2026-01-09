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
    /// Encryption algorithm identifier
    /// </summary>
    public string Algorithm { get; set; } = "AES256-GCM";

    /// <summary>
    /// PBKDF2 iteration count
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
