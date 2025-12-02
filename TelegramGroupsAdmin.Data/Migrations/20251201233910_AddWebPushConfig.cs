using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Adds dedicated Web Push configuration columns.
    /// - web_push_config: JSONB for non-secret settings (enabled, contact email, public key)
    /// - vapid_private_key_encrypted: Encrypted TEXT for the VAPID private key
    ///
    /// VAPID Key Migration Strategy:
    /// The existing VAPID keys in api_keys column are encrypted with "ApiKeys" purpose.
    /// We cannot migrate via SQL because the data needs to be:
    /// 1. Decrypted with "ApiKeys" purpose
    /// 2. Re-encrypted with "VapidPrivateKey" purpose for the private key
    /// 3. Stored in plain JSONB for the public key (it's not a secret)
    ///
    /// The VapidKeyMigrationService handles this at runtime on first startup.
    /// </summary>
    public partial class AddWebPushConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "vapid_private_key_encrypted",
                table: "configs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "web_push_config",
                table: "configs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vapid_private_key_encrypted",
                table: "configs");

            migrationBuilder.DropColumn(
                name: "web_push_config",
                table: "configs");
        }
    }
}
