using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationStateToTelegramUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ban_expires_at",
                table: "telegram_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_banned",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "warnings",
                table: "telegram_users",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_is_banned",
                table: "telegram_users",
                column: "is_banned");

            // REFACTOR-5: Backfill is_banned from user_actions audit log
            // This migrates existing ban state from the audit log to the user table (source of truth).
            // A user is banned if their MOST RECENT ban/unban action is a Ban AND it hasn't expired.
            // Users who were banned then unbanned will correctly show as not banned.
            // UserActionType enum: Ban = 0, Unban = 4
            migrationBuilder.Sql(@"
                UPDATE telegram_users tu
                SET is_banned = true,
                    ban_expires_at = subq.expires_at
                FROM (
                    -- Get most recent ban/unban action per user
                    SELECT DISTINCT ON (user_id)
                        user_id,
                        action_type,
                        expires_at
                    FROM user_actions
                    WHERE action_type IN (0, 4)  -- Ban = 0, Unban = 4
                    ORDER BY user_id, issued_at DESC
                ) subq
                WHERE tu.telegram_user_id = subq.user_id
                  AND subq.action_type = 0  -- Most recent is Ban
                  AND (subq.expires_at IS NULL OR subq.expires_at > NOW());
            ");

            // NOTE: Warning backfill intentionally skipped
            // Reason: Complex migration (user_actions lacks context like message_id, chat_id needed for warnings JSONB),
            // and the warning system is not yet deployed to production. Starting fresh with new warnings table
            // is simpler and safer than attempting lossy migration from audit log.
            // Existing user_actions.Warn records remain for historical audit trail.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telegram_users_is_banned",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "ban_expires_at",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "is_banned",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "warnings",
                table: "telegram_users");
        }
    }
}
