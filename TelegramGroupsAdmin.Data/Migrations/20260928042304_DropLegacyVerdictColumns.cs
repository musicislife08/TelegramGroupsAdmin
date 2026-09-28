using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Final verdict-event schema: is_spam is generated from classification, source and
    /// classification are required and CHECKed against each other, and the legacy verdict columns
    /// (net_score, used_for_training, detection_source) plus training_labels and the image/video
    /// sample tables are dropped. message_verdicts passes is_spam through.
    /// </summary>
    public partial class DropLegacyVerdictColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Drop the views that read detection_results (they block the column changes) ──
            migrationBuilder.Sql(MessageVerdictView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            // ── 2. is_spam generated from classification; source/classification required ──
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_detection_results_is_spam;
                DROP INDEX IF EXISTS ix_detection_results_is_spam_detected_at;
                ALTER TABLE detection_results DROP COLUMN is_spam;
                ALTER TABLE detection_results ALTER COLUMN source SET NOT NULL;
                ALTER TABLE detection_results ALTER COLUMN classification SET NOT NULL;
                ALTER TABLE detection_results ADD COLUMN is_spam boolean GENERATED ALWAYS AS (classification IN (0, 2, 4)) STORED;
                CREATE INDEX ix_detection_results_is_spam ON detection_results (is_spam);
                CREATE INDEX ix_detection_results_is_spam_detected_at ON detection_results (is_spam, detected_at);
                """);

            // ── 3. Legacy tables, indexes and columns; source/classification consistency ──
            migrationBuilder.DropTable(
                name: "image_training_samples");

            migrationBuilder.DropTable(
                name: "training_labels");

            migrationBuilder.DropTable(
                name: "video_training_samples");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_detection_source",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "IX_detection_results_used_for_training",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "detection_source",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "net_score",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "used_for_training",
                table: "detection_results");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_classification",
                table: "detection_results",
                sql: "classification IN (0, 1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results",
                sql: "(source IN (10, 11, 13, 14) AND classification = 0)\nOR (source = 12 AND classification = 1)\nOR (source = 15 AND classification = 3)\nOR (source IN (16, 18, 99) AND classification IN (0, 1))\nOR (source IN (1, 17) AND classification IN (4, 5))\nOR (source = 0 AND classification IN (2, 3, 4, 5))");

            // ── 4. Recreate the views on the final schema ──
            // Frozen literals (not the view-class constants) so later edits to those constants cannot rewrite this migration.
            migrationBuilder.Sql("""
                CREATE VIEW message_verdicts AS
                SELECT m.chat_id, m.message_id,
                       COALESCE(v.classification, 6) AS classification,
                       COALESCE(v.is_spam, false) AS is_spam,
                       v.source, v.detected_at, v.id AS verdict_id
                FROM messages m
                LEFT JOIN LATERAL (
                    SELECT d.classification, d.is_spam, d.source, d.detected_at, d.id
                    FROM detection_results d
                    WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id AND d.source <> 1
                    ORDER BY d.detected_at DESC, d.id DESC
                    LIMIT 1) v ON true;
                """);

            migrationBuilder.Sql("""
                CREATE VIEW enriched_detections AS
                SELECT
                    -- Detection columns
                    dr.id,
                    dr.message_id,
                    dr.detected_at,
                    dr.source,
                    dr.detection_method,
                    dr.classification,
                    (dr.classification IN (0, 2, 4)) AS is_spam,
                    dr.score,
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
                """);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NOTE: Up is one-way for data. training_labels, image_training_samples and
            // video_training_samples are recreated empty; their rows are not restored. The legacy
            // detection_results columns are re-derived from source/classification/score the way the
            // verdict writer kept them in step before this migration.

            // Drop views first (depend on columns)
            migrationBuilder.Sql(MessageVerdictView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_classification",
                table: "detection_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results");

            // Legacy columns back, then is_spam computed from net_score again
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_detection_results_is_spam;
                DROP INDEX IF EXISTS ix_detection_results_is_spam_detected_at;
                ALTER TABLE detection_results DROP COLUMN is_spam;
                ALTER TABLE detection_results ALTER COLUMN source DROP NOT NULL;
                ALTER TABLE detection_results ALTER COLUMN classification DROP NOT NULL;

                ALTER TABLE detection_results ADD COLUMN detection_source character varying(50) NOT NULL DEFAULT '';
                ALTER TABLE detection_results ADD COLUMN used_for_training boolean NOT NULL DEFAULT false;
                ALTER TABLE detection_results ADD COLUMN net_score double precision NOT NULL DEFAULT 0;

                UPDATE detection_results SET
                    detection_source = CASE source WHEN 0 THEN 'auto' WHEN 1 THEN 'file_scan' WHEN 18 THEN 'tg-spam-import' ELSE 'manual' END,
                    net_score = CASE WHEN classification IN (0, 2, 4) THEN abs(score) ELSE -abs(score) END,
                    used_for_training = classification = 2 OR (source IN (16, 18) AND classification IN (0, 1));

                ALTER TABLE detection_results ALTER COLUMN detection_source DROP DEFAULT;
                ALTER TABLE detection_results ALTER COLUMN used_for_training DROP DEFAULT;
                ALTER TABLE detection_results ALTER COLUMN net_score DROP DEFAULT;

                ALTER TABLE detection_results ADD COLUMN is_spam boolean GENERATED ALWAYS AS (net_score > 0) STORED;
                CREATE INDEX ix_detection_results_is_spam ON detection_results (is_spam);
                CREATE INDEX ix_detection_results_is_spam_detected_at ON detection_results (is_spam, detected_at);
                CREATE INDEX ix_detection_results_detection_source ON detection_results (detection_source);
                CREATE INDEX "IX_detection_results_used_for_training" ON detection_results (used_for_training);
                """);

            // Legacy tables (schema only)
            migrationBuilder.CreateTable(
                name: "image_training_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    file_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    marked_by_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    marked_by_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    marked_by_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    photo_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    photo_path = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_training_samples", x => x.id);
                    table.CheckConstraint("CK_image_training_exclusive_actor", "(marked_by_web_user_id IS NOT NULL)::int + (marked_by_telegram_user_id IS NOT NULL)::int + (marked_by_system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_image_training_samples_messages_message_id_chat_id",
                        columns: x => new { x.message_id, x.chat_id },
                        principalTable: "messages",
                        principalColumns: new[] { "message_id", "chat_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_training_samples_telegram_users_marked_by_telegram_us~",
                        column: x => x.marked_by_telegram_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_image_training_samples_users_marked_by_web_user_id",
                        column: x => x.marked_by_web_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "training_labels",
                columns: table => new
                {
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    labeled_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    audit_log_id = table.Column<long>(type: "bigint", nullable: true),
                    label = table.Column<short>(type: "smallint", nullable: false),
                    labeled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_labels", x => new { x.message_id, x.chat_id });
                    table.CheckConstraint("CK_training_labels_label", "label IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_training_labels_messages_message_id_chat_id",
                        columns: x => new { x.message_id, x.chat_id },
                        principalTable: "messages",
                        principalColumns: new[] { "message_id", "chat_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_training_labels_telegram_users_labeled_by_user_id",
                        column: x => x.labeled_by_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "video_training_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    file_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    has_audio = table.Column<bool>(type: "boolean", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    keyframe_hashes = table.Column<string>(type: "jsonb", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    marked_by_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    marked_by_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    marked_by_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    video_path = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_training_samples", x => x.id);
                    table.CheckConstraint("CK_video_training_exclusive_actor", "(marked_by_web_user_id IS NOT NULL)::int + (marked_by_telegram_user_id IS NOT NULL)::int + (marked_by_system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_video_training_samples_messages_message_id_chat_id",
                        columns: x => new { x.message_id, x.chat_id },
                        principalTable: "messages",
                        principalColumns: new[] { "message_id", "chat_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_is_spam_marked_at",
                table: "image_training_samples",
                columns: new[] { "is_spam", "marked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_marked_by_telegram_user_id",
                table: "image_training_samples",
                column: "marked_by_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_marked_by_web_user_id",
                table: "image_training_samples",
                column: "marked_by_web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_message_id_chat_id",
                table: "image_training_samples",
                columns: new[] { "message_id", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label",
                table: "training_labels",
                column: "label");

            migrationBuilder.CreateIndex(
                name: "IX_training_labels_label_labeled_at",
                table: "training_labels",
                columns: new[] { "label", "labeled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_training_labels_labeled_by_user_id",
                table: "training_labels",
                column: "labeled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_is_spam_marked_at",
                table: "video_training_samples",
                columns: new[] { "is_spam", "marked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_message_id_chat_id",
                table: "video_training_samples",
                columns: new[] { "message_id", "chat_id" },
                unique: true);

            // Previous view definitions (frozen literals): the transitional message_verdicts from
            // AddVerdictEvents and the analytics views from UpdateDetectionAnalyticsViews.
            migrationBuilder.Sql("""
                CREATE VIEW message_verdicts AS
                SELECT m.chat_id, m.message_id,
                       COALESCE(v.classification, 6) AS classification,
                       COALESCE(v.classification IN (0, 2, 4), false) AS is_spam,
                       v.source, v.detected_at, v.id AS verdict_id
                FROM messages m
                LEFT JOIN LATERAL (
                    SELECT d.classification, d.source, d.detected_at, d.id
                    FROM detection_results d
                    WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id
                      AND d.source <> 1
                      AND d.classification IS NOT NULL
                    ORDER BY d.detected_at DESC, d.id DESC
                    LIMIT 1) v ON true;
                """);

            migrationBuilder.Sql("""
                CREATE VIEW enriched_detections AS
                SELECT
                    -- Detection columns
                    dr.id,
                    dr.message_id,
                    dr.detected_at,
                    dr.source,
                    dr.detection_method,
                    dr.classification,
                    (dr.classification IN (0, 2, 4)) AS is_spam,
                    dr.score,
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
                """);

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
