using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropAddedByFromUserTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the legacy added_by column (replaced by actor system in Phase 4.19)
            migrationBuilder.DropColumn(
                name: "added_by",
                table: "user_tags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the added_by column if rolling back
            migrationBuilder.AddColumn<string>(
                name: "added_by",
                table: "user_tags",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "system");
        }
    }
}
