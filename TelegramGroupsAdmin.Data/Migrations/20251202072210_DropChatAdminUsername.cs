using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropChatAdminUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean up orphaned chat_admin records before adding FK constraint.
            // These are admins who were detected but never sent a message, so they
            // don't have a telegram_users entry. The ChatManagementService now creates
            // user records before admin records, but legacy data may have orphans.
            migrationBuilder.Sql("""
                DELETE FROM chat_admins
                WHERE telegram_id NOT IN (
                    SELECT telegram_user_id FROM telegram_users
                );
                """);

            migrationBuilder.DropColumn(
                name: "username",
                table: "chat_admins");

            migrationBuilder.AddForeignKey(
                name: "FK_chat_admins_telegram_users_telegram_id",
                table: "chat_admins",
                column: "telegram_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chat_admins_telegram_users_telegram_id",
                table: "chat_admins");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "chat_admins",
                type: "text",
                nullable: true);
        }
    }
}
