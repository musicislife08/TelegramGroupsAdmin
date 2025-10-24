using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAutoTrustedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill is_trusted flag for users who were auto-trusted before this fix
            // Users with active Trust actions should have telegram_users.is_trusted = true
            migrationBuilder.Sql(@"
                UPDATE telegram_users
                SET is_trusted = true
                WHERE telegram_user_id IN (
                    SELECT DISTINCT user_id
                    FROM user_actions
                    WHERE action_type = 1  -- Trust (from UserActionType enum)
                      AND (expires_at IS NULL OR expires_at > NOW())
                )
                AND is_trusted = false;  -- Only update those not already marked
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No down migration needed - this is a data-only fix
            // Reverting would require tracking which users were backfilled vs manually trusted
        }
    }
}
