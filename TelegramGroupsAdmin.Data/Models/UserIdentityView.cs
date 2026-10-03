using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Keyless model of the user_identities view: a user's names plus the explicit flag from their
/// latest profile scan. Every identity read joins this view so the verdict comes from one place.
/// </summary>
public class UserIdentityView
{
    public const string CreateViewSql = """
        CREATE VIEW user_identities AS
        SELECT u.telegram_user_id, u.first_name, u.last_name, u.username, u.is_bot,
               s.ai_explicit_display_text AS latest_scan_explicit
        FROM telegram_users u
        LEFT JOIN LATERAL (
            SELECT r.ai_explicit_display_text
            FROM profile_scan_results r
            WHERE r.user_id = u.telegram_user_id
            ORDER BY r.scanned_at DESC, r.id DESC
            LIMIT 1
        ) s ON true
        """;

    public const string DropViewSql = "DROP VIEW IF EXISTS user_identities";

    [Column("telegram_user_id")] public long TelegramUserId { get; set; }
    [Column("first_name")] public string? FirstName { get; set; }
    [Column("last_name")] public string? LastName { get; set; }
    [Column("username")] public string? Username { get; set; }
    [Column("is_bot")] public bool IsBot { get; set; }
    /// <summary>NULL when the user has no profile scan.</summary>
    [Column("latest_scan_explicit")] public bool? LatestScanExplicit { get; set; }
}
