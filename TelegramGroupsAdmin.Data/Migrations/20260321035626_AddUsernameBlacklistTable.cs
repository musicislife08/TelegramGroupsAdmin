using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameBlacklistTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "username_blacklist",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    match_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_username_blacklist", x => x.id);
                    table.CheckConstraint("CK_username_blacklist_exclusive_actor", "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_username_blacklist_telegram_users_telegram_user_id",
                        column: x => x.telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_username_blacklist_users_web_user_id",
                        column: x => x.web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_username_blacklist_telegram_user_id",
                table: "username_blacklist",
                column: "telegram_user_id");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_username_blacklist_unique_enabled_pattern\" " +
                "ON username_blacklist (LOWER(pattern)) WHERE enabled = true;");

            migrationBuilder.CreateIndex(
                name: "IX_username_blacklist_web_user_id",
                table: "username_blacklist",
                column: "web_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "username_blacklist");
        }
    }
}
