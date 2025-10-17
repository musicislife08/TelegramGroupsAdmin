using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertTagsToStringBased : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop tag_type index and column (tag system changes)
            migrationBuilder.DropIndex(
                name: "IX_user_tags_tag_type",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "tag_type",
                table: "user_tags");

            // Rename tag_label to removed_by_system_identifier for soft delete tracking
            migrationBuilder.RenameColumn(
                name: "tag_label",
                table: "user_tags",
                newName: "removed_by_system_identifier");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "user_tags",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "removed_by_telegram_user_id",
                table: "user_tags",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "removed_by_web_user_id",
                table: "user_tags",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tag_name",
                table: "user_tags",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "tag_definitions",
                columns: table => new
                {
                    tag_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_definitions", x => x.tag_name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_removed_at",
                table: "user_tags",
                column: "removed_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_tag_name",
                table: "user_tags",
                column: "tag_name");

            migrationBuilder.CreateIndex(
                name: "IX_tag_definitions_usage_count",
                table: "tag_definitions",
                column: "usage_count");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tag_definitions");

            migrationBuilder.DropIndex(
                name: "IX_user_tags_removed_at",
                table: "user_tags");

            migrationBuilder.DropIndex(
                name: "IX_user_tags_tag_name",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "removed_by_telegram_user_id",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "removed_by_web_user_id",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "tag_name",
                table: "user_tags");

            migrationBuilder.RenameColumn(
                name: "removed_by_system_identifier",
                table: "user_tags",
                newName: "tag_label");

            migrationBuilder.AddColumn<string>(
                name: "added_by",
                table: "user_tags",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "tag_type",
                table: "user_tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_tag_type",
                table: "user_tags",
                column: "tag_type");
        }
    }
}
