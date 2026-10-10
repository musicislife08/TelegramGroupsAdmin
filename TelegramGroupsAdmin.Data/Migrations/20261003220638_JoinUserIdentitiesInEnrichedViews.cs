using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class JoinUserIdentitiesInEnrichedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Identity columns (names, is_bot, latest scan flag) now come from user_identities.
            // No other view selects from these two, so they drop and recreate on their own.
            // Replays the frozen V2 shape so later view changes cannot alter history.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessagesV2);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedReportsV2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the frozen telegram_users-joined shapes so user_identities has no dependents again.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessages);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedReports);
        }
    }
}
