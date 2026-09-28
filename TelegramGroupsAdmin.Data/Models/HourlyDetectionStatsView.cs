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
    /// Totals, spam/ham and the average score count only detector output (source 0 = ContentScan);
    /// manual_count counts human decisions (web/command/review/training-page sources and legacy manual rows).
    /// Classification literals (0, 2, 4) are the Spam set (VerdictClassifications.SpamValues).
    /// </summary>
    public const string CreateViewSql = """
        CREATE VIEW hourly_detection_stats AS
        SELECT date(detected_at) AS detection_date,
               EXTRACT(hour FROM detected_at)::integer AS detection_hour,
               count(*) FILTER (WHERE source = 0) AS total_count,
               count(*) FILTER (WHERE source = 0 AND classification IN (0, 2, 4)) AS spam_count,
               count(*) FILTER (WHERE source = 0 AND classification NOT IN (0, 2, 4)) AS ham_count,
               count(*) FILTER (WHERE source IN (11, 12, 13, 14, 15, 16, 17, 99)) AS manual_count,
               avg(score) FILTER (WHERE source = 0) AS avg_score
        FROM detection_results
        GROUP BY date(detected_at), EXTRACT(hour FROM detected_at);
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
    /// Total detector scans (ContentScan rows) in this hour
    /// </summary>
    [Column("total_count")]
    public long TotalCount { get; set; }

    /// <summary>
    /// Number of detector scans classified as spam
    /// </summary>
    [Column("spam_count")]
    public long SpamCount { get; set; }

    /// <summary>
    /// Number of detector scans classified as not spam
    /// </summary>
    [Column("ham_count")]
    public long HamCount { get; set; }

    /// <summary>
    /// Number of human decisions (reviews, web/command marks, training-data edits)
    /// </summary>
    [Column("manual_count")]
    public long ManualCount { get; set; }

    /// <summary>
    /// Average detector score for this hour (ContentScan rows only)
    /// </summary>
    [Column("avg_score")]
    public double? AvgScore { get; set; }

    #endregion
}
