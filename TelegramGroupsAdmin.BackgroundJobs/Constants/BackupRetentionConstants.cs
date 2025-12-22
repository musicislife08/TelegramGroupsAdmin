namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for backup retention policy defaults.
/// </summary>
public static class BackupRetentionConstants
{
    /// <summary>
    /// Default number of hourly backups to retain (last 24 hours).
    /// </summary>
    public const int DefaultRetainHourlyBackups = 24;

    /// <summary>
    /// Default number of daily backups to retain (last 7 days).
    /// </summary>
    public const int DefaultRetainDailyBackups = 7;

    /// <summary>
    /// Default number of weekly backups to retain (last 4 weeks).
    /// </summary>
    public const int DefaultRetainWeeklyBackups = 4;

    /// <summary>
    /// Default number of monthly backups to retain (last 12 months).
    /// </summary>
    public const int DefaultRetainMonthlyBackups = 12;

    /// <summary>
    /// Default number of yearly backups to retain (last 3 years).
    /// </summary>
    public const int DefaultRetainYearlyBackups = 3;

    /// <summary>
    /// Number of days in a week for week calculation.
    /// </summary>
    public const int DaysPerWeek = 7;
}
