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

            // Inline SQL snapshot — the C# constant was later updated for composite keys
            // (chat_id joins) which don't exist at this migration's point in time.
            // AddCompositeMessageKey migration recreates this view with the updated SQL.
            migrationBuilder.Sql("""
                CREATE VIEW enriched_detections AS
                SELECT
                    dr.id, dr.message_id, dr.detected_at, dr.detection_source, dr.detection_method,
                    dr.is_spam, dr.confidence, dr.net_confidence, dr.reason, dr.check_results_json,
                    dr.edit_version,
                    dr.web_user_id, dr.telegram_user_id, dr.system_identifier,
                    wu.email AS actor_web_email,
                    actor_tu.username AS actor_telegram_username,
                    actor_tu.first_name AS actor_telegram_first_name,
                    actor_tu.last_name AS actor_telegram_last_name,
                    m.user_id AS message_user_id, m.message_text, m.content_hash,
                    msg_tu.username AS message_author_username,
                    msg_tu.first_name AS message_author_first_name,
                    msg_tu.last_name AS message_author_last_name
                FROM detection_results dr
                INNER JOIN messages m ON dr.message_id = m.message_id
                LEFT JOIN users wu ON dr.web_user_id = wu.id
                LEFT JOIN telegram_users actor_tu ON dr.telegram_user_id = actor_tu.telegram_user_id
                LEFT JOIN telegram_users msg_tu ON m.user_id = msg_tu.telegram_user_id;
                """);

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
