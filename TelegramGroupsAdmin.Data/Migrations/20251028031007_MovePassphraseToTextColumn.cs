using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class MovePassphraseToTextColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new column
            migrationBuilder.AddColumn<string>(
                name: "passphrase_encrypted",
                table: "configs",
                type: "text",
                nullable: true);

            // Step 2: Migrate existing passphrases from JSONB to TEXT column
            // Extract PassphraseEncrypted from backup_encryption_config JSONB and copy to new column
            // Note: JSON property is PascalCase because .NET serializes with default naming
            migrationBuilder.Sql(@"
                UPDATE configs
                SET passphrase_encrypted = backup_encryption_config->>'PassphraseEncrypted'
                WHERE backup_encryption_config IS NOT NULL
                  AND backup_encryption_config->>'PassphraseEncrypted' IS NOT NULL
                  AND chat_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "passphrase_encrypted",
                table: "configs");
        }
    }
}
