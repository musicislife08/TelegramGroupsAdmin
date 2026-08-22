using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentUserIdToEnrichedReportsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop and recreate to add content_user_id (type = 0)
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down references the code constant, which always has the latest shape.
            // Drop is safe — the original migration's Up recreates it.
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
        }
    }
}
