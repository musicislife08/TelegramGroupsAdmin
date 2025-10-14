using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedConfigsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    spam_detection_config = table.Column<string>(type: "jsonb", nullable: true),
                    welcome_config = table.Column<string>(type: "jsonb", nullable: true),
                    log_config = table.Column<string>(type: "jsonb", nullable: true),
                    moderation_config = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configs", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "configs",
                columns: new[] { "id", "chat_id", "created_at", "log_config", "moderation_config", "spam_detection_config", "updated_at", "welcome_config" },
                values: new object[] { 1L, null, 1760484666L, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_configs_chat_id",
                table: "configs",
                column: "chat_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configs");
        }
    }
}
