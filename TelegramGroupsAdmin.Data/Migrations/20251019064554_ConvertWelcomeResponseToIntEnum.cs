using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertWelcomeResponseToIntEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert column from varchar(20) to integer with explicit data conversion
            // pending → 0, accepted → 1, denied → 2, timeout → 3, left → 4
            migrationBuilder.Sql(@"
                ALTER TABLE welcome_responses
                ALTER COLUMN response TYPE integer
                USING (
                    CASE response
                        WHEN 'pending' THEN 0
                        WHEN 'accepted' THEN 1
                        WHEN 'denied' THEN 2
                        WHEN 'timeout' THEN 3
                        WHEN 'left' THEN 4
                        ELSE 0
                    END
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Convert column back from integer to varchar(20) with explicit data conversion
            // 0 → pending, 1 → accepted, 2 → denied, 3 → timeout, 4 → left
            migrationBuilder.Sql(@"
                ALTER TABLE welcome_responses
                ALTER COLUMN response TYPE character varying(20)
                USING (
                    CASE response
                        WHEN 0 THEN 'pending'
                        WHEN 1 THEN 'accepted'
                        WHEN 2 THEN 'denied'
                        WHEN 3 THEN 'timeout'
                        WHEN 4 THEN 'left'
                        ELSE 'pending'
                    END
                );
            ");
        }
    }
}
