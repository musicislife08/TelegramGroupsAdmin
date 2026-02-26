using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeMessageKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Drop views that reference message columns.
            // PostgreSQL cannot ALTER PK/columns when views depend on them.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);

            // Step 2: Drop all 7 FKs referencing messages PK
            migrationBuilder.DropForeignKey(
                name: "FK_detection_results_messages_message_id",
                table: "detection_results");

            migrationBuilder.DropForeignKey(
                name: "FK_image_training_samples_messages_message_id",
                table: "image_training_samples");

            migrationBuilder.DropForeignKey(
                name: "FK_message_edits_messages_message_id",
                table: "message_edits");

            migrationBuilder.DropForeignKey(
                name: "FK_message_translations_messages_message_id",
                table: "message_translations");

            migrationBuilder.DropForeignKey(
                name: "FK_training_labels_messages_message_id",
                table: "training_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_user_actions_messages_message_id",
                table: "user_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_video_training_samples_messages_message_id",
                table: "video_training_samples");

            // Step 3: Drop indexes and PKs that will be recreated as composite
            migrationBuilder.DropIndex(
                name: "IX_video_training_samples_message_id",
                table: "video_training_samples");

            migrationBuilder.DropIndex(
                name: "IX_user_actions_message_id",
                table: "user_actions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_training_labels",
                table: "training_labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_messages",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_chat_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_message_translations_exclusive_source",
                table: "message_translations");

            migrationBuilder.DropIndex(
                name: "IX_message_edits_message_id",
                table: "message_edits");

            migrationBuilder.DropIndex(
                name: "IX_image_training_samples_message_id",
                table: "image_training_samples");

            // Step 4: Add chat_id columns to child tables
            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "video_training_samples",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "user_actions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "training_labels",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "message_translations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "message_edits",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "image_training_samples",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "chat_id",
                table: "detection_results",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Step 5: Backfill chat_id from parent messages table.
            // One-time data migration — after migration collapse on a fresh DB,
            // these are no-ops (child tables start with chat_id populated by app code).
            migrationBuilder.Sql("""
                UPDATE detection_results dr
                SET chat_id = m.chat_id
                FROM messages m
                WHERE dr.message_id = m.message_id AND dr.chat_id = 0;

                UPDATE message_edits me
                SET chat_id = m.chat_id
                FROM messages m
                WHERE me.message_id = m.message_id AND me.chat_id = 0;

                UPDATE training_labels tl
                SET chat_id = m.chat_id
                FROM messages m
                WHERE tl.message_id = m.message_id AND tl.chat_id = 0;

                UPDATE image_training_samples its
                SET chat_id = m.chat_id
                FROM messages m
                WHERE its.message_id = m.message_id AND its.chat_id = 0;

                UPDATE video_training_samples vts
                SET chat_id = m.chat_id
                FROM messages m
                WHERE vts.message_id = m.message_id AND vts.chat_id = 0;

                UPDATE user_actions ua
                SET chat_id = m.chat_id
                FROM messages m
                WHERE ua.message_id = m.message_id AND ua.chat_id IS NULL;

                UPDATE message_translations mt
                SET chat_id = m.chat_id
                FROM messages m
                WHERE mt.message_id = m.message_id AND mt.chat_id IS NULL;
                """);

            // Step 6: Drop identity on message_id (app always provides explicit IDs)
            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // PostgreSQL needs explicit identity drop (AlterColumn alone doesn't remove the sequence)
            migrationBuilder.Sql("ALTER TABLE messages ALTER COLUMN message_id DROP IDENTITY IF EXISTS;");

            // Step 7: Create composite PKs
            migrationBuilder.AddPrimaryKey(
                name: "PK_messages",
                table: "messages",
                columns: new[] { "message_id", "chat_id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_training_labels",
                table: "training_labels",
                columns: new[] { "message_id", "chat_id" });

            // Step 8: Create composite indexes
            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_message_id_chat_id",
                table: "video_training_samples",
                columns: new[] { "message_id", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_message_id_chat_id",
                table: "user_actions",
                columns: new[] { "message_id", "chat_id" });

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_message_id_chat_id",
                table: "message_translations",
                columns: new[] { "message_id", "chat_id" },
                unique: true,
                filter: "message_id IS NOT NULL AND chat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_edits_message_id_chat_id",
                table: "message_edits",
                columns: new[] { "message_id", "chat_id" });

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_message_id_chat_id",
                table: "image_training_samples",
                columns: new[] { "message_id", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_message_id_chat_id",
                table: "detection_results",
                columns: new[] { "message_id", "chat_id" });

            // Step 9: Add/update CHECK constraints
            migrationBuilder.AddCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions",
                sql: "(message_id IS NULL) = (chat_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_message_translations_exclusive_source",
                table: "message_translations",
                sql: "(message_id IS NOT NULL AND chat_id IS NOT NULL)::int + (edit_id IS NOT NULL)::int = 1");

            // Step 10: Recreate composite FKs
            migrationBuilder.AddForeignKey(
                name: "FK_detection_results_messages_message_id_chat_id",
                table: "detection_results",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_image_training_samples_messages_message_id_chat_id",
                table: "image_training_samples",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_message_edits_messages_message_id_chat_id",
                table: "message_edits",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_message_translations_messages_message_id_chat_id",
                table: "message_translations",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_training_labels_messages_message_id_chat_id",
                table: "training_labels",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_actions_messages_message_id_chat_id",
                table: "user_actions",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_video_training_samples_messages_message_id_chat_id",
                table: "video_training_samples",
                columns: new[] { "message_id", "chat_id" },
                principalTable: "messages",
                principalColumns: new[] { "message_id", "chat_id" },
                onDelete: ReferentialAction.Cascade);

            // Step 11: Recreate views with updated composite join SQL
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop views before reverting
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.DropViewSql);
            migrationBuilder.Sql(EnrichedReportView.DropViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.DropViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.DropViewSql);

            migrationBuilder.DropForeignKey(
                name: "FK_detection_results_messages_message_id_chat_id",
                table: "detection_results");

            migrationBuilder.DropForeignKey(
                name: "FK_image_training_samples_messages_message_id_chat_id",
                table: "image_training_samples");

            migrationBuilder.DropForeignKey(
                name: "FK_message_edits_messages_message_id_chat_id",
                table: "message_edits");

            migrationBuilder.DropForeignKey(
                name: "FK_message_translations_messages_message_id_chat_id",
                table: "message_translations");

            migrationBuilder.DropForeignKey(
                name: "FK_training_labels_messages_message_id_chat_id",
                table: "training_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_user_actions_messages_message_id_chat_id",
                table: "user_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_video_training_samples_messages_message_id_chat_id",
                table: "video_training_samples");

            migrationBuilder.DropIndex(
                name: "IX_video_training_samples_message_id_chat_id",
                table: "video_training_samples");

            migrationBuilder.DropIndex(
                name: "IX_user_actions_message_id_chat_id",
                table: "user_actions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_training_labels",
                table: "training_labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_messages",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_message_translations_message_id_chat_id",
                table: "message_translations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_message_translations_exclusive_source",
                table: "message_translations");

            migrationBuilder.DropIndex(
                name: "IX_message_edits_message_id_chat_id",
                table: "message_edits");

            migrationBuilder.DropIndex(
                name: "IX_image_training_samples_message_id_chat_id",
                table: "image_training_samples");

            migrationBuilder.DropIndex(
                name: "IX_detection_results_message_id_chat_id",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "video_training_samples");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "user_actions");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "training_labels");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "message_translations");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "message_edits");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "image_training_samples");

            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "detection_results");

            migrationBuilder.AlterColumn<int>(
                name: "message_id",
                table: "messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_training_labels",
                table: "training_labels",
                column: "message_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_messages",
                table: "messages",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_message_id",
                table: "video_training_samples",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_message_id",
                table: "user_actions",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_chat_id",
                table: "messages",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations",
                column: "message_id",
                unique: true,
                filter: "message_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_message_translations_exclusive_source",
                table: "message_translations",
                sql: "(message_id IS NOT NULL)::int + (edit_id IS NOT NULL)::int = 1");

            migrationBuilder.CreateIndex(
                name: "IX_message_edits_message_id",
                table: "message_edits",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_image_training_samples_message_id",
                table: "image_training_samples",
                column: "message_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_detection_results_messages_message_id",
                table: "detection_results",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_image_training_samples_messages_message_id",
                table: "image_training_samples",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_message_edits_messages_message_id",
                table: "message_edits",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_message_translations_messages_message_id",
                table: "message_translations",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_training_labels_messages_message_id",
                table: "training_labels",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_actions_messages_message_id",
                table: "user_actions",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_video_training_samples_messages_message_id",
                table: "video_training_samples",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "message_id",
                onDelete: ReferentialAction.Cascade);

            // Recreate views with old single-key join SQL
            // EnrichedMessageView and EnrichedDetectionView use inline SQL here because
            // the constants reference child table chat_id columns that no longer exist
            // after Down() drops them. The other 3 views don't reference child chat_id.
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
