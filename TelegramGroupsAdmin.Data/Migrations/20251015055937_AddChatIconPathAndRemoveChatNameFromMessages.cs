using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatIconPathAndRemoveChatNameFromMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chat_name",
                table: "messages");

            migrationBuilder.AddColumn<string>(
                name: "chat_icon_path",
                table: "managed_chats",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chat_icon_path",
                table: "managed_chats");

            migrationBuilder.AddColumn<string>(
                name: "chat_name",
                table: "messages",
                type: "text",
                nullable: true);
        }
    }
}
