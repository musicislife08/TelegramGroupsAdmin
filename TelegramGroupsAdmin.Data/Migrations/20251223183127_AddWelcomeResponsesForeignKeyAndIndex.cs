using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeResponsesForeignKeyAndIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_welcome_responses_user_id",
                table: "welcome_responses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_users_is_active",
                table: "telegram_users",
                column: "is_active",
                filter: "is_active = false");

            migrationBuilder.AddForeignKey(
                name: "FK_welcome_responses_telegram_users_user_id",
                table: "welcome_responses",
                column: "user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_welcome_responses_telegram_users_user_id",
                table: "welcome_responses");

            migrationBuilder.DropIndex(
                name: "IX_welcome_responses_user_id",
                table: "welcome_responses");

            migrationBuilder.DropIndex(
                name: "ix_telegram_users_is_active",
                table: "telegram_users");
        }
    }
}
