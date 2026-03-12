using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBannedAtToTelegramUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "banned_at",
                table: "telegram_users",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill from most recent Ban action in user_actions for currently-banned users
            migrationBuilder.Sql("""
                UPDATE telegram_users tu
                SET banned_at = (
                    SELECT MAX(ua.issued_at)
                    FROM user_actions ua
                    WHERE ua.user_id = tu.telegram_user_id
                      AND ua.action_type = 0
                )
                WHERE tu.is_banned = true
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "banned_at",
                table: "telegram_users");
        }
    }
}
