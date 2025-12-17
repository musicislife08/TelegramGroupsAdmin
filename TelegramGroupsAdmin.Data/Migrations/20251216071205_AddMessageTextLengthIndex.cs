using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTextLengthIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expression index for efficient ORDER BY LENGTH(message_text)
            // Enables fast sorting for ML training data selection (implicit ham samples)
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_messages_text_length
                ON messages ((LENGTH(message_text)))
                WHERE message_text IS NOT NULL AND LENGTH(message_text) > 10;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_messages_text_length;");
        }
    }
}
