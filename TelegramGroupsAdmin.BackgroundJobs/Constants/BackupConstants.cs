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
}
