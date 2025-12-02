using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebNotificationsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_web_notifications_user_id",
                table: "web_notifications");

            migrationBuilder.CreateIndex(
                name: "ix_web_notifications_user_id_created_at",
                table: "web_notifications",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_web_notifications_user_id_created_at",
                table: "web_notifications");

            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_user_id",
                table: "web_notifications",
                column: "user_id");
        }
    }
}
