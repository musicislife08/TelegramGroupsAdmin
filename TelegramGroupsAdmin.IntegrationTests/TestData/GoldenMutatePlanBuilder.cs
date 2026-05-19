using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.IntegrationTests.TestData;

/// <summary>
/// In-place mutator for canonical substrate. Sibling of <see cref="GoldenReducePlanBuilder"/>
/// but strictly limited to <em>editing</em> existing rows — never creates them. Reach for this
/// only when canonical structurally cannot provide the shape (e.g., analytics aggregations
/// need timestamps relative to NOW() but canonical's timestamps are frozen at the bootstrap
/// snapshot date). If a verb would need to insert rows, that's the signal to extend canonical
/// instead.
///
/// Verb count is intentionally bounded — see <c>docs/superpowers/plans/2026-04-30-canonical-
/// golden-snapshot-and-template-cloning.md</c> for the active register.
/// </summary>
public sealed class GoldenMutatePlanBuilder
{
    private readonly AppDbContext _context;
    private List<TimestampShift>? _detectionResultShifts;
    private List<TimestampShift>? _welcomeResponseShifts;

    internal GoldenMutatePlanBuilder(AppDbContext context) => _context = context;

    /// <summary>
    /// For each shift, sets <c>detection_results.detected_at = date_trunc('day', NOW()) + Offset</c>
    /// where <c>id</c> matches. Rows not in the shift list are left untouched. Calling
    /// twice merges (last shift wins per id).
    /// </summary>
    public GoldenMutatePlanBuilder ShiftDetectionResultTimestamps(IEnumerable<TimestampShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        (_detectionResultShifts ??= new()).AddRange(shifts);
        return this;
    }

    /// <summary>
    /// For each shift, sets <c>welcome_responses.responded_at = date_trunc('day', NOW()) + Offset</c>
    /// (and <c>created_at</c> to <c>responded_at - 1 minute</c>, mirroring the legacy seed's
    /// "created shortly before response" pattern) where <c>id</c> matches. Rows not in the
    /// shift list are left untouched.
    /// </summary>
    public GoldenMutatePlanBuilder ShiftWelcomeResponseTimestamps(IEnumerable<TimestampShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        (_welcomeResponseShifts ??= new()).AddRange(shifts);
        return this;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            if (_detectionResultShifts is { Count: > 0 } drShifts)
            {
                foreach (var s in drShifts)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE detection_results SET detected_at = date_trunc('day', NOW()) + ({0}::text)::interval WHERE id = {1}",
                        new object[] { FormatInterval(s.Offset), s.Id },
                        ct);
                }
            }

            if (_welcomeResponseShifts is { Count: > 0 } wrShifts)
            {
                foreach (var s in wrShifts)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE welcome_responses " +
                        "SET responded_at = date_trunc('day', NOW()) + ({0}::text)::interval, " +
                        "    created_at   = date_trunc('day', NOW()) + ({0}::text)::interval - INTERVAL '1 minute' " +
                        "WHERE id = {1}",
                        new object[] { FormatInterval(s.Offset), s.Id },
                        ct);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // PostgreSQL interval literal: total microseconds preserves sub-second precision.
    private static string FormatInterval(TimeSpan offset)
        => $"{offset.Ticks / 10.0:0.######} microseconds";
}
