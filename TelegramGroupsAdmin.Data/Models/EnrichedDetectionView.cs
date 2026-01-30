using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for enriched detection results with actor and message data pre-joined.
/// Maps to enriched_detections PostgreSQL view.
/// Simplifies the 4-table LEFT JOIN chain in GetRecentDetectionsAsync.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// </summary>
public class EnrichedDetectionView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the enriched_detections view. Referenced by migrations.
    /// Pre-joins detection_results with:
    /// - messages (message content, user_id)
    /// - users (web user email for actor display)
    /// - telegram_users (actor telegram user info)
    /// - telegram_users (message author info)
    /// Actor columns exposed raw - app-layer ActorMappings.ToActor() handles computation.
    /// </summary>
    public const string CreateViewSql = """
        CREATE VIEW enriched_detections AS
        SELECT
            -- Detection columns
            dr.id,
            dr.message_id,
            dr.detected_at,
            dr.detection_source,
            dr.detection_method,
            dr.is_spam,
            dr.confidence,
            dr.net_confidence,
            dr.reason,
            dr.check_results_json,
            dr.edit_version,

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
        INNER JOIN messages m ON dr.message_id = m.message_id
        LEFT JOIN users wu ON dr.web_user_id = wu.id
        LEFT JOIN telegram_users actor_tu ON dr.telegram_user_id = actor_tu.telegram_user_id
        LEFT JOIN telegram_users msg_tu ON m.user_id = msg_tu.telegram_user_id;
        """;

    /// <summary>
    /// SQL to drop the enriched_detections view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS enriched_detections";

    #endregion

    #region Detection Columns (from detection_results table)

    [Column("id")]
    public long Id { get; set; }

    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("detected_at")]
    public DateTimeOffset DetectedAt { get; set; }

    [Column("detection_source")]
    public string DetectionSource { get; set; } = string.Empty;

    [Column("detection_method")]
    public string DetectionMethod { get; set; } = string.Empty;

    [Column("is_spam")]
    public bool IsSpam { get; set; }

    [Column("confidence")]
    public int Confidence { get; set; }

    [Column("net_confidence")]
    public int NetConfidence { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("check_results_json")]
    public string? CheckResultsJson { get; set; }

    [Column("edit_version")]
    public int EditVersion { get; set; }

    #endregion

    #region Actor Columns (raw - app computes Actor via ActorMappings)

    [Column("web_user_id")]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    public string? SystemIdentifier { get; set; }

    #endregion

    #region Actor Enrichment (from users and telegram_users JOINs)

    [Column("actor_web_email")]
    public string? ActorWebEmail { get; set; }

    [Column("actor_telegram_username")]
    public string? ActorTelegramUsername { get; set; }

    [Column("actor_telegram_first_name")]
    public string? ActorTelegramFirstName { get; set; }

    [Column("actor_telegram_last_name")]
    public string? ActorTelegramLastName { get; set; }

    #endregion

    #region Message Enrichment (from messages and telegram_users JOINs)

    [Column("message_user_id")]
    public long MessageUserId { get; set; }

    [Column("message_text")]
    public string? MessageText { get; set; }

    [Column("content_hash")]
    public string? ContentHash { get; set; }

    [Column("message_author_username")]
    public string? MessageAuthorUsername { get; set; }

    [Column("message_author_first_name")]
    public string? MessageAuthorFirstName { get; set; }

    [Column("message_author_last_name")]
    public string? MessageAuthorLastName { get; set; }

    #endregion
}
