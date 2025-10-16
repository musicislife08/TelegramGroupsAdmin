using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_users",
                columns: table => new
                {
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    first_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_photo_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    photo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_trusted = table.Column<bool>(type: "boolean", nullable: false),
                    warning_points = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_users", x => x.telegram_user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_is_trusted",
                table: "telegram_users",
                column: "is_trusted");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_last_seen_at",
                table: "telegram_users",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_users_username",
                table: "telegram_users",
                column: "username");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_users");
        }
    }
}
