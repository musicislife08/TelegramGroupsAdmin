using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for hourly detection statistics.
/// Maps to hourly_detection_stats PostgreSQL view.
/// Provides hourly aggregates that can roll up to daily stats in C#.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// NOTE: Regular VIEW = stored query definition, no rows stored. Query runs fresh each time.
/// </summary>
public class HourlyDetectionStatsView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the hourly_detection_stats view. Referenced by migrations.
    /// Aggregates detection_results by date and hour for:
    /// - Dashboard daily stats (roll up hourly to daily)
    /// - Peak hour detection
    /// - Spam/ham trend analysis
    /// </summary>
    public const string CreateViewSql = """
        CREATE VIEW hourly_detection_stats AS
        SELECT
            DATE(dr.detected_at) AS detection_date,
            EXTRACT(HOUR FROM dr.detected_at)::int AS detection_hour,
            COUNT(*) AS total_count,
            COUNT(*) FILTER (WHERE dr.is_spam) AS spam_count,
            COUNT(*) FILTER (WHERE NOT dr.is_spam) AS ham_count,
            COUNT(*) FILTER (WHERE dr.detection_source = 'manual') AS manual_count,
            AVG(dr.confidence) AS avg_confidence
        FROM detection_results dr
        GROUP BY DATE(dr.detected_at), EXTRACT(HOUR FROM dr.detected_at);
        """;

    /// <summary>
    /// SQL to drop the hourly_detection_stats view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS hourly_detection_stats";

    #endregion

    #region Aggregation Columns

    /// <summary>
    /// Date of the detections (UTC date from detected_at)
    /// </summary>
    [Column("detection_date")]
    public DateOnly DetectionDate { get; set; }

    /// <summary>
    /// Hour of the day (0-23)
    /// </summary>
    [Column("detection_hour")]
    public int DetectionHour { get; set; }

    /// <summary>
    /// Total detections in this hour
    /// </summary>
    [Column("total_count")]
    public long TotalCount { get; set; }

    /// <summary>
    /// Number of spam detections
    /// </summary>
    [Column("spam_count")]
    public long SpamCount { get; set; }

    /// <summary>
    /// Number of ham (not spam) detections
    /// </summary>
    [Column("ham_count")]
    public long HamCount { get; set; }

    /// <summary>
    /// Number of manual classifications (reviews)
    /// </summary>
    [Column("manual_count")]
    public long ManualCount { get; set; }

    /// <summary>
    /// Average confidence score for this hour
    /// </summary>
    [Column("avg_confidence")]
    public double? AvgConfidence { get; set; }

    #endregion
}
