using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertChatIdToBigint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with USING clause for safe text-to-bigint conversion
            // This handles existing data by casting text values to bigint
            migrationBuilder.Sql(
                "ALTER TABLE spam_detection_configs ALTER COLUMN chat_id TYPE bigint USING chat_id::bigint;");

            migrationBuilder.Sql(
                "ALTER TABLE spam_check_configs ALTER COLUMN chat_id TYPE bigint USING chat_id::bigint;");

            migrationBuilder.Sql(
                "ALTER TABLE chat_prompts ALTER COLUMN chat_id TYPE bigint USING chat_id::bigint;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse conversion: bigint back to text
            migrationBuilder.Sql(
                "ALTER TABLE spam_detection_configs ALTER COLUMN chat_id TYPE text USING chat_id::text;");

            migrationBuilder.Sql(
                "ALTER TABLE spam_check_configs ALTER COLUMN chat_id TYPE text USING chat_id::text;");

            migrationBuilder.Sql(
                "ALTER TABLE chat_prompts ALTER COLUMN chat_id TYPE text USING chat_id::text;");
        }
    }
}
