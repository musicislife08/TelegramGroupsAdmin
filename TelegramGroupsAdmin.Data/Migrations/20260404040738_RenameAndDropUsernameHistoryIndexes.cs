using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameAndDropUsernameHistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_username_history_name_lower",
                table: "username_history");

            migrationBuilder.DropIndex(
                name: "IX_username_history_username_lower",
                table: "username_history");

            migrationBuilder.RenameIndex(
                name: "IX_username_history_user_id",
                table: "username_history",
                newName: "ix_username_history_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_username_history_user_id",
                table: "username_history",
                newName: "IX_username_history_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_username_history_name_lower",
                table: "username_history",
                columns: new[] { "first_name", "last_name" });

            migrationBuilder.CreateIndex(
                name: "IX_username_history_username_lower",
                table: "username_history",
                column: "username");
        }
    }
}
