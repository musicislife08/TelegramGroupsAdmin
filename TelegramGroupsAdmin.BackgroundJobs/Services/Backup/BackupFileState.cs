namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// What a backup file on disk holds, as far as passphrase rotation is concerned.
/// </summary>
public enum BackupFileState
{
    /// <summary>A .tar.gz archive whose database entry is encrypted (the current format).</summary>
    Encrypted,

    /// <summary>A .tar.gz archive whose database entry is unencrypted (legacy backups).</summary>
    Plain,

    /// <summary>
    /// A whole archive encrypted as one blob. Older versions of the rotation job produced these by
    /// mistake: the outer layer is on the stored passphrase, the database inside on an earlier one.
    /// </summary>
    Wrapped,

    /// <summary>Not a backup this version can read.</summary>
    Unreadable
}
