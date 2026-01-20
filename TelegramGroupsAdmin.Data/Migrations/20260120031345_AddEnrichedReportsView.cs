using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichedReportsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the enriched_reports view using SQL from the entity class
            // This keeps the view definition co-located with the entity for maintainability
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
        }
    }
}
