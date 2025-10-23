using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    prompt_text = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    generation_metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_versions", x => x.id);
                });

            // Index for finding active prompt per chat (most common query)
            migrationBuilder.CreateIndex(
                name: "IX_prompt_versions_chat_id_is_active",
                table: "prompt_versions",
                columns: new[] { "chat_id", "is_active" });

            // Unique constraint: only one version number per chat
            migrationBuilder.CreateIndex(
                name: "IX_prompt_versions_chat_id_version",
                table: "prompt_versions",
                columns: new[] { "chat_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_prompt_versions_chat_id_version",
                table: "prompt_versions");

            migrationBuilder.DropIndex(
                name: "IX_prompt_versions_chat_id_is_active",
                table: "prompt_versions");

            migrationBuilder.DropTable(
                name: "prompt_versions");
        }
    }
}
