using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemapAIFeatureConfigKeysToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET ai_provider_config = jsonb_set(
                    ai_provider_config,
                    '{features}',
                    (
                        SELECT COALESCE(jsonb_object_agg(
                            CASE elem.key
                                WHEN 'SpamDetection' THEN '0'
                                WHEN 'Translation'   THEN '1'
                                WHEN 'ImageAnalysis' THEN '2'
                                WHEN 'VideoAnalysis' THEN '3'
                                WHEN 'PromptBuilder' THEN '4'
                                WHEN 'ProfileScan'   THEN '5'
                                ELSE elem.key
                            END, elem.value), '{}'::jsonb)
                        FROM jsonb_each(ai_provider_config -> 'features') AS elem
                    ))
                WHERE ai_provider_config IS NOT NULL
                  AND ai_provider_config ? 'features';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET ai_provider_config = jsonb_set(
                    ai_provider_config,
                    '{features}',
                    (
                        SELECT COALESCE(jsonb_object_agg(
                            CASE elem.key
                                WHEN '0' THEN 'SpamDetection'
                                WHEN '1' THEN 'Translation'
                                WHEN '2' THEN 'ImageAnalysis'
                                WHEN '3' THEN 'VideoAnalysis'
                                WHEN '4' THEN 'PromptBuilder'
                                WHEN '5' THEN 'ProfileScan'
                                ELSE elem.key
                            END, elem.value), '{}'::jsonb)
                        FROM jsonb_each(ai_provider_config -> 'features') AS elem
                    ))
                WHERE ai_provider_config IS NOT NULL
                  AND ai_provider_config ? 'features';
                """);
        }
    }
}
