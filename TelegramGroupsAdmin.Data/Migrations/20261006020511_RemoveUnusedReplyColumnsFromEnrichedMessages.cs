using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedReplyColumnsFromEnrichedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing reads the reply-to user's is_bot / latest_scan_explicit: recreate the view without them.
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(EnrichedMessageView.CreateViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
            migrationBuilder.Sql(LegacyEnrichedViewSql.EnrichedMessagesV3);
        }
    }
}
