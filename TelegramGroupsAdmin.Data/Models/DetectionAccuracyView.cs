using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for detection accuracy with pre-computed FP/FN flags.
/// Maps to detection_accuracy PostgreSQL view.
/// Simplifies false positive/negative detection using self-join with manual corrections.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// NOTE: Regular VIEW = stored query definition, no rows stored. Query runs fresh each time.
/// Can upgrade to MATERIALIZED VIEW with scheduled refresh if performance becomes an issue.
/// </summary>
public class DetectionAccuracyView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the detection_accuracy view. Referenced by migrations.
    /// Uses CTE to find the latest manual correction per message, then flags:
    /// - False Positive: Originally spam, manually corrected to not-spam
    /// - False Negative: Originally not-spam, manually corrected to spam
    /// Excludes manual detections from the result set (we only care about automated accuracy).
    /// </summary>
    public const string CreateViewSql = """
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

    /// <summary>
    /// SQL to drop the detection_accuracy view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS detection_accuracy";

    #endregion

    #region Detection Columns

    /// <summary>
    /// Detection result ID
    /// </summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Message ID this detection relates to
    /// </summary>
    [Column("message_id")]
    public long MessageId { get; set; }

    /// <summary>
    /// Full timestamp of the detection (for timezone-aware grouping)
    /// </summary>
    [Column("detected_at")]
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>
    /// Date of the detection (UTC date from detected_at)
    /// </summary>
    [Column("detection_date")]
    public DateOnly DetectionDate { get; set; }

    /// <summary>
    /// Original automated classification (true = spam, false = ham)
    /// </summary>
    [Column("original_classification")]
    public bool OriginalClassification { get; set; }

    #endregion

    #region Accuracy Flags

    /// <summary>
    /// True if this was a false positive (detected as spam but manually corrected to ham)
    /// </summary>
    [Column("is_false_positive")]
    public bool IsFalsePositive { get; set; }

    /// <summary>
    /// True if this was a false negative (detected as ham but manually corrected to spam)
    /// </summary>
    [Column("is_false_negative")]
    public bool IsFalseNegative { get; set; }

    #endregion
}
