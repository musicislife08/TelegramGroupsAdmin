using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedReviewsAndExamSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old impersonation_alerts table (data stored in unified reviews via JSONB context)
            migrationBuilder.DropTable(
                name: "impersonation_alerts");

            // RENAME reports -> reviews (preserves existing data!)
            // EF Core generates DROP + CREATE which would lose data
            migrationBuilder.RenameTable(
                name: "reports",
                newName: "reviews");

            // Rename indexes to match new table name
            migrationBuilder.RenameIndex(
                name: "IX_reports_unique_pending_per_message",
                table: "reviews",
                newName: "IX_reviews_unique_pending_per_message");

            migrationBuilder.RenameIndex(
                name: "IX_reports_web_user_id",
                table: "reviews",
                newName: "IX_reviews_web_user_id");

            // Add new columns to the unified reviews table
            migrationBuilder.AddColumn<short>(
                name: "type",
                table: "reviews",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0); // Default to Report type for existing rows

            migrationBuilder.AddColumn<string>(
                name: "context",
                table: "reviews",
                type: "jsonb",
                nullable: true);

            // Create index on type column
            migrationBuilder.CreateIndex(
                name: "IX_reviews_type",
                table: "reviews",
                column: "type");

            // Add review_type column to report_callback_contexts
            migrationBuilder.AddColumn<short>(
                name: "review_type",
                table: "report_callback_contexts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            // Create the new exam_sessions table
            migrationBuilder.CreateTable(
                name: "exam_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    current_question_index = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    mc_answers = table.Column<string>(type: "jsonb", nullable: true),
                    shuffle_state = table.Column<string>(type: "jsonb", nullable: true),
                    open_ended_answer = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exam_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exam_sessions_chat_id_user_id",
                table: "exam_sessions",
                columns: new[] { "chat_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exam_sessions_expires_at",
                table: "exam_sessions",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_sessions");

            // Remove added columns
            migrationBuilder.DropIndex(
                name: "IX_reviews_type",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "type",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "context",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "review_type",
                table: "report_callback_contexts");

            // Rename back to reports
            migrationBuilder.RenameIndex(
                name: "IX_reviews_unique_pending_per_message",
                table: "reviews",
                newName: "IX_reports_unique_pending_per_message");

            migrationBuilder.RenameIndex(
                name: "IX_reviews_web_user_id",
                table: "reviews",
                newName: "IX_reports_web_user_id");

            migrationBuilder.RenameTable(
                name: "reviews",
                newName: "reports");

            // Recreate impersonation_alerts table
            migrationBuilder.CreateTable(
                name: "impersonation_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    suspected_user_id = table.Column<long>(type: "bigint", nullable: false),
                    target_user_id = table.Column<long>(type: "bigint", nullable: false),
                    auto_banned = table.Column<bool>(type: "boolean", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_similarity_score = table.Column<double>(type: "double precision", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    risk_level = table.Column<int>(type: "integer", nullable: false),
                    total_score = table.Column<int>(type: "integer", nullable: false),
                    verdict = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impersonation_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_suspected_user_id",
                        column: x => x.suspected_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_chat_id",
                table: "impersonation_alerts",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_reviewed_by_user_id",
                table: "impersonation_alerts",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_risk_level_detected_at",
                table: "impersonation_alerts",
                columns: new[] { "risk_level", "detected_at" },
                filter: "reviewed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_suspected_user_id",
                table: "impersonation_alerts",
                column: "suspected_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_target_user_id",
                table: "impersonation_alerts",
                column: "target_user_id");
        }
    }
}
