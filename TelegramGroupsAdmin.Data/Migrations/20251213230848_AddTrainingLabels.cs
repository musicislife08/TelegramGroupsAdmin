using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "training_labels",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    labeled_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    labeled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    audit_log_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_labels", x => x.message_id);
                    table.CheckConstraint("CK_training_labels_label", "label IN ('spam', 'ham')");
                    table.ForeignKey(
                        name: "FK_training_labels_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_training_labels_telegram_users_labeled_by_user_id",
                        column: x => x.labeled_by_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.SetNull);
                });

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

            // Data Migration: Migrate existing detection_results to training_labels

            // IMPORTANT: Ensure indexes exist for migration performance
            // These should already exist from previous migrations:
            // - detection_results(message_id, is_spam, used_for_training)
            // - training_labels(label, labeled_at) -- created above

            // Custom: Migrate manual spam labels
            migrationBuilder.Sql(@"
                INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at, reason)
                SELECT
                  dr.message_id,
                  'spam',
                  NULL,
                  dr.detected_at,
                  dr.reason
                FROM detection_results dr
                WHERE dr.detection_source = 'manual'
                  AND dr.is_spam = true
                  AND dr.used_for_training = true
                  AND NOT EXISTS (
                    SELECT 1 FROM training_labels tl WHERE tl.message_id = dr.message_id
                  );
            ");

            // Custom: Migrate ALL manual ham labels (corrections + direct labels)
            migrationBuilder.Sql(@"
                INSERT INTO training_labels (message_id, label, labeled_by_user_id, labeled_at, reason)
                SELECT
                  dr.message_id,
                  'ham',
                  NULL,
                  dr.detected_at,
                  dr.reason
                FROM detection_results dr
                WHERE dr.detection_source = 'manual'
                  AND dr.is_spam = false
                  AND dr.used_for_training = true
                  AND NOT EXISTS (
                    SELECT 1 FROM training_labels tl WHERE tl.message_id = dr.message_id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "training_labels");
        }
    }
}
