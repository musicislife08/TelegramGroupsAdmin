using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>A table that rotates its rows shuffle-bag style.</summary>
internal enum RotationBag
{
    BanCelebrationGifs,
    BanCelebrationCaptions
}

/// <summary>
/// Shuffle-bag rotation over a table with a nullable <c>dispensed_at</c> column: a null stamp
/// means the row is still pending in the current cycle. Shared by the ban celebration GIF and
/// caption repositories.
/// </summary>
internal static class RotationCycleClaim
{
    /// <summary>
    /// The table each bag rotates and the advisory lock serializing its cycle exhaustion. Callers
    /// name a bag, never a table, so a table can never be paired with the wrong lock key.
    /// </summary>
    private static (string Table, long AdvisoryLockKey) Resolve(RotationBag bag) => bag switch
    {
        RotationBag.BanCelebrationGifs =>
            ("ban_celebration_gifs", AdvisoryLockKeys.BanCelebrationGifCycle),
        RotationBag.BanCelebrationCaptions =>
            ("ban_celebration_captions", AdvisoryLockKeys.BanCelebrationCaptionCycle),
        // Required: TreatWarningsAsErrors turns a defaultless enum switch into a CS8524 build
        // failure. A new bag added without an arm here therefore fails at runtime, not at build.
        _ => throw new ArgumentOutOfRangeException(nameof(bag), bag, "Unknown rotation bag")
    };

    /// <summary>
    /// Claims the next id in the current cycle, starting a fresh cycle when the current one is
    /// exhausted. Returns null only when nothing is claimable — an empty table, or (vanishingly
    /// rarely) every pending row locked by concurrent claims.
    /// </summary>
    /// <param name="context">Context carrying the whole operation. All statements run in one
    /// transaction because <c>pg_advisory_xact_lock</c> is transaction-scoped.</param>
    /// <param name="bag">Which rotation to claim from.</param>
    public static async Task<int?> ClaimNextIdAsync(
        AppDbContext context,
        RotationBag bag,
        CancellationToken ct)
    {
        var (table, advisoryLockKey) = Resolve(bag);

        // Production enables retry-on-failure; the strategy re-runs this whole unit on a transient
        // failure rather than letting one surface as a skipped celebration.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            // Fast path: something is still pending in the current cycle.
            var claimed = await ClaimOneAsync(context, table, ct)
                          ?? await StartFreshCycleAndClaimAsync(context, table, advisoryLockKey, ct);

            await transaction.CommitAsync(ct);
            return claimed;
        });
    }

    private static async Task<int?> StartFreshCycleAndClaimAsync(
        AppDbContext context,
        string table,
        long advisoryLockKey,
        CancellationToken ct)
    {
        // SAFETY: the only interpolated value below is `table`, which comes from Resolve(bag)'s own
        // switch — never from a caller. Every runtime value still binds as a {0} parameter.
#pragma warning disable EF1002
        // Serialize exhaustion. Released automatically when the transaction ends.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [advisoryLockKey], ct);

        // Double-check: whoever held the lock before us has already reset and committed, so the
        // common outcome here is a hit — and we never reset at all.
        var claimed = await ClaimOneAsync(context, table, ct);
        if (claimed is not null)
            return claimed;

        // The row dispensed most recently. Held back from the new cycle so it cannot be dispensed
        // twice in a row across the boundary, then released once the new cycle's first item is
        // claimed. Null means no row carries a stamp, so there is nothing to rotate.
        var heldBackId = await ScalarAsync(context,
            $"""
             SELECT id FROM {table}
             WHERE dispensed_at IS NOT NULL
             ORDER BY dispensed_at DESC LIMIT 1
             """, ct);

        if (heldBackId is null)
            return null;

        var rowCount = await ScalarAsync(context, $"SELECT count(*)::int FROM {table}", ct) ?? 0;

        if (rowCount <= 1)
        {
            // A one-row library repeats by definition; holding its only row back would starve it.
            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE {table} SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL", ct);
            return await ClaimOneAsync(context, table, ct);
        }

        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET dispensed_at = NULL WHERE dispensed_at IS NOT NULL AND id <> {{0}}",
            [heldBackId.Value], ct);

        claimed = await ClaimOneAsync(context, table, ct);

        // Release the held-back row into the remainder of the new cycle.
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET dispensed_at = NULL WHERE id = {{0}}", [heldBackId.Value], ct);

        // Only reachable if concurrent claims took every freshly-cleared row first.
        return claimed ?? await ClaimOneAsync(context, table, ct);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Picks a random pending row and stamps it in one statement. FOR UPDATE SKIP LOCKED stops two
    /// concurrent claims taking the same row.
    /// </summary>
    private static Task<int?> ClaimOneAsync(AppDbContext context, string table, CancellationToken ct) =>
        ScalarAsync(context,
            $"""
             UPDATE {table} SET dispensed_at = now()
             WHERE id = (
                 SELECT id FROM {table}
                 WHERE dispensed_at IS NULL
                 ORDER BY random() LIMIT 1
                 FOR UPDATE SKIP LOCKED
             )
             RETURNING id
             """, ct);

    /// <summary>
    /// Runs a statement that yields at most one integer, and returns it — or null when the
    /// statement matched nothing.
    ///
    /// ToListAsync materializes without composing, which is what lets an UPDATE ... RETURNING run
    /// here at all: applying any LINQ operator instead (FirstOrDefaultAsync, Where, Take) wraps the
    /// SQL in a subquery, and PostgreSQL rejects a data-modifying statement there. Do not "tidy"
    /// this into FirstOrDefaultAsync.
    /// </summary>
    private static async Task<int?> ScalarAsync(AppDbContext context, string sql, CancellationToken ct)
    {
        var results = await context.Database.SqlQueryRaw<int>(sql).ToListAsync(ct);
        return results.Count > 0 ? results[0] : null;
    }
}
