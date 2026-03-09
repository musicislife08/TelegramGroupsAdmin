namespace TelegramGroupsAdmin.Data.Services;

/// <summary>
/// Result of migration history compaction check.
/// </summary>
public enum MigrationCompactionResult
{
    /// <summary>No history table - fresh database, proceed with full migration</summary>
    FreshDatabase,

    /// <summary>History compacted to baseline - proceed (migrations will be no-op)</summary>
    Compacted,

    /// <summary>Already at or past baseline - no action needed</summary>
    NoActionNeeded,

    /// <summary>Database at incompatible state - cannot proceed</summary>
    IncompatibleState
}
