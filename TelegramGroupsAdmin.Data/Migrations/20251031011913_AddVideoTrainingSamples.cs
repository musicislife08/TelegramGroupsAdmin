using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoTrainingSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "video_training_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    video_path = table.Column<string>(type: "text", nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    file_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    keyframe_hashes = table.Column<string>(type: "jsonb", nullable: false),
                    has_audio = table.Column<bool>(type: "boolean", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    marked_by_web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    marked_by_telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    marked_by_system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_training_samples", x => x.id);
                    table.CheckConstraint("CK_video_training_exclusive_actor", "(marked_by_web_user_id IS NOT NULL)::int + (marked_by_telegram_user_id IS NOT NULL)::int + (marked_by_system_identifier IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_video_training_samples_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "message_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_is_spam_marked_at",
                table: "video_training_samples",
                columns: new[] { "is_spam", "marked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_video_training_samples_message_id",
                table: "video_training_samples",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_training_samples");
        }
    }
}
