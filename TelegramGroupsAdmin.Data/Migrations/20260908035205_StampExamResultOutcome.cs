using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class StampExamResultOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All exam rows created before ExamOutcome existed are failures (0).
            // Idempotent: only stamps rows missing the key.
            migrationBuilder.Sql(
                """
                UPDATE reports
                SET context = jsonb_set(context, '{outcome}', '0')
                WHERE type = 2
                  AND context IS NOT NULL
                  AND NOT (context ? 'outcome');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
