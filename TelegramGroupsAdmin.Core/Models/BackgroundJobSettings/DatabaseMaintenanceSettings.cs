namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Database Maintenance job - PostgreSQL maintenance operations.
/// </summary>
public record DatabaseMaintenanceSettings
{
    /// <summary>
    /// Whether to run VACUUM during maintenance (default: true).
    /// </summary>
    public bool RunVacuum { get; init; } = true;

    /// <summary>
    /// Whether to run ANALYZE during maintenance (default: true).
    /// </summary>
    public bool RunAnalyze { get; init; } = true;
}
