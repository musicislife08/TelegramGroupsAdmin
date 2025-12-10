namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for ScheduledBackupJob - automatic database backups with granular retention
/// </summary>
public record ScheduledBackupPayload
{
    /// <summary>
    /// Number of hourly backups to retain (default: 24 = last 24 hours)
    /// </summary>
    public int RetainHourlyBackups { get; init; } = 24;

    /// <summary>
    /// Number of daily backups to retain (default: 7 = last 7 days)
    /// </summary>
    public int RetainDailyBackups { get; init; } = 7;

    /// <summary>
    /// Number of weekly backups to retain (default: 4 = last 4 weeks)
    /// </summary>
    public int RetainWeeklyBackups { get; init; } = 4;

    /// <summary>
    /// Number of monthly backups to retain (default: 12 = last 12 months)
    /// </summary>
    public int RetainMonthlyBackups { get; init; } = 12;

    /// <summary>
    /// Number of yearly backups to retain (default: 3 = last 3 years)
    /// </summary>
    public int RetainYearlyBackups { get; init; } = 3;

    /// <summary>
    /// Directory path where backups should be saved
    /// If null, uses default /data/backups
    /// </summary>
    public string? BackupDirectory { get; init; }
}
