using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSimilarityHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "similarity_hash",
                table: "messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "similarity_hash",
                table: "message_translations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_similarity_hash",
                table: "messages",
                column: "similarity_hash",
                filter: "similarity_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_message_translations_similarity_hash",
                table: "message_translations",
                column: "similarity_hash",
                filter: "similarity_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_similarity_hash",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_message_translations_similarity_hash",
                table: "message_translations");

            migrationBuilder.DropColumn(
                name: "similarity_hash",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "similarity_hash",
                table: "message_translations");
        }
    }
}
