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

    /// <summary>
    /// Job-specific settings as JSON
    /// Example for scheduled_backup: {"retention_days": 7}
    /// </summary>
    public Dictionary<string, object>? Settings { get; set; }
}
