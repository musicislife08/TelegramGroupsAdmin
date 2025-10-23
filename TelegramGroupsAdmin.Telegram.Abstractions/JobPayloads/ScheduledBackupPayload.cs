namespace TelegramGroupsAdmin.Telegram.Abstractions;

/// <summary>
/// Payload for ScheduledBackupJob - automatic database backups
/// </summary>
public record ScheduledBackupPayload
{
    /// <summary>
    /// Number of days to retain backups
    /// Older backups will be deleted automatically
    /// Default: 7 days
    /// </summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>
    /// Directory path where backups should be saved
    /// If null, uses default ./data/backups
    /// </summary>
    public string? BackupDirectory { get; init; }
}
