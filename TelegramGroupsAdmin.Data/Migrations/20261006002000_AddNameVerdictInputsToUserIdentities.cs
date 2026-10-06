using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNameVerdictInputsToUserIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // user_identities gains the verdict inputs (promotional flag, ban state). Both enriched
            // views select from it, so they drop first and come back with the new identity columns.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.CreateViewSql);
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(UserIdentityView.DropViewSql);
            migrationBuilder.Sql(LegacyUserIdentityViewSql.V1);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessagesV2);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedReportsV2);
        }
    }
}
