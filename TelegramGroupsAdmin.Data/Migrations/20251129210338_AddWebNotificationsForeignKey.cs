using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebNotificationsForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_web_notifications_user_id",
                table: "web_notifications",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_web_notifications_users_user_id",
                table: "web_notifications",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_web_notifications_users_user_id",
                table: "web_notifications");

            migrationBuilder.DropIndex(
                name: "IX_web_notifications_user_id",
                table: "web_notifications");
        }
    }
}
