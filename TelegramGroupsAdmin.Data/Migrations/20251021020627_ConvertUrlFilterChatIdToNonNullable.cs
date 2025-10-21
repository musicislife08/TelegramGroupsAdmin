using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertUrlFilterChatIdToNonNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IMPORTANT: Must handle duplicate prevention before altering columns
            // The unique constraint IX_cached_blocked_domains_domain_block_mode_chat_id
            // will reject multiple NULL→0 conversions for same domain+block_mode

            // Step 1: Drop the unique constraint temporarily
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_cached_blocked_domains_domain_block_mode_chat_id"";
            ");

            // Step 2: Delete ALL rows from cached_blocked_domains where chat_id IS NULL
            // This is safe - the cache is rebuilt automatically by BlocklistSyncService
            // Deleting is faster than deduplicating in a large table
            migrationBuilder.Sql("DELETE FROM cached_blocked_domains WHERE chat_id IS NULL;");

            // Step 3: Convert NULL → 0 for the two other tables (should be minimal rows)
            migrationBuilder.Sql("UPDATE domain_filters SET chat_id = 0 WHERE chat_id IS NULL;");
            migrationBuilder.Sql("UPDATE blocklist_subscriptions SET chat_id = 0 WHERE chat_id IS NULL;");

            // Step 4: Alter columns to NOT NULL with default 0
            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "domain_filters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "cached_blocked_domains",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "blocklist_subscriptions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            // Step 5: Recreate the unique constraint
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_cached_blocked_domains_domain_block_mode_chat_id""
                ON cached_blocked_domains (domain, block_mode, chat_id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "domain_filters",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "cached_blocked_domains",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "chat_id",
                table: "blocklist_subscriptions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
