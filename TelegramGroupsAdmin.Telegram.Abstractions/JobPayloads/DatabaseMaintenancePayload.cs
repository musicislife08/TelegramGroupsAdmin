namespace TelegramGroupsAdmin.Telegram.Abstractions;

/// <summary>
/// Payload for DatabaseMaintenanceJob - PostgreSQL VACUUM and ANALYZE operations
/// </summary>
public record DatabaseMaintenancePayload
{
    /// <summary>
    /// Whether to run VACUUM operation
    /// Reclaims storage occupied by dead tuples
    /// Default: true
    /// </summary>
    public bool RunVacuum { get; init; } = true;

    /// <summary>
    /// Whether to run ANALYZE operation
    /// Updates statistics for query planner
    /// Default: true
    /// </summary>
    public bool RunAnalyze { get; init; } = true;

    /// <summary>
    /// Whether to run VACUUM FULL (more aggressive, locks tables)
    /// Default: false (too disruptive for production)
    /// </summary>
    public bool RunVacuumFull { get; init; } = false;
}
