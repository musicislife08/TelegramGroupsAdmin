using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // STEP 1: Migrate always_run values from content_check_configs into content_detection_configs
            // This must happen BEFORE dropping the table.
            // The SQL updates each sub-config's alwaysRun property in the JSONB column.
            migrationBuilder.Sql(@"
                -- For each check in content_check_configs with always_run=true,
                -- update the corresponding sub-config in content_detection_configs
                UPDATE content_detection_configs cdc
                SET config_json = jsonb_set(
                    COALESCE(config_json::jsonb, '{}'),
                    ARRAY[
                        CASE ccc.check_name
                            WHEN 'StopWords' THEN 'stopWords'
                            WHEN 'Similarity' THEN 'similarity'
                            WHEN 'Cas' THEN 'cas'
                            WHEN 'CAS' THEN 'cas'
                            WHEN 'Bayes' THEN 'bayes'
                            WHEN 'InvisibleChars' THEN 'invisibleChars'
                            WHEN 'Translation' THEN 'translation'
                            WHEN 'MultiLanguage' THEN 'translation'
                            WHEN 'Spacing' THEN 'spacing'
                            WHEN 'AIVeto' THEN 'aiVeto'
                            WHEN 'OpenAI' THEN 'aiVeto'
                            WHEN 'UrlBlocklist' THEN 'urlBlocklist'
                            WHEN 'URLCheck' THEN 'urlBlocklist'
                            WHEN 'ThreatIntel' THEN 'threatIntel'
                            WHEN 'VirusTotal' THEN 'threatIntel'
                            WHEN 'FileScanning' THEN 'fileScanning'
                            WHEN 'SeoScraping' THEN 'seoScraping'
                            WHEN 'ImageSpam' THEN 'imageSpam'
                            WHEN 'VideoSpam' THEN 'videoSpam'
                            ELSE lower(ccc.check_name)
                        END,
                        'alwaysRun'
                    ],
                    'true'::jsonb
                )
                FROM content_check_configs ccc
                WHERE cdc.chat_id = ccc.chat_id
                  AND ccc.always_run = true
                  AND ccc.enabled = true;
            ");

            // STEP 2: Now safe to drop the old table
            migrationBuilder.DropTable(
                name: "content_check_configs");

            migrationBuilder.DropColumn(
                name: "spam_detection_config",
                table: "configs");

            // STEP 3: Convert config_json from TEXT to JSONB with explicit cast
            // (EF Core's AlterColumn doesn't include the USING clause needed by PostgreSQL)
            migrationBuilder.Sql(@"
                ALTER TABLE content_detection_configs
                ALTER COLUMN config_json TYPE jsonb USING config_json::jsonb;
            ");

            migrationBuilder.CreateIndex(
                name: "idx_content_detection_configs_chat",
                table: "content_detection_configs",
                column: "chat_id",
                unique: true,
                filter: "chat_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_content_detection_configs_chat",
                table: "content_detection_configs");

            // Convert back from JSONB to TEXT with explicit cast
            migrationBuilder.Sql(@"
                ALTER TABLE content_detection_configs
                ALTER COLUMN config_json TYPE text USING config_json::text;

                ALTER TABLE content_detection_configs
                ALTER COLUMN config_json SET NOT NULL;

                ALTER TABLE content_detection_configs
                ALTER COLUMN config_json SET DEFAULT '';
            ");

            migrationBuilder.AddColumn<string>(
                name: "spam_detection_config",
                table: "configs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "content_check_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    always_run = table.Column<bool>(type: "boolean", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    check_name = table.Column<string>(type: "text", nullable: false),
                    confidence_threshold = table.Column<int>(type: "integer", nullable: true),
                    configuration_json = table.Column<string>(type: "text", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    modified_by = table.Column<string>(type: "text", nullable: true),
                    modified_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_check_configs", x => x.id);
                });
        }
    }
}
