using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDetectionResultsIndexesAndJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert TEXT to JSONB with explicit USING clause (required by PostgreSQL)
            migrationBuilder.Sql(@"
                ALTER TABLE detection_results
                ALTER COLUMN check_results_json TYPE jsonb USING check_results_json::jsonb;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_detection_source",
                table: "detection_results",
                column: "detection_source");

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_is_spam",
                table: "detection_results",
                column: "is_spam");

            migrationBuilder.CreateIndex(
                name: "ix_detection_results_is_spam_detected_at",
                table: "detection_results",
                columns: new[] { "is_spam", "detected_at" });

            // Add partial index for non-spam analytics (not supported by EF Core Fluent API)
            migrationBuilder.Sql(@"
                CREATE INDEX ix_detection_results_recent_non_spam
                ON detection_results (detected_at DESC)
                WHERE is_spam = false;
            ");

            // Add GIN index for JSONB queries (not supported by EF Core Fluent API)
            migrationBuilder.Sql(@"
                CREATE INDEX ix_detection_results_check_results_gin
                ON detection_results USING GIN (check_results_json);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_detection_results_detection_source",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_is_spam",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "ix_detection_results_is_spam_detected_at",
                table: "detection_results");

            // Drop manual indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_check_results_gin;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_detection_results_recent_non_spam;");

            // Convert JSONB back to TEXT (downgrade)
            migrationBuilder.Sql(@"
                ALTER TABLE detection_results
                ALTER COLUMN check_results_json TYPE text USING check_results_json::text;
            ");
        }
    }
}
