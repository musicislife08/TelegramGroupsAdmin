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
            // Intentionally empty. Rolling back would need to strip the 'outcome' key from JSONB context,
            // but by the time anyone rolls back, real pass records (outcome=1) may exist. Stripping the key
            // would destroy that data with no way to distinguish it from a pre-migration row. Leaving Down empty
            // prevents accidental data loss.
        }
    }
}
