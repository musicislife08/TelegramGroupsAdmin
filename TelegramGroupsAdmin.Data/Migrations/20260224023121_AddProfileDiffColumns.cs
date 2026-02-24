using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileDiffColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "personal_channel_photo_id",
                table: "telegram_users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pinned_story_ids",
                table: "telegram_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_photo_id",
                table: "telegram_users",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "personal_channel_photo_id",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "pinned_story_ids",
                table: "telegram_users");

            migrationBuilder.DropColumn(
                name: "profile_photo_id",
                table: "telegram_users");
        }
    }
}
