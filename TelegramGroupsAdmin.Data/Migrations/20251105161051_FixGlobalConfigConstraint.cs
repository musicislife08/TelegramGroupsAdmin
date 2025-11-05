using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixGlobalConfigConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_configs_chat_id",
                table: "configs");

            // CRITICAL: Handle edge case where both NULL and 0 rows exist (production data issue)
            // This can happen if newer code created chat_id=0 while older code still used NULL
            migrationBuilder.Sql(@"
                -- Step 1: Merge data from chat_id=0 into chat_id=NULL if both exist
                -- Use COALESCE to preserve existing values, only fill NULLs
                UPDATE configs AS target
                SET
                    spam_detection_config = COALESCE(target.spam_detection_config, source.spam_detection_config),
                    welcome_config = COALESCE(target.welcome_config, source.welcome_config),
                    log_config = COALESCE(target.log_config, source.log_config),
                    moderation_config = COALESCE(target.moderation_config, source.moderation_config),
                    bot_protection_config = COALESCE(target.bot_protection_config, source.bot_protection_config),
                    telegram_bot_config = COALESCE(target.telegram_bot_config, source.telegram_bot_config),
                    file_scanning_config = COALESCE(target.file_scanning_config, source.file_scanning_config),
                    background_jobs_config = COALESCE(target.background_jobs_config, source.background_jobs_config),
                    api_keys = COALESCE(target.api_keys, source.api_keys),
                    backup_encryption_config = COALESCE(target.backup_encryption_config, source.backup_encryption_config),
                    passphrase_encrypted = COALESCE(target.passphrase_encrypted, source.passphrase_encrypted),
                    invite_link = COALESCE(target.invite_link, source.invite_link),
                    telegram_bot_token_encrypted = COALESCE(target.telegram_bot_token_encrypted, source.telegram_bot_token_encrypted)
                FROM configs AS source
                WHERE target.chat_id IS NULL
                  AND source.chat_id = 0;

                -- Step 2: Delete any existing chat_id=0 rows (after merging data into NULL row)
                DELETE FROM configs WHERE chat_id = 0;

                -- Step 3: Now safe to migrate NULL to 0
                UPDATE configs SET chat_id = 0 WHERE chat_id IS NULL;
            ");

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "configs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            // Partial unique index: Only ONE global config allowed (chat_id = 0)
            // This enforces the singleton pattern for global configuration
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX idx_configs_single_global
                ON configs (chat_id)
                WHERE chat_id = 0;
            ");

            // Partial unique index: Each chat can have only ONE chat-specific config
            migrationBuilder.CreateIndex(
                name: "idx_configs_chat_specific",
                table: "configs",
                column: "chat_id",
                unique: true,
                filter: "chat_id != 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop both partial indexes
            migrationBuilder.DropIndex(
                name: "idx_configs_chat_specific",
                table: "configs");

            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_configs_single_global;");

            // Revert data migration: 0 → NULL
            migrationBuilder.Sql("UPDATE configs SET chat_id = NULL WHERE chat_id = 0;");

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "configs",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            // Restore original unique index (ineffective for NULL values)
            migrationBuilder.CreateIndex(
                name: "IX_configs_chat_id",
                table: "configs",
                column: "chat_id",
                unique: true);
        }
    }
}
