namespace TelegramGroupsAdmin.Data.Services;

/// <summary>
/// Pre-migration service that compacts migration history when database
/// is at a known stable state (v1.5.0), replacing all previous migrations
/// with a single consolidated baseline.
/// </summary>
public interface IMigrationHistoryCompactionService
{
    /// <summary>
    /// Checks migration history state and performs compaction if appropriate.
    /// Must be called BEFORE context.Database.MigrateAsync().
    /// </summary>
    Task<MigrationCompactionResult> CompactIfEligibleAsync(CancellationToken cancellationToken = default);
}
