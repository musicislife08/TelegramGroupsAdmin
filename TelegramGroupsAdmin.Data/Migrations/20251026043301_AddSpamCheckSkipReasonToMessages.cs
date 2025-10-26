using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamCheckSkipReasonToMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "spam_check_skip_reason",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill existing data: Intelligent detection of skip reasons
            // For messages without detection_results, infer why they were skipped

            // Step 1: Mark messages from chat admins as UserAdmin (2)
            migrationBuilder.Sql(@"
                UPDATE messages m
                SET spam_check_skip_reason = 2
                WHERE NOT EXISTS (
                    SELECT 1 FROM detection_results WHERE message_id = m.message_id
                )
                AND EXISTS (
                    SELECT 1 FROM chat_admins ca
                    WHERE ca.chat_id = m.chat_id
                    AND ca.telegram_id = m.user_id
                    AND ca.is_active = true
                );
            ");

            // Step 2: Mark messages from trusted users as UserTrusted (1)
            // Only if not already marked as UserAdmin
            migrationBuilder.Sql(@"
                UPDATE messages m
                SET spam_check_skip_reason = 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM detection_results WHERE message_id = m.message_id
                )
                AND spam_check_skip_reason = 0
                AND EXISTS (
                    SELECT 1 FROM telegram_users tu
                    WHERE tu.telegram_user_id = m.user_id
                    AND tu.is_trusted = true
                );
            ");

            // Note: Messages that remain at 0 (NotSkipped) but have no detection_results
            // are likely old data from before spam detection was implemented, or edge cases
            // like command messages. Leaving them as NotSkipped is the most honest representation.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "spam_check_skip_reason",
                table: "messages");
        }
    }
}
