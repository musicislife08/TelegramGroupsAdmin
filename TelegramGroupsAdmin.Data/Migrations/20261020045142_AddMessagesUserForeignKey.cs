using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagesUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair before the key: every messages.user_id needs a telegram_users row.
            //
            // 1. User 0 (channel posts and samples without a sender) if absent: id only, not trusted,
            //    not active, not banned, the NOT NULL columns filled as TelegramUserRepository.GetOrUpdateAsync
            //    inserts them. An existing user 0 row is left alone.
            //
            // 2 + 3. Each message author with no row gets an id-only row: first/last seen = their earliest /
            //    latest message, names_observed_at NULL so the next observation records names. Trusted only
            //    for Telegram system accounts (TelegramConstants.SystemUserIds, frozen here), as
            //    GetOrUpdateAsync does. Banned, banned_at = the earliest qualifying decision, no expiry, when
            //    one of their messages (joined on message_id AND chat_id) has a spam detection_results row
            //    outside chat 0 that is only written after a successful ban: source AutoBan (10),
            //    WebMarkSpam (11), SpamCommand (13) or ReviewSpam (14), or a LegacyManual (99) row with the
            //    reason 'Marked as spam by moderator'. One INSERT computes the ban from the rows it inserts,
            //    so existing users are never touched.
            migrationBuilder.Sql("""
                INSERT INTO telegram_users (
                    telegram_user_id, username, first_name, last_name, names_observed_at,
                    is_bot, is_trusted, is_banned, bot_dm_enabled,
                    first_seen_at, last_seen_at, created_at, updated_at, is_active)
                VALUES (
                    0, NULL, NULL, NULL, NULL,
                    false, false, false, false,
                    now(), now(), now(), now(), false)
                ON CONFLICT (telegram_user_id) DO NOTHING;

                WITH orphans AS (
                    SELECT m.user_id, min(m."timestamp") AS first_seen, max(m."timestamp") AS last_seen
                    FROM messages m
                    WHERE NOT EXISTS (SELECT 1 FROM telegram_users u WHERE u.telegram_user_id = m.user_id)
                    GROUP BY m.user_id
                ),
                bans AS (
                    SELECT m.user_id, min(dr.detected_at) AS banned_at
                    FROM detection_results dr
                    JOIN messages m ON m.message_id = dr.message_id AND m.chat_id = dr.chat_id
                    JOIN orphans o ON o.user_id = m.user_id
                    WHERE dr.is_spam
                      AND dr.chat_id <> 0
                      AND (dr.source IN (10, 11, 13, 14)
                           OR (dr.source = 99 AND dr.reason = 'Marked as spam by moderator'))
                    GROUP BY m.user_id
                )
                INSERT INTO telegram_users (
                    telegram_user_id, username, first_name, last_name, names_observed_at,
                    is_bot, is_trusted, is_banned, banned_at, ban_expires_at, bot_dm_enabled,
                    first_seen_at, last_seen_at, created_at, updated_at, is_active)
                SELECT
                    o.user_id, NULL, NULL, NULL, NULL,
                    false, o.user_id IN (777000, 1087968824, 136817688, 1271266957, 5434988373),
                    b.banned_at IS NOT NULL, b.banned_at, NULL, false,
                    o.first_seen, o.last_seen, now(), now(), false
                FROM orphans o
                LEFT JOIN bans b ON b.user_id = o.user_id;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_messages_telegram_users_user_id",
                table: "messages",
                column: "user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The repaired rows stay: they are valid users and cannot be told apart from observed ones.
            migrationBuilder.DropForeignKey(
                name: "FK_messages_telegram_users_user_id",
                table: "messages");
        }
    }
}
