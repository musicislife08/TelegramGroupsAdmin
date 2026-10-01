using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>
/// In-place mutator for canonical substrate. Sibling of <see cref="GoldenReducePlanBuilder"/>
/// but strictly limited to <em>editing</em> existing rows — never creates them. Reach for this
/// only when canonical structurally cannot provide the shape (e.g., analytics aggregations
/// need timestamps relative to NOW() but canonical's timestamps are frozen at the bootstrap
/// snapshot date; an account lockout is only "locked" while <c>locked_until</c> is still ahead of
/// NOW()). If a verb would need to insert rows, that's the signal to extend canonical instead.
///
/// Verb count is intentionally bounded — see <c>docs/superpowers/plans/2026-04-30-canonical-
/// golden-snapshot-and-template-cloning.md</c> for the active register.
/// </summary>
public sealed class GoldenMutatePlanBuilder
{
    private readonly AppDbContext _context;
    private List<TimestampShift>? _detectionResultShifts;
    private List<TimestampShift>? _welcomeResponseShifts;
    private List<(long ChatId, TimestampShift Shift)>? _messageShifts;
    private List<(string UserId, TimeSpan LockFor)>? _webUserLocks;

    /// <summary>
    /// <c>failed_login_attempts</c> written by <see cref="LockWebUser"/>: the first lockout happens at
    /// <c>AccountLockoutConstants.MaxFailedAttempts</c> (5) failed logins, so a locked row carries 5.
    /// </summary>
    public const int LockedFailedLoginAttempts = 5;

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
    /// For each shift, sets <c>messages.timestamp = date_trunc('day', NOW()) + Offset</c>
    /// where <c>(chat_id, message_id)</c> matches. <see cref="TimestampShift.Id"/> carries
    /// <c>message_id</c>; the composite key's chat side is passed once per call. Rows not
    /// in the shift list are left untouched.
    /// </summary>
    public GoldenMutatePlanBuilder ShiftMessageTimestamps(long chatId, IEnumerable<TimestampShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        (_messageShifts ??= new()).AddRange(shifts.Select(s => (chatId, s)));
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

    /// <summary>
    /// Locks the web user: sets <c>users.locked_until = NOW() + lockFor</c> and
    /// <c>failed_login_attempts = <see cref="LockedFailedLoginAttempts"/></c> where <c>id</c> matches —
    /// the shape <c>AccountLockoutService</c> leaves after the fifth failed login. A lockout is
    /// NOW()-relative by nature (the UI reads it as locked only while <c>locked_until</c> is in the
    /// future), so canonical's frozen snapshot cannot carry it. Other rows are left untouched.
    /// </summary>
    public GoldenMutatePlanBuilder LockWebUser(string userId, TimeSpan lockFor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (lockFor <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockFor), lockFor, "a lock must end in the future");
        }

        (_webUserLocks ??= new()).Add((userId, lockFor));
        return this;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            if (_webUserLocks is { Count: > 0 } locks)
            {
                foreach (var (userId, lockFor) in locks)
                {
                    var rows = await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE users SET locked_until = NOW() + ({0}::text)::interval, failed_login_attempts = {1} WHERE id = {2}",
                        new object[] { FormatInterval(lockFor), LockedFailedLoginAttempts, userId },
                        ct);
                    if (rows != 1)
                    {
                        throw new InvalidOperationException($"LockWebUser: expected to lock exactly one users row for id {userId}, updated {rows}");
                    }
                }
            }

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

            if (_messageShifts is { Count: > 0 } msgShifts)
            {
                foreach (var (chatId, s) in msgShifts)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE messages SET timestamp = date_trunc('day', NOW()) + ({0}::text)::interval " +
                        "WHERE chat_id = {1} AND message_id = {2}",
                        new object[] { FormatInterval(s.Offset), chatId, s.Id },
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
