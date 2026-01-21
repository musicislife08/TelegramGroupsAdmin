using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBanCelebrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ban_celebration_config",
                table: "configs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ban_celebration_captions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "text", nullable: false),
                    dm_text = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ban_celebration_captions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ban_celebration_gifs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ban_celebration_gifs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ban_celebration_captions_created_at",
                table: "ban_celebration_captions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ban_celebration_gifs_created_at",
                table: "ban_celebration_gifs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ban_celebration_captions");

            migrationBuilder.DropTable(
                name: "ban_celebration_gifs");

            migrationBuilder.DropColumn(
                name: "ban_celebration_config",
                table: "configs");
        }
    }
}
