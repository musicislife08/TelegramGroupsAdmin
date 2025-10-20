using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileScanningConfigColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "file_scanning_config",
                table: "configs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "file_scanning_config",
                table: "configs");
        }
    }
}
