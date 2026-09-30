namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Frozen analytics view definitions from before the verdict-event columns (source/classification)
/// drove the views (UpdateDetectionAnalyticsViews). Migrations older than that recreate the views
/// with these so a fresh database replays unchanged; the view classes' CreateViewSql constants track
/// the current schema and reference columns that do not exist yet at those points in history.
/// </summary>
internal static class LegacyDetectionViewSql
{
    /// <summary>detection_accuracy keyed on detection_source = 'manual' corrections joined by message_id.</summary>
    public const string DetectionAccuracy = """
        CREATE VIEW detection_accuracy AS
        WITH manual_corrections AS (
            SELECT DISTINCT ON (message_id)
                message_id,
                is_spam AS corrected_to_spam
            FROM detection_results
            WHERE detection_source = 'manual'
            ORDER BY message_id, detected_at DESC
        )
        SELECT
            dr.id,
            dr.message_id,
            dr.detected_at,
            DATE(dr.detected_at) AS detection_date,
            dr.is_spam AS original_classification,
            CASE WHEN mc.message_id IS NOT NULL
                 AND dr.is_spam AND NOT mc.corrected_to_spam
                 THEN TRUE ELSE FALSE END AS is_false_positive,
            CASE WHEN mc.message_id IS NOT NULL
                 AND NOT dr.is_spam AND mc.corrected_to_spam
                 THEN TRUE ELSE FALSE END AS is_false_negative
        FROM detection_results dr
        LEFT JOIN manual_corrections mc ON dr.message_id = mc.message_id
        WHERE dr.detection_source != 'manual';
        """;

    /// <summary>enriched_detections exposing detection_source, is_spam and net_score.</summary>
    public const string EnrichedDetections = """
        CREATE VIEW enriched_detections AS
        SELECT
            -- Detection columns
            dr.id,
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

            -- Actor columns (raw - app computes Actor via ActorMappings)
            dr.web_user_id,
            dr.telegram_user_id,
            dr.system_identifier,

            -- Actor enrichment (pre-joined for display)
            wu.email AS actor_web_email,
            actor_tu.username AS actor_telegram_username,
            actor_tu.first_name AS actor_telegram_first_name,
            actor_tu.last_name AS actor_telegram_last_name,

            -- Message enrichment
            m.user_id AS message_user_id,
            m.message_text,
            m.content_hash,
            msg_tu.username AS message_author_username,
            msg_tu.first_name AS message_author_first_name,
            msg_tu.last_name AS message_author_last_name

        FROM detection_results dr
        INNER JOIN messages m ON dr.message_id = m.message_id AND dr.chat_id = m.chat_id
        LEFT JOIN users wu ON dr.web_user_id = wu.id
        LEFT JOIN telegram_users actor_tu ON dr.telegram_user_id = actor_tu.telegram_user_id
        LEFT JOIN telegram_users msg_tu ON m.user_id = msg_tu.telegram_user_id;
        """;

    /// <summary>hourly_detection_stats counting every detection_results row.</summary>
    public const string HourlyDetectionStats = """
        CREATE VIEW hourly_detection_stats AS
        SELECT
            DATE(dr.detected_at) AS detection_date,
            EXTRACT(HOUR FROM dr.detected_at)::int AS detection_hour,
            COUNT(*) AS total_count,
            COUNT(*) FILTER (WHERE dr.is_spam) AS spam_count,
            COUNT(*) FILTER (WHERE NOT dr.is_spam) AS ham_count,
            COUNT(*) FILTER (WHERE dr.detection_source = 'manual') AS manual_count,
            AVG(dr.score) AS avg_score
        FROM detection_results dr
        GROUP BY DATE(dr.detected_at), EXTRACT(HOUR FROM dr.detected_at);
        """;
}
