using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create analytics views using SQL from entity classes
            // This keeps view definitions co-located with entities for maintainability

            // 1. EnrichedDetectionView - pre-joins detection data with actor and message info
            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);

            // 2. HourlyDetectionStatsView - hourly aggregated detection stats
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);

            // 3. WelcomeResponseSummaryView - welcome response distributions by date/chat
            migrationBuilder.Sql(WelcomeResponseSummaryView.CreateViewSql);

            // 4. DetectionAccuracyView - pre-computed FP/FN flags
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop views in reverse order (dependencies first)
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);
            migrationBuilder.Sql(WelcomeResponseSummaryView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
        }
    }
}
