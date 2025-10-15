using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveObsoleteTrainingSamplesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "training_samples");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "training_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    chat_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    confidence_when_added = table.Column<int>(type: "integer", nullable: true),
                    detection_count = table.Column<int>(type: "integer", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    last_detected_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    message_text = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_samples", x => x.id);
                });
        }
    }
}
