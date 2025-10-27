using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixAuditLogCascadeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_telegram_users_actor_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_telegram_users_target_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_users_actor_web_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_users_target_web_user_id",
                table: "audit_log");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_telegram_users_actor_telegram_user_id",
                table: "audit_log",
                column: "actor_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_telegram_users_target_telegram_user_id",
                table: "audit_log",
                column: "target_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_users_actor_web_user_id",
                table: "audit_log",
                column: "actor_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_users_target_web_user_id",
                table: "audit_log",
                column: "target_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_telegram_users_actor_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_telegram_users_target_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_users_actor_web_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_users_target_web_user_id",
                table: "audit_log");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_telegram_users_actor_telegram_user_id",
                table: "audit_log",
                column: "actor_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_telegram_users_target_telegram_user_id",
                table: "audit_log",
                column: "target_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_users_actor_web_user_id",
                table: "audit_log",
                column: "actor_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_users_target_web_user_id",
                table: "audit_log",
                column: "target_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
