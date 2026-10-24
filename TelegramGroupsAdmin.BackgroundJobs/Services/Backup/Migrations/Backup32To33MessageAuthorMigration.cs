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
        {
            data[TelegramUsers] = userRows = [];
            if (!backup.Metadata.Tables.Contains(TelegramUsers))
            {
                backup.Metadata.Tables.Add(TelegramUsers);
                backup.Metadata.TableCount = backup.Metadata.Tables.Count;
            }
        }

        var known = userRows.Select(r => Int64(TelegramUsers, ToElement(r), "telegram_user_id")).ToHashSet();
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
                (int)Int64(Messages, e, "message_id"), Int64(Messages, e, "chat_id"),
                Int64(Messages, e, "user_id"), Time(Messages, e, "timestamp"))).ToList()
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
                var at = Time(DetectionResults, d, "detected_at");
                var key = ((int)Int64(DetectionResults, d, "message_id"), Int64(DetectionResults, d, "chat_id"));
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
        if (Int64(DetectionResults, d, "chat_id") == 0)
            return false;

        var source = (int)Int64(DetectionResults, d, "source");
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

    private static JsonElement Required(string table, JsonElement row, string field, JsonValueKind kind)
    {
        if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out var value) && value.ValueKind == kind)
            return value;

        var id = row.ValueKind == JsonValueKind.Object
            ? new[] { "id", "telegram_user_id", "message_id" }
                .Where(k => row.TryGetProperty(k, out _)).Select(k => $"{k} {row.GetProperty(k)}").FirstOrDefault()
            : null;
        throw new InvalidOperationException(
            $"Backup {table} row {id ?? "(no id)"} has a missing or invalid required field '{field}'");
    }

    private static long Int64(string table, JsonElement row, string field) =>
        Required(table, row, field, JsonValueKind.Number).TryGetInt64(out var v)
            ? v
            : throw new InvalidOperationException($"Backup {table} field '{field}' is not an integer: {row.GetProperty(field)}");

    private static DateTimeOffset Time(string table, JsonElement row, string field) =>
        Required(table, row, field, JsonValueKind.String).TryGetDateTimeOffset(out var v)
            ? v
            : throw new InvalidOperationException($"Backup {table} field '{field}' is not a timestamp: {row.GetProperty(field)}");

    private static JsonElement ToElement(object row) =>
        row is JsonElement element ? element : JsonSerializer.SerializeToElement(row);

    private readonly record struct MessageKey(int MessageId, long ChatId, long UserId, DateTimeOffset At);
}
