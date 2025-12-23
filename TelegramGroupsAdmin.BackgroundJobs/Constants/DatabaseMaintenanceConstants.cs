namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for PostgreSQL database maintenance operations.
/// </summary>
public static class DatabaseMaintenanceConstants
{
    /// <summary>
    /// Command timeout in seconds for database maintenance operations (VACUUM, ANALYZE).
    /// Long timeout needed for large databases.
    /// </summary>
    public const int CommandTimeoutSeconds = 600;
}
