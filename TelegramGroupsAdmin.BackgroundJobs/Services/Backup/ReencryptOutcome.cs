namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Result of re-encrypting one backup's database entry.
/// </summary>
public enum ReencryptOutcome
{
    /// <summary>The entry was re-encrypted with the new passphrase.</summary>
    Reencrypted,

    /// <summary>The entry already opened with the new passphrase, so the file was left untouched.</summary>
    AlreadyCurrent
}
