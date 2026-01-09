using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToTelegramUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill 1: All existing users in telegram_users have sent messages, so set active
            migrationBuilder.Sql("UPDATE telegram_users SET is_active = true;");

            // Backfill 2: Create records for users in welcome_responses who don't exist in telegram_users
            // These are users who joined but never sent a message (timeouts, pending, denied, etc.)
            migrationBuilder.Sql(@"
                INSERT INTO telegram_users (telegram_user_id, username, is_active, is_trusted, is_bot, bot_dm_enabled, is_banned, first_seen_at, last_seen_at, created_at, updated_at)
                SELECT DISTINCT ON (wr.user_id)
                    wr.user_id,
                    wr.username,
                    false,
                    false,
                    false,
                    false,
                    false,
                    wr.created_at,
                    wr.created_at,
                    wr.created_at,
                    NOW()
                FROM welcome_responses wr
                WHERE NOT EXISTS (
                    SELECT 1 FROM telegram_users tu WHERE tu.telegram_user_id = wr.user_id
                )
                ORDER BY wr.user_id, wr.created_at ASC;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "telegram_users");
        }
    }
}
