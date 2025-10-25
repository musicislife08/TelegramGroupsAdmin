using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActorExclusiveArcToAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_system_identifier",
                table: "audit_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "actor_telegram_user_id",
                table: "audit_log",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_web_user_id",
                table: "audit_log",
                type: "character varying(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_system_identifier",
                table: "audit_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "target_telegram_user_id",
                table: "audit_log",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_web_user_id",
                table: "audit_log",
                type: "character varying(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_actor_telegram_user_id",
                table: "audit_log",
                column: "actor_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_actor_web_user_id",
                table: "audit_log",
                column: "actor_web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_target_telegram_user_id",
                table: "audit_log",
                column: "target_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_target_web_user_id",
                table: "audit_log",
                column: "target_web_user_id");

            // ============================================================================
            // Data Migration: Populate new Actor exclusive arc columns from legacy fields
            // ============================================================================

            // Migrate actor_user_id → actor_web_user_id (non-null values are web user IDs)
            migrationBuilder.Sql(@"
                UPDATE audit_log
                SET actor_web_user_id = actor_user_id
                WHERE actor_user_id IS NOT NULL;
            ");

            // Migrate NULL actor_user_id → actor_system_identifier='SYSTEM' (system events)
            migrationBuilder.Sql(@"
                UPDATE audit_log
                SET actor_system_identifier = 'SYSTEM'
                WHERE actor_user_id IS NULL;
            ");

            // Migrate target_user_id → target_web_user_id OR target_telegram_user_id
            // Strategy: Check if target_user_id exists in users table (web) or is numeric (telegram)
            // Web user IDs are VARCHAR(450) ASP.NET Identity IDs (GUIDs), Telegram IDs are numeric strings

            // First, migrate web user targets (IDs that exist in users table)
            migrationBuilder.Sql(@"
                UPDATE audit_log
                SET target_web_user_id = target_user_id
                WHERE target_user_id IS NOT NULL
                  AND EXISTS (SELECT 1 FROM users WHERE id = audit_log.target_user_id);
            ");

            // Then, migrate telegram user targets (numeric IDs that don't exist in users table)
            // Cast to bigint for telegram_user_id column
            migrationBuilder.Sql(@"
                UPDATE audit_log
                SET target_telegram_user_id = CAST(target_user_id AS bigint)
                WHERE target_user_id IS NOT NULL
                  AND target_user_id ~ '^[0-9]+$'  -- Regex: only digits
                  AND NOT EXISTS (SELECT 1 FROM users WHERE id = audit_log.target_user_id);
            ");

            // ============================================================================
            // Add CHECK constraints AFTER data migration to ensure validity
            // ============================================================================

            migrationBuilder.AddCheckConstraint(
                name: "CK_audit_log_exclusive_actor",
                table: "audit_log",
                sql: "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_audit_log_exclusive_target",
                table: "audit_log",
                sql: "(target_web_user_id IS NULL AND target_telegram_user_id IS NULL AND target_system_identifier IS NULL) OR ((target_web_user_id IS NOT NULL)::int + (target_telegram_user_id IS NOT NULL)::int + (target_system_identifier IS NOT NULL)::int = 1)");

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

            migrationBuilder.DropIndex(
                name: "IX_audit_log_actor_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_actor_web_user_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_target_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_target_web_user_id",
                table: "audit_log");

            migrationBuilder.DropCheckConstraint(
                name: "CK_audit_log_exclusive_actor",
                table: "audit_log");

            migrationBuilder.DropCheckConstraint(
                name: "CK_audit_log_exclusive_target",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_system_identifier",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_web_user_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "target_system_identifier",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "target_telegram_user_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "target_web_user_id",
                table: "audit_log");
        }
    }
}
