using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueConstraintToMessageTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_translations_edit_id",
                table: "message_translations");

            migrationBuilder.DropIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_edit_id",
                table: "message_translations",
                column: "edit_id",
                unique: true,
                filter: "edit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations",
                column: "message_id",
                unique: true,
                filter: "message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_translations_edit_id",
                table: "message_translations");

            migrationBuilder.DropIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_edit_id",
                table: "message_translations",
                column: "edit_id",
                filter: "edit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations",
                column: "message_id",
                filter: "message_id IS NOT NULL");
        }
    }
}
