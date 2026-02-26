using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMessageIdFromBigintToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Remap out-of-range message_id values (imported ML training data with
            // synthetic bigint IDs that don't fit in int). Build a temp mapping table,
            // disable FK triggers during remap, then re-enable.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _msg_id_remap AS
                SELECT message_id AS old_id,
                       (-ROW_NUMBER() OVER (ORDER BY message_id))::bigint AS new_id
                FROM messages
                WHERE message_id > 2147483647 OR message_id < -2147483648;

                -- Disable FK triggers for the remap (all tables referencing messages)
                ALTER TABLE detection_results DISABLE TRIGGER ALL;
                ALTER TABLE message_translations DISABLE TRIGGER ALL;
                ALTER TABLE training_labels DISABLE TRIGGER ALL;
                ALTER TABLE message_edits DISABLE TRIGGER ALL;
                ALTER TABLE user_actions DISABLE TRIGGER ALL;
                ALTER TABLE image_training_samples DISABLE TRIGGER ALL;
                ALTER TABLE video_training_samples DISABLE TRIGGER ALL;
                ALTER TABLE messages DISABLE TRIGGER ALL;

                -- Update parent table first
                UPDATE messages m
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE m.message_id = r.old_id;

                -- Also remap reply_to_message_id references
                UPDATE messages m
                SET reply_to_message_id = r.new_id
                FROM _msg_id_remap r
                WHERE m.reply_to_message_id = r.old_id;

                -- Update child tables
                UPDATE detection_results dr
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE dr.message_id = r.old_id;

                UPDATE message_translations mt
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE mt.message_id = r.old_id;

                UPDATE training_labels tl
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE tl.message_id = r.old_id;

                UPDATE message_edits me
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE me.message_id = r.old_id;

                UPDATE user_actions ua
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE ua.message_id = r.old_id;

                UPDATE image_training_samples its
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE its.message_id = r.old_id;

                UPDATE video_training_samples vts
                SET message_id = r.new_id
                FROM _msg_id_remap r
                WHERE vts.message_id = r.old_id;

                -- Re-enable FK triggers
                ALTER TABLE detection_results ENABLE TRIGGER ALL;
                ALTER TABLE message_translations ENABLE TRIGGER ALL;
                ALTER TABLE training_labels ENABLE TRIGGER ALL;
                ALTER TABLE message_edits ENABLE TRIGGER ALL;
                ALTER TABLE user_actions ENABLE TRIGGER ALL;
                ALTER TABLE image_training_samples ENABLE TRIGGER ALL;
                ALTER TABLE video_training_samples ENABLE TRIGGER ALL;
                ALTER TABLE messages ENABLE TRIGGER ALL;

                DROP TABLE _msg_id_remap;
                """);

            // Step 2: Drop views that reference message_id columns.
            // PostgreSQL cannot ALTER column type when views depend on it.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);

            // Step 3: ALTER columns from bigint to integer
            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "video_training_samples",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "user_actions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "training_labels",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "reply_to_message_id",
                table: "messages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "message_translations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "message_edits",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "image_training_samples",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "detection_results",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            // Step 4: Recreate views
            // IMPORTANT: EnrichedMessageView and EnrichedDetectionView use inline SQL here
            // (snapshot at this migration's point in time) because the C# constants were updated
            // for composite keys in AddCompositeMessageKey, which adds chat_id columns that
            // don't exist yet at this migration step.
            migrationBuilder.Sql("""
                CREATE VIEW enriched_messages AS
                SELECT
                    m.message_id, m.user_id, m.chat_id, m.timestamp, m.message_text,
                    m.photo_file_id, m.photo_file_size, m.urls, m.edit_date, m.content_hash,
                    m.photo_local_path, m.photo_thumbnail_path, m.deleted_at, m.deletion_source,
                    m.reply_to_message_id, m.media_type, m.media_file_id, m.media_file_size,
                    m.media_file_name, m.media_mime_type, m.media_local_path, m.media_duration,
                    m.content_check_skip_reason, m.similarity_hash,
                    c.chat_name, c.chat_icon_path,
                    u.username AS user_name, u.first_name, u.last_name, u.user_photo_path,
                    parent_user.first_name AS reply_to_first_name,
                    parent_user.last_name AS reply_to_last_name,
                    parent_user.username AS reply_to_username,
                    parent_user.telegram_user_id AS reply_to_user_id,
                    parent.message_text AS reply_to_text,
                    t.id AS translation_id, t.translated_text, t.detected_language,
                    t.confidence AS translation_confidence, t.translated_at
                FROM messages m
                LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
                LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
                LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id
                LEFT JOIN telegram_users parent_user ON parent.user_id = parent_user.telegram_user_id
                LEFT JOIN message_translations t ON m.message_id = t.message_id AND t.edit_id IS NULL;
                """);
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
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop views before reverting column types
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "video_training_samples",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "user_actions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "training_labels",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "reply_to_message_id",
                table: "messages",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "message_translations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "message_edits",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "image_training_samples",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "message_id",
                table: "detection_results",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            // Recreate views after reverting column types (inline SQL — see Up() comment)
            migrationBuilder.Sql("""
                CREATE VIEW enriched_messages AS
                SELECT
                    m.message_id, m.user_id, m.chat_id, m.timestamp, m.message_text,
                    m.photo_file_id, m.photo_file_size, m.urls, m.edit_date, m.content_hash,
                    m.photo_local_path, m.photo_thumbnail_path, m.deleted_at, m.deletion_source,
                    m.reply_to_message_id, m.media_type, m.media_file_id, m.media_file_size,
                    m.media_file_name, m.media_mime_type, m.media_local_path, m.media_duration,
                    m.content_check_skip_reason, m.similarity_hash,
                    c.chat_name, c.chat_icon_path,
                    u.username AS user_name, u.first_name, u.last_name, u.user_photo_path,
                    parent_user.first_name AS reply_to_first_name,
                    parent_user.last_name AS reply_to_last_name,
                    parent_user.username AS reply_to_username,
                    parent_user.telegram_user_id AS reply_to_user_id,
                    parent.message_text AS reply_to_text,
                    t.id AS translation_id, t.translated_text, t.detected_language,
                    t.confidence AS translation_confidence, t.translated_at
                FROM messages m
                LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
                LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
                LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id
                LEFT JOIN telegram_users parent_user ON parent.user_id = parent_user.telegram_user_id
                LEFT JOIN message_translations t ON m.message_id = t.message_id AND t.edit_id IS NULL;
                """);
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
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
        }
    }
}
