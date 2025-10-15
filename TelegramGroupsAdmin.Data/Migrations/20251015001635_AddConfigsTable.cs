using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigsTable : Migration
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
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configs_chat_id",
                table: "configs",
                column: "chat_id",
                unique: true);

            // Migrate existing spam_detection_configs data to new unified configs table
            migrationBuilder.Sql(@"
                INSERT INTO configs (chat_id, spam_detection_config, created_at, updated_at)
                SELECT
                    CASE
                        WHEN chat_id = '0' THEN NULL  -- Convert '0' to NULL for global config
                        ELSE chat_id::bigint          -- Convert text to bigint for chat-specific
                    END as chat_id,
                    config_json::jsonb as spam_detection_config,  -- Cast text to jsonb
                    to_timestamp(last_updated) as created_at,
                    to_timestamp(last_updated) as updated_at
                FROM spam_detection_configs
                WHERE EXISTS (SELECT 1 FROM spam_detection_configs)  -- Only if table exists
                ON CONFLICT (chat_id) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configs");
        }
    }
}
