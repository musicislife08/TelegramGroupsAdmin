using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropUsernameBlacklistActorColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_username_blacklist_telegram_users_telegram_user_id",
                table: "username_blacklist");

            migrationBuilder.DropForeignKey(
                name: "FK_username_blacklist_users_web_user_id",
                table: "username_blacklist");

            migrationBuilder.DropIndex(
                name: "IX_username_blacklist_telegram_user_id",
                table: "username_blacklist");

            migrationBuilder.DropIndex(
                name: "IX_username_blacklist_web_user_id",
                table: "username_blacklist");

            migrationBuilder.DropCheckConstraint(
                name: "CK_username_blacklist_exclusive_actor",
                table: "username_blacklist");

            migrationBuilder.DropColumn(
                name: "system_identifier",
                table: "username_blacklist");

            migrationBuilder.DropColumn(
                name: "telegram_user_id",
                table: "username_blacklist");

            migrationBuilder.DropColumn(
                name: "web_user_id",
                table: "username_blacklist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_identifier",
                table: "username_blacklist",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "username_blacklist",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "web_user_id",
                table: "username_blacklist",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_username_blacklist_telegram_user_id",
                table: "username_blacklist",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_username_blacklist_web_user_id",
                table: "username_blacklist",
                column: "web_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_username_blacklist_exclusive_actor",
                table: "username_blacklist",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_username_blacklist_telegram_users_telegram_user_id",
                table: "username_blacklist",
                column: "telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_username_blacklist_users_web_user_id",
                table: "username_blacklist",
                column: "web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
