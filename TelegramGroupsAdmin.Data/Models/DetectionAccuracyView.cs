using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for detection accuracy with pre-computed FP/FN flags.
/// Maps to detection_accuracy PostgreSQL view.
/// Simplifies false positive/negative detection using a self-join with human correction decisions.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// NOTE: Regular VIEW = stored query definition, no rows stored. Query runs fresh each time.
/// Can upgrade to MATERIALIZED VIEW with scheduled refresh if performance becomes an issue.
/// </summary>
public class DetectionAccuracyView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the detection_accuracy view. Referenced by migrations.
    /// Uses a CTE to find the latest human decision per (chat_id, message_id), then flags:
    /// - False Positive: the scan said spam, the latest decision said not-spam
    /// - False Negative: the scan said not-spam, the latest decision said spam
    /// Rows are detector output only (source 0 = ContentScan). Corrections are
    /// <see cref="VerdictSql.CorrectionSources"/>.
    /// </summary>
    public const string CreateViewSql = $"""
        CREATE VIEW detection_accuracy AS
        WITH corrections AS (
            SELECT DISTINCT ON (chat_id, message_id)
                chat_id, message_id, classification IN ({VerdictSql.SpamClassifications}) AS corrected_to_spam
            FROM detection_results
            WHERE source IN ({VerdictSql.CorrectionSources})
            ORDER BY chat_id, message_id, detected_at DESC, id DESC
        )
        SELECT dr.id, dr.chat_id, dr.message_id, dr.detected_at, date(dr.detected_at) AS detection_date,
               dr.classification IN ({VerdictSql.SpamClassifications}) AS original_classification,
               COALESCE(c.message_id IS NOT NULL AND dr.classification IN ({VerdictSql.SpamClassifications}) AND NOT c.corrected_to_spam, false) AS is_false_positive,
               COALESCE(c.message_id IS NOT NULL AND dr.classification NOT IN ({VerdictSql.SpamClassifications}) AND c.corrected_to_spam, false) AS is_false_negative
        FROM detection_results dr
        LEFT JOIN corrections c ON c.chat_id = dr.chat_id AND c.message_id = dr.message_id
        WHERE dr.source = 0;
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
    /// Chat the message belongs to (message ids are only unique per chat)
    /// </summary>
    [Column("chat_id")]
    public long ChatId { get; set; }

    /// <summary>
    /// Message ID this detection relates to
    /// </summary>
    [Column("message_id")]
    public int MessageId { get; set; }

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
    /// True if this was a false positive (scan said spam, latest human decision said ham)
    /// </summary>
    [Column("is_false_positive")]
    public bool IsFalsePositive { get; set; }

    /// <summary>
    /// True if this was a false negative (scan said ham, latest human decision said spam)
    /// </summary>
    [Column("is_false_negative")]
    public bool IsFalseNegative { get; set; }

    #endregion
}
