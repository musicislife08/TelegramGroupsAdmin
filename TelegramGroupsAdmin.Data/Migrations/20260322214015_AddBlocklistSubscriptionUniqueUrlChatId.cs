using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlocklistSubscriptionUniqueUrlChatId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove any existing duplicate (url, chat_id) rows before creating the unique index
            migrationBuilder.Sql("""
                DELETE FROM blocklist_subscriptions
                WHERE id NOT IN (
                    SELECT MAX(id) FROM blocklist_subscriptions GROUP BY url, chat_id
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_blocklist_subscriptions_url",
                table: "blocklist_subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_url_chat_id",
                table: "blocklist_subscriptions",
                columns: new[] { "url", "chat_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_blocklist_subscriptions_url_chat_id",
                table: "blocklist_subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_url",
                table: "blocklist_subscriptions",
                column: "url");
        }
    }
}
