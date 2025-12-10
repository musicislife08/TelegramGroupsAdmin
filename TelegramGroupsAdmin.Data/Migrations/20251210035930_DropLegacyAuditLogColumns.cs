using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyAuditLogColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "target_user_id",
                table: "audit_log");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_user_id",
                table: "audit_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_user_id",
                table: "audit_log",
                type: "text",
                nullable: true);
        }
    }
}
