namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for backup service operations.
/// </summary>
public static class BackupConstants
{
    /// <summary>
    /// Prefix for temporary directories created during backup media restore.
    /// Used by restore to create temp dirs and by cleanup to identify orphaned ones.
    /// </summary>
    public const string MediaTempDirPrefix = "backup-media-";

    /// <summary>File extension of a backup archive.</summary>
    public const string BackupFileExtension = ".tar.gz";

    /// <summary>Tar entry holding the unencrypted backup metadata (readable without a passphrase).</summary>
    public const string MetadataEntryName = "metadata.json";

    /// <summary>Tar entry holding the AES-GCM encrypted database export.</summary>
    public const string EncryptedDatabaseEntryName = "database.json.enc";

    /// <summary>Tar entry holding an unencrypted database export (legacy backups only).</summary>
    public const string PlainDatabaseEntryName = "database.json";

    /// <summary>Prefix of tar entries holding media files.</summary>
    public const string MediaEntryPrefix = "media/";
}
