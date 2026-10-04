namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Frozen user_identities definition as created by AddUserIdentitiesView. The migration replays with
/// this copy so a later change to <c>UserIdentityView.CreateViewSql</c> cannot alter fresh-database history.
/// </summary>
internal static class LegacyUserIdentityViewSql
{
    public const string V1 = """
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
}
