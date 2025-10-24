using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_translations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    edit_id = table.Column<long>(type: "bigint", nullable: true),
                    translated_text = table.Column<string>(type: "text", nullable: false),
                    detected_language = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    translated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_translations", x => x.id);
                    table.CheckConstraint("CK_message_translations_exclusive_source", "(message_id IS NOT NULL)::int + (edit_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_message_translations_message_edits_edit_id",
                        column: x => x.edit_id,
                        principalTable: "message_edits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_message_translations_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_detected_language",
                table: "message_translations",
                column: "detected_language");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_edit_id",
                table: "message_translations",
                column: "edit_id",
                filter: "edit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_translations_message_id",
                table: "message_translations",
                column: "message_id",
                filter: "message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_translations");
        }
    }
}
