using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Constants;

namespace TelegramGroupsAdmin.Testing.Golden;

/// <summary>
/// In-place mutator for canonical substrate. Sibling of <see cref="GoldenReducePlanBuilder"/>
/// but strictly limited to <em>editing</em> existing rows — never creates them. Reach for this
/// only when canonical structurally cannot provide the shape (e.g., analytics aggregations
/// need timestamps relative to NOW() but canonical's timestamps are frozen at the bootstrap
/// snapshot date; an account lockout is only "locked" while <c>locked_until</c> is still ahead of
/// NOW()). If a verb would need to insert rows, that's the signal to extend canonical instead.
///
/// Verb count is intentionally bounded. The verbs' XML docs on this class are the register; each
/// has a self-test in <c>IntegrationTests/TestData/Tests/GoldenMutatePlanTests.cs</c> and a Part 2
/// recipe in <c>TelegramGroupsAdmin.IntegrationTests/CLAUDE.md</c> where a test shape needs it.
/// </summary>
public sealed class GoldenMutatePlanBuilder
{
    private readonly AppDbContext _context;
    private List<TimestampShift>? _detectionResultShifts;
    private List<TimestampShift>? _welcomeResponseShifts;
    private List<(long ChatId, TimestampShift Shift)>? _messageShifts;
    private List<(string UserId, TimeSpan LockFor)>? _webUserLocks;
    private List<(long TelegramUserId, TimeSpan ExpiresIn)>? _warningExtensions;
    private (IDataProtectionProvider Provider, string ApiKey)? _sendGridApiKey;

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

    /// <summary>
    /// Re-times the Telegram user's warnings: sets every element's <c>ExpiresAt</c> in
    /// <c>telegram_users.warnings</c> to <c>NOW() + expiresIn</c> where <c>telegram_user_id</c> matches,
    /// leaving IssuedAt, Reason, actor and context alone. A warning counts only while <c>ExpiresAt</c> is
    /// ahead of NOW() (90-day default expiry), so canonical's frozen snapshot carries only expired ones;
    /// this is the NOW()-relative shape, like <see cref="LockWebUser"/>. Fails loudly for a user with no
    /// warnings — that is a missing anchor, not something to invent. Other rows are left untouched.
    /// </summary>
    public GoldenMutatePlanBuilder ExtendTelegramUserWarnings(long telegramUserId, TimeSpan expiresIn)
    {
        if (expiresIn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), expiresIn, "a warning in force must expire in the future");
        }

        (_warningExtensions ??= new()).Add((telegramUserId, expiresIn));
        return this;
    }

    /// <summary>
    /// Adds a SendGrid API key to the stored global <c>configs.api_keys</c> (chat_id = 0): decrypts the
    /// canonical ciphertext with <paramref name="dataProtection"/> under <c>DataProtectionPurposes.ApiKeys</c>,
    /// sets <c>sendGrid</c> in the JSON (the AI connection keys stay), and re-encrypts. Canonical's
    /// <c>sendgrid_config</c> is already enabled with a from-address but, like every encrypted column, its
    /// key is not part of the SQL — so the app reads email as Disabled. The key only makes the app read
    /// email as configured (login page links, password reset); it is a dummy that is never sent anywhere.
    /// The provider must be the key ring the app under test uses. Fails loudly when no key set is stored.
    /// </summary>
    public GoldenMutatePlanBuilder EnableSendGridApiKey(IDataProtectionProvider dataProtection, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _sendGridApiKey = (dataProtection, apiKey);
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

            if (_warningExtensions is { Count: > 0 } extensions)
            {
                foreach (var (telegramUserId, expiresIn) in extensions)
                {
                    // jsonb_build_object renders the timestamptz as ISO 8601 with offset, the shape
                    // System.Text.Json wrote the canonical entries in (WarningEntry.ExpiresAt).
                    var rows = await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE telegram_users SET warnings = (" +
                        "  SELECT jsonb_agg(w || jsonb_build_object('ExpiresAt', NOW() + ({0}::text)::interval)) " +
                        "  FROM jsonb_array_elements(warnings) AS w) " +
                        "WHERE telegram_user_id = {1} AND jsonb_array_length(COALESCE(warnings, '[]'::jsonb)) > 0",
                        new object[] { FormatInterval(expiresIn), telegramUserId },
                        ct);
                    if (rows != 1)
                    {
                        throw new InvalidOperationException(
                            $"ExtendTelegramUserWarnings: expected exactly one telegram_users row with warnings for id {telegramUserId}, updated {rows}");
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

            if (_sendGridApiKey is { } sendGrid)
            {
                var protector = sendGrid.Provider.CreateProtector(DataProtectionPurposes.ApiKeys);
                var cipher = await _context.Configs.AsNoTracking()
                    .Where(c => c.ChatId == 0)
                    .Select(c => c.ApiKeys)
                    .SingleOrDefaultAsync(ct);
                if (string.IsNullOrEmpty(cipher))
                {
                    throw new InvalidOperationException("EnableSendGridApiKey: no stored global api_keys to extend");
                }

                var keys = JsonNode.Parse(protector.Unprotect(cipher))!.AsObject();
                keys["sendGrid"] = sendGrid.ApiKey;

                var rows = await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE configs SET api_keys = {0} WHERE chat_id = 0",
                    new object[] { protector.Protect(keys.ToJsonString()) },
                    ct);
                if (rows != 1)
                {
                    throw new InvalidOperationException($"EnableSendGridApiKey: expected to update exactly one configs row, updated {rows}");
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
