using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserActionTargetUserNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete orphaned user_actions records that reference non-existent telegram_users
            // These records are useless without the associated user data
            migrationBuilder.Sql("""
                DELETE FROM user_actions
                WHERE user_id NOT IN (SELECT telegram_user_id FROM telegram_users)
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_user_actions_telegram_users_user_id",
                table: "user_actions",
                column: "user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_actions_telegram_users_user_id",
                table: "user_actions");
        }
    }
}
