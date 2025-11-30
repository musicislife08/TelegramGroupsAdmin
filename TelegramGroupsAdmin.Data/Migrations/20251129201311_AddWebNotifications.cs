using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_notifications", x => x.id);
                });

            // Index for unread notification count queries: WHERE user_id = ? AND is_read = false
            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_user_id_is_read",
                table: "web_notifications",
                columns: new[] { "user_id", "is_read" });

            // Index for paginated recent notifications: WHERE user_id = ? ORDER BY created_at DESC
            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_user_id_created_at",
                table: "web_notifications",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_notifications");
        }
    }
}
