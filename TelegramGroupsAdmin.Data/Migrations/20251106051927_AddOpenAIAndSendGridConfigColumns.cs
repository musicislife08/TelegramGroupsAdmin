using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenAIAndSendGridConfigColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "openai_config",
                table: "configs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sendgrid_config",
                table: "configs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "openai_config",
                table: "configs");

            migrationBuilder.DropColumn(
                name: "sendgrid_config",
                table: "configs");
        }
    }
}
