using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileScanColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "telegram_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_pinned_stories",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_fake",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_scam",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_verified",
                table: "telegram_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "personal_channel_about",
                table: "telegram_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "personal_channel_id",
                table: "telegram_users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "personal_channel_title",
                table: "telegram_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pinned_story_captions",
                table: "telegram_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "profile_scan_score",
                table: "telegram_users",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "profile_scanned_at",
                table: "telegram_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_users_profile_scanned_at",
                table: "telegram_users",
                column: "profile_scanned_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telegram_users_profile_scanned_at",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "has_pinned_stories",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "is_fake",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "is_scam",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "is_verified",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "personal_channel_about",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "personal_channel_id",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "personal_channel_title",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "pinned_story_captions",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "profile_scan_score",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "profile_scanned_at",
                table: "telegram_users");
        }
    }
}
