namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BackgroundJobConfig for EF Core JSON column mapping.
/// </summary>
public class BackgroundJobConfigData
{
    /// <summary>
    /// Unique job identifier
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
    /// Natural language schedule expression
    /// </summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// Last time this job executed successfully
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Calculated next run time
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Last error message if job failed
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Job-specific settings as JSON
    /// </summary>
    public Dictionary<string, object>? Settings { get; set; }
}
