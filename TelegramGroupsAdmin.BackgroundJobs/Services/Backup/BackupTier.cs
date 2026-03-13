namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Backup tier classification (grandfather-father-son strategy)
/// </summary>
public enum BackupTier
{
    Hourly,
    Daily,
    Weekly,
    Monthly,
    Yearly
}
