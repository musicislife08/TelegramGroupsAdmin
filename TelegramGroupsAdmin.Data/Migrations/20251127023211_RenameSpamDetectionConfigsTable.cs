using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSpamDetectionConfigsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_spam_detection_configs",
                table: "spam_detection_configs");

            migrationBuilder.RenameTable(
                name: "spam_detection_configs",
                newName: "content_detection_configs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_content_detection_configs",
                table: "content_detection_configs",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_content_detection_configs",
                table: "content_detection_configs");

            migrationBuilder.RenameTable(
                name: "content_detection_configs",
                newName: "spam_detection_configs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_spam_detection_configs",
                table: "spam_detection_configs",
                column: "id");
        }
    }
}
