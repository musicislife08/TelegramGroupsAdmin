using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupLegacyConfigColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop legacy chat_prompts table (superseded by prompt_versions)
            migrationBuilder.DropTable(
                name: "chat_prompts");

            // Drop legacy openai_config column (superseded by ai_provider_config)
            migrationBuilder.DropColumn(
                name: "openai_config",
                table: "configs");

            // Add fileScanning sub-config to existing content_detection_configs
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = config_json || '{"fileScanning": {"enabled": true, "alwaysRun": true}}'::jsonb
                WHERE config_json->>'fileScanning' IS NULL;
                """);

            // Clean up legacy AIVeto fields (vetoMode, vetoThreshold, systemPrompt)
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = config_json #- '{openAI,vetoMode}'
                                              #- '{openAI,vetoThreshold}'
                                              #- '{openAI,systemPrompt}'
                WHERE config_json->'openAI' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "openai_config",
                table: "configs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_prompts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    added_by = table.Column<string>(type: "text", nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    custom_prompt = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_prompts", x => x.id);
                });
        }
    }
}
