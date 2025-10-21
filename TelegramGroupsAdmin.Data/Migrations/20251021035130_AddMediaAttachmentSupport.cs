using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAttachmentSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "media_duration",
                table: "messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_file_id",
                table: "messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_file_name",
                table: "messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "media_file_size",
                table: "messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_local_path",
                table: "messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_mime_type",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "media_type",
                table: "messages",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "media_duration",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_file_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_file_name",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_file_size",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_local_path",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_mime_type",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_type",
                table: "messages");
        }
    }
}
