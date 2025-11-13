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
    /// Cron expression defining the job schedule
    /// Example: "0 2 * * *" for daily at 2 AM
    /// UI converts friendly formats (1h, 1d@02:00) to cron bidirectionally
    /// </summary>
    public required string CronExpression { get; set; }

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
