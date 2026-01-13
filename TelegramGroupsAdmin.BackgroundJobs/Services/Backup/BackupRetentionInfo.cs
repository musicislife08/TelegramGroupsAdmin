namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Result of analyzing a backup's retention status.
/// </summary>
/// <param name="PrimaryTier">The highest retention tier this backup qualifies for.</param>
/// <param name="WillBeKept">Whether this backup will be kept based on retention policy.</param>
public record BackupRetentionInfo(BackupTier PrimaryTier, bool WillBeKept);
