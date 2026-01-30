namespace TelegramGroupsAdmin.Data.Constants;

/// <summary>
/// Constants for migration history compaction.
/// Used by MigrationHistoryCompactionService to determine when to compact
/// and what baseline migration to insert.
/// </summary>
public static class MigrationCompactionConstants
{
    /// <summary>
    /// Last migration that must be applied before compaction can occur.
    /// Database must have this migration for compaction to proceed.
    /// </summary>
    public const string LastRequiredMigration = "20260109002130_AddUseGlobalToFileScanningConfig";

    /// <summary>
    /// The application version containing LastRequiredMigration.
    /// Used in error messages to guide users on which version to upgrade to first.
    /// </summary>
    public const string RequiredVersion = "v1.5.0";

    /// <summary>
    /// New baseline migration ID replacing all previous migrations.
    /// </summary>
    public const string BaselineMigrationId = "20260109050105_InitialCreate";

    /// <summary>
    /// EF Core product version for the baseline record.
    /// </summary>
    public const string BaselineProductVersion = "10.0.0";
}
