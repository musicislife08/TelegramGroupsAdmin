using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramSessionsAndUserApiConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_api_config",
                table: "configs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_api_hash_encrypted",
                table: "configs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    web_user_id = table.Column<string>(type: "character varying(450)", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    session_data = table.Column<byte[]>(type: "bytea", nullable: false),
                    member_chats = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_telegram_sessions_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_sessions_active",
                table: "telegram_sessions",
                column: "is_active",
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_sessions_unique_active_per_user",
                table: "telegram_sessions",
                column: "web_user_id",
                unique: true,
                filter: "is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_sessions");

            migrationBuilder.DropColumn(
                name: "user_api_config",
                table: "configs");

            migrationBuilder.DropColumn(
                name: "user_api_hash_encrypted",
                table: "configs");
        }
    }
}
