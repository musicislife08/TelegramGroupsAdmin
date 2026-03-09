using TelegramGroupsAdmin.BackgroundJobs.Constants;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Retention policy configuration (5-tier)
/// </summary>
public class RetentionConfig
{
    /// <summary>
    /// Number of hourly backups to retain (default: 24 = last 24 hours)
    /// </summary>
    public int RetainHourlyBackups { get; set; } = BackupRetentionConstants.DefaultRetainHourlyBackups;

    /// <summary>
    /// Number of daily backups to retain (default: 7 = last 7 days)
    /// </summary>
    public int RetainDailyBackups { get; set; } = BackupRetentionConstants.DefaultRetainDailyBackups;

    /// <summary>
    /// Number of weekly backups to retain (default: 4 = last 4 weeks)
    /// </summary>
    public int RetainWeeklyBackups { get; set; } = BackupRetentionConstants.DefaultRetainWeeklyBackups;

    /// <summary>
    /// Number of monthly backups to retain (default: 12 = last 12 months)
    /// </summary>
    public int RetainMonthlyBackups { get; set; } = BackupRetentionConstants.DefaultRetainMonthlyBackups;

    /// <summary>
    /// Number of yearly backups to retain (default: 3 = last 3 years)
    /// </summary>
    public int RetainYearlyBackups { get; set; } = BackupRetentionConstants.DefaultRetainYearlyBackups;
}
