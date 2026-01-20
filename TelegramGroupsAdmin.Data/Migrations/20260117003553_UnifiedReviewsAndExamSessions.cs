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
            // STEP 1: Add new columns to the unified reports table FIRST
            // (Must exist before we can migrate data into them)
            migrationBuilder.AddColumn<short>(
                name: "type",
                table: "reports",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0); // Default to Report type for existing rows

            migrationBuilder.AddColumn<string>(
                name: "context",
                table: "reports",
                type: "jsonb",
                nullable: true);

            // Create index on type column
            migrationBuilder.CreateIndex(
                name: "IX_reports_type",
                table: "reports",
                column: "type");

            // Update partial unique index to only apply to ContentReport type (type=0)
            // ExamFailures and ImpersonationAlerts don't have message IDs
            migrationBuilder.DropIndex(
                name: "IX_reports_unique_pending_per_message",
                table: "reports");

            migrationBuilder.CreateIndex(
                name: "IX_reports_unique_pending_per_message",
                table: "reports",
                columns: new[] { "message_id", "chat_id" },
                unique: true,
                filter: "status = 0 AND type = 0");

            // Add report_type column to report_callback_contexts
            migrationBuilder.AddColumn<short>(
                name: "report_type",
                table: "report_callback_contexts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            // STEP 2: Migrate existing impersonation_alerts data to unified reports table
            // This must happen BEFORE dropping the table to preserve data for existing deployments.
            // Maps: detected_at→reported_at, risk_level int→string, verdict int→string,
            //       all impersonation-specific fields→JSONB context
            migrationBuilder.Sql(@"
                INSERT INTO reports (type, message_id, chat_id, reported_at, status, reviewed_by, reviewed_at, web_user_id, context)
                SELECT
                    1 as type,  -- ReportType.ImpersonationAlert
                    0 as message_id,  -- Sentinel value (no message for impersonation alerts)
                    ia.chat_id,
                    ia.detected_at as reported_at,
                    CASE WHEN ia.reviewed_at IS NULL THEN 0 ELSE 1 END as status,
                    u.email as reviewed_by,
                    ia.reviewed_at,
                    ia.reviewed_by_user_id as web_user_id,
                    jsonb_build_object(
                        'suspectedUserId', ia.suspected_user_id,
                        'targetUserId', ia.target_user_id,
                        'totalScore', ia.total_score,
                        'riskLevel', CASE ia.risk_level
                            WHEN 0 THEN 'low'
                            WHEN 1 THEN 'medium'
                            WHEN 2 THEN 'high'
                            WHEN 3 THEN 'critical'
                            ELSE 'medium'
                        END,
                        'nameMatch', ia.name_match,
                        'photoMatch', ia.photo_match,
                        'photoSimilarity', ia.photo_similarity_score,
                        'autoBanned', ia.auto_banned,
                        'verdict', CASE ia.verdict
                            WHEN 0 THEN 'false_positive'
                            WHEN 1 THEN 'confirmed_scam'
                            WHEN 2 THEN 'whitelisted'
                            ELSE null
                        END
                    ) as context
                FROM impersonation_alerts ia
                LEFT JOIN users u ON u.id = ia.reviewed_by_user_id;
            ");

            // STEP 3: Now safe to drop the old table (data has been migrated)
            migrationBuilder.DropTable(
                name: "impersonation_alerts");

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

            // Restore original partial unique index (without type filter)
            migrationBuilder.DropIndex(
                name: "IX_reports_unique_pending_per_message",
                table: "reports");

            migrationBuilder.CreateIndex(
                name: "IX_reports_unique_pending_per_message",
                table: "reports",
                columns: new[] { "message_id", "chat_id" },
                unique: true,
                filter: "status = 0");

            // Remove added columns from reports table
            migrationBuilder.DropIndex(
                name: "IX_reports_type",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "type",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "context",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "report_type",
                table: "report_callback_contexts");

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
