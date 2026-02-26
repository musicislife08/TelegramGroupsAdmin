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
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);
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

            // Recreate views after reverting column types
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
            migrationBuilder.Sql(EnrichedDetectionView.CreateViewSql);
            migrationBuilder.Sql(EnrichedReportView.CreateViewSql);
            migrationBuilder.Sql(DetectionAccuracyView.CreateViewSql);
            migrationBuilder.Sql(HourlyDetectionStatsView.CreateViewSql);
        }
    }
}
