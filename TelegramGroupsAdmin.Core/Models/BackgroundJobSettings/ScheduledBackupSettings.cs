namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Scheduled Backup job - granular 5-tier retention.
/// </summary>
public record ScheduledBackupSettings
{
    /// <summary>
    /// Number of hourly backups to retain (default: 24).
    /// </summary>
    public int RetainHourlyBackups { get; init; } = 24;

    /// <summary>
    /// Number of daily backups to retain (default: 7).
    /// </summary>
    public int RetainDailyBackups { get; init; } = 7;

    /// <summary>
    /// Number of weekly backups to retain (default: 4).
    /// </summary>
    public int RetainWeeklyBackups { get; init; } = 4;

    /// <summary>
    /// Number of monthly backups to retain (default: 12).
    /// </summary>
    public int RetainMonthlyBackups { get; init; } = 12;

    /// <summary>
    /// Number of yearly backups to retain (default: 3).
    /// </summary>
    public int RetainYearlyBackups { get; init; } = 3;

    /// <summary>
    /// Directory path for storing backups.
    /// </summary>
    public string? BackupDirectory { get; init; }
}
