using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserNameAndPhotoFromMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "user_name",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "user_photo_path",
                table: "messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_name",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_photo_path",
                table: "messages",
                type: "text",
                nullable: true);
        }
    }
}
