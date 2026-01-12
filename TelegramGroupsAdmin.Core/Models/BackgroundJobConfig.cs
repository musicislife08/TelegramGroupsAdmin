using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Configuration for a single background job
/// Stored in configs.background_jobs_config JSONB column
/// </summary>
public class BackgroundJobConfig
{
    /// <summary>
    /// Unique job identifier (e.g., "scheduled_backup", "message_cleanup")
    /// </summary>
    public required string JobName { get; set; }

    /// <summary>
    /// Human-readable job display name
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Description of what this job does
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Whether this job is currently enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Natural language schedule expression (e.g., "1d at 2pm", "2w on sunday", "30m")
    /// Converted to Quartz CronSchedule or CalendarIntervalSchedule at runtime
    /// Supports bidirectional conversion via NaturalCron library
    /// </summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// Last time this job executed successfully (UTC)
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Calculated next run time (UTC)
    /// Updated after each execution
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Last error message if job failed
    /// </summary>
    public string? LastError { get; set; }

    // === Job-specific settings (only populate the relevant one per job) ===

    /// <summary>
    /// Settings for Data Cleanup job (retention periods for messages, reports, etc.)
    /// </summary>
    public DataCleanupSettings? DataCleanup { get; set; }

    /// <summary>
    /// Settings for Scheduled Backup job (5-tier retention policy)
    /// </summary>
    public ScheduledBackupSettings? ScheduledBackup { get; set; }

    /// <summary>
    /// Settings for Database Maintenance job (VACUUM, ANALYZE)
    /// </summary>
    public DatabaseMaintenanceSettings? DatabaseMaintenance { get; set; }

    /// <summary>
    /// Settings for User Photo Refresh job
    /// </summary>
    public UserPhotoRefreshSettings? UserPhotoRefresh { get; set; }
}
