using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Detector analytics views read the verdict-event columns: rows of the detector views are
    /// ContentScan events (source 0), spam is the classification Spam set, and accuracy
    /// corrections are human decisions on the same (chat_id, message_id).
    /// enriched_detections exposes source/classification (is_spam computed) instead of
    /// detection_source/net_score.
    /// </summary>
    public partial class UpdateDetectionAnalyticsViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            // Previous definitions, captured with pg_get_viewdef before this migration was applied.
            migrationBuilder.Sql("""
                CREATE VIEW enriched_detections AS
                 SELECT dr.id,
                    dr.message_id,
                    dr.detected_at,
                    dr.detection_source,
                    dr.detection_method,
                    dr.is_spam,
                    dr.score,
                    dr.net_score,
                    dr.reason,
                    dr.check_results_json,
                    dr.edit_version,
                    dr.chat_id,
                    dr.web_user_id,
                    dr.telegram_user_id,
                    dr.system_identifier,
                    wu.email AS actor_web_email,
                    actor_tu.username AS actor_telegram_username,
                    actor_tu.first_name AS actor_telegram_first_name,
                    actor_tu.last_name AS actor_telegram_last_name,
                    m.user_id AS message_user_id,
                    m.message_text,
                    m.content_hash,
                    msg_tu.username AS message_author_username,
                    msg_tu.first_name AS message_author_first_name,
                    msg_tu.last_name AS message_author_last_name
                   FROM detection_results dr
                     JOIN messages m ON dr.message_id = m.message_id AND dr.chat_id = m.chat_id
                     LEFT JOIN users wu ON dr.web_user_id::text = wu.id::text
                     LEFT JOIN telegram_users actor_tu ON dr.telegram_user_id = actor_tu.telegram_user_id
                     LEFT JOIN telegram_users msg_tu ON m.user_id = msg_tu.telegram_user_id;
                """);

            migrationBuilder.Sql("""
                CREATE VIEW hourly_detection_stats AS
                 SELECT date(detected_at) AS detection_date,
                    EXTRACT(hour FROM detected_at)::integer AS detection_hour,
                    count(*) AS total_count,
                    count(*) FILTER (WHERE is_spam) AS spam_count,
                    count(*) FILTER (WHERE NOT is_spam) AS ham_count,
                    count(*) FILTER (WHERE detection_source::text = 'manual'::text) AS manual_count,
                    avg(score) AS avg_score
                   FROM detection_results dr
                  GROUP BY (date(detected_at)), (EXTRACT(hour FROM detected_at));
                """);

            migrationBuilder.Sql("""
                CREATE VIEW detection_accuracy AS
                 WITH manual_corrections AS (
                         SELECT DISTINCT ON (detection_results.message_id) detection_results.message_id,
                            detection_results.is_spam AS corrected_to_spam
                           FROM detection_results
                          WHERE detection_results.detection_source::text = 'manual'::text
                          ORDER BY detection_results.message_id, detection_results.detected_at DESC
                        )
                 SELECT dr.id,
                    dr.message_id,
                    dr.detected_at,
                    date(dr.detected_at) AS detection_date,
                    dr.is_spam AS original_classification,
                        CASE
                            WHEN mc.message_id IS NOT NULL AND dr.is_spam AND NOT mc.corrected_to_spam THEN true
                            ELSE false
                        END AS is_false_positive,
                        CASE
                            WHEN mc.message_id IS NOT NULL AND NOT dr.is_spam AND mc.corrected_to_spam THEN true
                            ELSE false
                        END AS is_false_negative
                   FROM detection_results dr
                     LEFT JOIN manual_corrections mc ON dr.message_id = mc.message_id
                  WHERE dr.detection_source::text <> 'manual'::text;
                """);
        }
    }
}
