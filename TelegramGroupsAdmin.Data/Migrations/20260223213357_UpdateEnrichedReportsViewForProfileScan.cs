using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEnrichedReportsViewForProfileScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop and recreate to add profile_user columns (type = 3)
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down would need the OLD view SQL to revert, but we reference the code constant
            // which always has the latest. Drop is safe — the original migration's Up will recreate.
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
        }
    }
}
