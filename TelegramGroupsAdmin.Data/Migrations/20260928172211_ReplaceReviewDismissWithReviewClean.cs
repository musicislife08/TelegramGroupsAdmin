using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceReviewDismissWithReviewClean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dismiss no longer writes a verdict. ReviewDismiss (15) never shipped, but a database that ran a
            // pre-release build can hold some; those rows only recorded an acknowledgement, so drop them.
            migrationBuilder.Sql("DELETE FROM detection_results WHERE source = 15;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results",
                sql: "(source IN (10, 11, 13, 14) AND classification = 0)\nOR (source IN (12, 19) AND classification = 1)\nOR (source IN (16, 18, 99) AND classification IN (0, 1))\nOR (source IN (1, 17) AND classification IN (4, 5))\nOR (source = 0 AND classification IN (2, 3, 4, 5))");

            // Human-decision source lists in the analytics views: ReviewClean (19) in, ReviewDismiss (15) out.
            // Frozen literals (not the view-class constants) so later edits to those constants cannot rewrite this migration.
            migrationBuilder.Sql("DROP VIEW IF EXISTS hourly_detection_stats;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS detection_accuracy;");

            migrationBuilder.Sql("""
                CREATE VIEW hourly_detection_stats AS
                SELECT date(detected_at) AS detection_date,
                       EXTRACT(hour FROM detected_at)::integer AS detection_hour,
                       count(*) FILTER (WHERE source = 0) AS total_count,
                       count(*) FILTER (WHERE source = 0 AND classification IN (0, 2, 4)) AS spam_count,
                       count(*) FILTER (WHERE source = 0 AND classification NOT IN (0, 2, 4)) AS ham_count,
                       count(*) FILTER (WHERE source IN (11, 12, 13, 14, 16, 17, 19, 99)) AS manual_count,
                       avg(score) FILTER (WHERE source = 0) AS avg_score
                FROM detection_results
                GROUP BY date(detected_at), EXTRACT(hour FROM detected_at);
                """);

            migrationBuilder.Sql("""
                CREATE VIEW detection_accuracy AS
                WITH corrections AS (
                    SELECT DISTINCT ON (chat_id, message_id)
                        chat_id, message_id, classification IN (0, 2, 4) AS corrected_to_spam
                    FROM detection_results
                    WHERE source IN (11, 12, 13, 14, 19, 99)
                    ORDER BY chat_id, message_id, detected_at DESC, id DESC
                )
                SELECT dr.id, dr.chat_id, dr.message_id, dr.detected_at, date(dr.detected_at) AS detection_date,
                       dr.classification IN (0, 2, 4) AS original_classification,
                       COALESCE(c.message_id IS NOT NULL AND dr.classification IN (0, 2, 4) AND NOT c.corrected_to_spam, false) AS is_false_positive,
                       COALESCE(c.message_id IS NOT NULL AND dr.classification NOT IN (0, 2, 4) AND c.corrected_to_spam, false) AS is_false_negative
                FROM detection_results dr
                LEFT JOIN corrections c ON c.chat_id = dr.chat_id AND c.message_id = dr.message_id
                WHERE dr.source = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results");

            // ReviewClean (19) has no pre-migration equivalent; WebMarkHam (12) keeps it ExplicitHam.
            migrationBuilder.Sql("UPDATE detection_results SET source = 12 WHERE source = 19;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results",
                sql: "(source IN (10, 11, 13, 14) AND classification = 0)\nOR (source = 12 AND classification = 1)\nOR (source = 15 AND classification = 3)\nOR (source IN (16, 18, 99) AND classification IN (0, 1))\nOR (source IN (1, 17) AND classification IN (4, 5))\nOR (source = 0 AND classification IN (2, 3, 4, 5))");

            migrationBuilder.Sql("DROP VIEW IF EXISTS hourly_detection_stats;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS detection_accuracy;");

            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.Sql("""
                CREATE VIEW detection_accuracy AS
                WITH corrections AS (
                    SELECT DISTINCT ON (chat_id, message_id)
                        chat_id, message_id, classification IN (0, 2, 4) AS corrected_to_spam
                    FROM detection_results
                    WHERE source IN (11, 12, 13, 14, 15, 99)   -- human decisions that can contradict a scan
                    ORDER BY chat_id, message_id, detected_at DESC, id DESC
                )
                SELECT dr.id, dr.chat_id, dr.message_id, dr.detected_at, date(dr.detected_at) AS detection_date,
                       dr.classification IN (0, 2, 4) AS original_classification,
                       COALESCE(c.message_id IS NOT NULL AND dr.classification IN (0, 2, 4) AND NOT c.corrected_to_spam, false) AS is_false_positive,
                       COALESCE(c.message_id IS NOT NULL AND dr.classification NOT IN (0, 2, 4) AND c.corrected_to_spam, false) AS is_false_negative
                FROM detection_results dr
                LEFT JOIN corrections c ON c.chat_id = dr.chat_id AND c.message_id = dr.message_id
                WHERE dr.source = 0;
                """);
        }
    }
}
