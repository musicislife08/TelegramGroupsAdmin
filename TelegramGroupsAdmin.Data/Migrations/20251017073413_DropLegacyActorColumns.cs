using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyActorColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop legacy actor columns (replaced by Actor system: web_user_id, telegram_user_id, system_identifier)
            migrationBuilder.DropColumn(
                name: "issued_by",
                table: "user_actions");

            migrationBuilder.DropColumn(
                name: "added_by",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "added_by",
                table: "stop_words");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore legacy actor columns (for rollback only - data will be lost)
            migrationBuilder.AddColumn<string>(
                name: "issued_by",
                table: "user_actions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "added_by",
                table: "detection_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "added_by",
                table: "stop_words",
                type: "text",
                nullable: true);
        }
    }
}
