using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoHashToBanCelebrationGifs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "photo_hash",
                table: "ban_celebration_gifs",
                type: "bytea",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ban_celebration_gifs_photo_hash",
                table: "ban_celebration_gifs",
                column: "photo_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ban_celebration_gifs_photo_hash",
                table: "ban_celebration_gifs");

            migrationBuilder.DropColumn(
                name: "photo_hash",
                table: "ban_celebration_gifs");
        }
    }
}
