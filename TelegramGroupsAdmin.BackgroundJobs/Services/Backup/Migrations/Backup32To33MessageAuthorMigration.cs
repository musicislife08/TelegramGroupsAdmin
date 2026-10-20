using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;

/// <summary>
/// Backup format 3.2 → 3.3 (every message has a user row): a 3.2 backup can hold messages whose author has
/// no telegram_users row, and no user 0 row. The schema now has the foreign key messages.user_id →
/// telegram_users, so restore would fail; this adds the missing rows to the backup's telegram_users.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate implementation from the SQL migration <c>AddMessagesUserForeignKey</c>
/// (backup-only, time-boxed), applying the same rules to the backup's JSON rows: user 0 is added when
/// absent (id only, never banned); each other message author without a row gets an id-only row spanning
/// their messages (first/last seen), trusted only for Telegram system accounts (frozen list, as in the
/// SQL migration), banned (no expiry, banned_at = earliest qualifying decision) when one of their messages,
/// joined on message_id AND chat_id, has a spam decision outside chat 0 with source 10, 11, 13 or 14, or
/// source 99 with the reason 'Marked as spam by moderator'. Spam is derived from classification (0, 2, 4),
/// not from the exported is_spam. Existing users are never touched.
/// </para>
/// <para>
/// Remove this class, and its call in <c>BackupService.ApplyBackupMigrations</c>, one year after the
/// release that introduced backup format 3.3.
/// </para>
/// </remarks>
public static class Backup32To33MessageAuthorMigration
{
    private const string TelegramUsers = "telegram_users";
    private const string Messages = "messages";
    private const string DetectionResults = "detection_results";

    private const string LegacyModeratorReason = "Marked as spam by moderator";

    // VerdictSource: AutoBan, WebMarkSpam, SpamCommand, ReviewSpam; LegacyManual is 99.
    private static readonly int[] BanningSources = [10, 11, 13, 14];
    private const int LegacyManualSource = 99;

    // VerdictClassification: ExplicitSpam, ImplicitSpam, UntrainedSpam (VerdictClassifications.Spam).
    private static readonly int[] SpamClassifications = [0, 2, 4];

    // TelegramConstants.SystemUserIds, frozen as in the SQL migration.
    private static readonly long[] SystemUserIds = [777000, 1087968824, 136817688, 1271266957, 5434988373];

    public static void Apply(SystemBackup backup, ILogger logger)
    {
        backup.Data ??= [];
        var data = backup.Data;

        if (!data.TryGetValue(TelegramUsers, out var userRows))
            data[TelegramUsers] = userRows = [];

        var known = userRows.Select(r => ToElement(r).GetProperty("telegram_user_id").GetInt64()).ToHashSet();
        var added = 0;

        // User 0 first, so messages without a sender are never orphans (and never banned).
        if (known.Add(0))
        {
            var now = DateTimeOffset.UtcNow;
            userRows.Add(NewUser(0, now, now, trusted: false, bannedAt: null));
            added++;
        }

        var messages = data.TryGetValue(Messages, out var messageRows)
            ? messageRows.Select(r => ToElement(r)).Select(e => new MessageKey(
                e.GetProperty("message_id").GetInt32(), e.GetProperty("chat_id").GetInt64(),
                e.GetProperty("user_id").GetInt64(), e.GetProperty("timestamp").GetDateTimeOffset())).ToList()
            : [];

        var orphans = messages.Where(m => !known.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g => (First: g.Min(m => m.At), Last: g.Max(m => m.At)));

        var authorsByMessage = messages.Where(m => orphans.ContainsKey(m.UserId))
            .ToLookup(m => (m.MessageId, m.ChatId), m => m.UserId);

        var bans = new Dictionary<long, DateTimeOffset>();
        if (orphans.Count > 0 && data.TryGetValue(DetectionResults, out var decisionRows))
        {
            foreach (var d in decisionRows.Select(r => ToElement(r)))
            {
                if (!QualifiesForBan(d))
                    continue;
                var at = d.GetProperty("detected_at").GetDateTimeOffset();
                var key = (d.GetProperty("message_id").GetInt32(), d.GetProperty("chat_id").GetInt64());
                foreach (var userId in authorsByMessage[key].Distinct())
                    if (!bans.TryGetValue(userId, out var earliest) || at < earliest)
                        bans[userId] = at;
            }
        }

        foreach (var (userId, span) in orphans)
        {
            userRows.Add(NewUser(userId, span.First, span.Last, SystemUserIds.Contains(userId),
                bans.TryGetValue(userId, out var bannedAt) ? bannedAt : null));
            added++;
        }

        logger.LogInformation("Backup 3.2→3.3: added {Added} missing telegram_users rows for message authors ({Banned} banned)",
            added, bans.Count);
    }

    private static bool QualifiesForBan(JsonElement d)
    {
        if (!d.TryGetProperty("classification", out var cls) || cls.ValueKind != JsonValueKind.Number
            || !SpamClassifications.Contains(cls.GetInt32()))
            return false;
        if (d.GetProperty("chat_id").GetInt64() == 0)
            return false;

        var source = d.GetProperty("source").GetInt32();
        if (BanningSources.Contains(source))
            return true;

        return source == LegacyManualSource
            && d.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            && reason.GetString() == LegacyModeratorReason;
    }

    private static object NewUser(long id, DateTimeOffset firstSeen, DateTimeOffset lastSeen, bool trusted, DateTimeOffset? bannedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["telegram_user_id"] = id,
            ["username"] = null,
            ["first_name"] = null,
            ["last_name"] = null,
            ["names_observed_at"] = null,
            ["is_bot"] = false,
            ["is_trusted"] = trusted,
            ["is_active"] = false,
            ["is_banned"] = bannedAt is not null,
            ["banned_at"] = bannedAt,
            ["ban_expires_at"] = null,
            ["kick_count"] = 0,
            ["bot_dm_enabled"] = false,
            ["first_seen_at"] = firstSeen,
            ["last_seen_at"] = lastSeen,
            ["created_at"] = now,
            ["updated_at"] = now,
            ["has_pinned_stories"] = false,
            ["is_scam"] = false,
            ["is_fake"] = false,
            ["is_verified"] = false,
            ["profile_scan_excluded"] = false
        });
    }

    private static JsonElement ToElement(object row) =>
        row is JsonElement element ? element : JsonSerializer.SerializeToElement(row);

    private readonly record struct MessageKey(int MessageId, long ChatId, long UserId, DateTimeOffset At);
}
