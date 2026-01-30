using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Data.Constants;

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

/// <summary>
/// Implementation of migration history compaction.
/// Uses raw NpgsqlConnection to check/modify history before EF Core touches it.
/// </summary>
public class MigrationHistoryCompactionService : IMigrationHistoryCompactionService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<MigrationHistoryCompactionService> _logger;

    public MigrationHistoryCompactionService(
        NpgsqlDataSource dataSource,
        ILogger<MigrationHistoryCompactionService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MigrationCompactionResult> CompactIfEligibleAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // 1. HOT PATH: Check if baseline migration exists (99% of startups after first run)
        //    Single query, immediate exit - keeps startup fast
        if (await MigrationExistsAsync(connection, MigrationCompactionConstants.BaselineMigrationId, cancellationToken))
        {
            _logger.LogDebug("Baseline migration exists - proceeding with normal startup");
            return MigrationCompactionResult.NoActionNeeded;
        }

        // 2. Check if last compatible migration exists (compaction eligible)
        //    Only runs on first deployment after upgrade to this version
        if (await MigrationExistsAsync(connection, MigrationCompactionConstants.LastRequiredMigration, cancellationToken))
        {
            _logger.LogInformation("Database at required migration - compacting history");
            await CompactHistoryAsync(connection, cancellationToken);
            return MigrationCompactionResult.Compacted;
        }

        // 3. Check if this is a fresh database (no history table)
        //    Only for brand new installations
        if (!await MigrationHistoryExistsAsync(connection, cancellationToken))
        {
            _logger.LogInformation("Fresh database - no migration history found");
            return MigrationCompactionResult.FreshDatabase;
        }

        // 4. Incompatible state - database exists but not at required migration
        //    Return result - caller handles exit (keeps service testable)
        var lastMigration = await GetLastMigrationAsync(connection, cancellationToken);
        _logger.LogCritical(
            "Database migration incompatible. Current: {Current}, Required: {Required}. " +
            "Please upgrade to {Version} before running this version.",
            lastMigration,
            MigrationCompactionConstants.LastRequiredMigration,
            MigrationCompactionConstants.RequiredVersion);

        return MigrationCompactionResult.IncompatibleState;
    }

    /// <summary>
    /// Check if a specific migration exists in the history table.
    /// </summary>
    private static async Task<bool> MigrationExistsAsync(
        NpgsqlConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = @MigrationId
            )
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("MigrationId", migrationId);

        try
        {
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result is true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
        {
            return false;
        }
    }

    /// <summary>
    /// Check if the __EFMigrationsHistory table exists.
    /// </summary>
    private static async Task<bool> MigrationHistoryExistsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = '__EFMigrationsHistory'
            )
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    /// <summary>
    /// Get the last migration ID from history (for error messages).
    /// </summary>
    private static async Task<string?> GetLastMigrationAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT "MigrationId"
            FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId" DESC
            LIMIT 1
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    /// <summary>
    /// Compact migration history: delete all entries, insert baseline.
    /// </summary>
    private async Task CompactHistoryAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Delete all existing migration history
            await using (var deleteCmd = new NpgsqlCommand(
                "DELETE FROM \"__EFMigrationsHistory\"", connection, transaction))
            {
                var deletedCount = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Deleted {Count} migration history records", deletedCount);
            }

            // Insert single baseline migration record
            await using (var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES (@MigrationId, @ProductVersion)
                """, connection, transaction))
            {
                insertCmd.Parameters.AddWithValue("MigrationId", MigrationCompactionConstants.BaselineMigrationId);
                insertCmd.Parameters.AddWithValue("ProductVersion", MigrationCompactionConstants.BaselineProductVersion);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Migration history compacted to baseline: {Baseline}",
                MigrationCompactionConstants.BaselineMigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration history compaction failed, rolling back transaction");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
