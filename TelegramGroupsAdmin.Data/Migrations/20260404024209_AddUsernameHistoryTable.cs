using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameHistoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "username_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    first_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_username_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_username_history_telegram_users_user_id",
                        column: x => x.user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_username_history_name_lower",
                table: "username_history",
                columns: new[] { "first_name", "last_name" });

            migrationBuilder.CreateIndex(
                name: "IX_username_history_user_id",
                table: "username_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_username_history_username_lower",
                table: "username_history",
                column: "username");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "username_history");
        }
    }
}
