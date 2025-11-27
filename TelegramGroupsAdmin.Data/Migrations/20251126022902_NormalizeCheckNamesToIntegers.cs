using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCheckNamesToIntegers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert string CheckNames to integer enum values in check_results_json JSONB column
            // This normalizes old data (CheckName as string) to new format (CheckName as int)
            // After this migration, all CheckNames will be stored as integers matching the enum
            migrationBuilder.Sql(@"
                UPDATE detection_results
                SET check_results_json = (
                    SELECT jsonb_build_object(
                        'Checks',
                        jsonb_agg(
                            jsonb_build_object(
                                'CheckName',
                                CASE elem->>'CheckName'
                                    WHEN 'StopWords' THEN 0
                                    WHEN 'CAS' THEN 1
                                    WHEN 'Similarity' THEN 2
                                    WHEN 'Bayes' THEN 3
                                    WHEN 'Spacing' THEN 4
                                    WHEN 'InvisibleChars' THEN 5
                                    WHEN 'OpenAI' THEN 6
                                    WHEN 'ThreatIntel' THEN 7
                                    WHEN 'UrlBlocklist' THEN 8
                                    WHEN 'SeoScraping' THEN 9
                                    WHEN 'ImageSpam' THEN 10
                                    WHEN 'VideoSpam' THEN 11
                                    WHEN 'FileScanning' THEN 12
                                    ELSE (elem->>'CheckName')::int
                                END,
                                'Result', (elem->>'Result')::int,
                                'Confidence', (elem->>'Confidence')::int,
                                'Reason', elem->>'Reason',
                                'ProcessingTimeMs', COALESCE((elem->>'ProcessingTimeMs')::float, 0)
                            )
                        )
                    )
                    FROM jsonb_array_elements(check_results_json->'Checks') AS elem
                )
                WHERE check_results_json IS NOT NULL
                  AND check_results_json->'Checks' IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert integer CheckNames back to strings for rollback
            // Allows downgrade to previous version if needed
            migrationBuilder.Sql(@"
                UPDATE detection_results
                SET check_results_json = (
                    SELECT jsonb_build_object(
                        'Checks',
                        jsonb_agg(
                            jsonb_build_object(
                                'CheckName',
                                CASE (elem->>'CheckName')::text
                                    WHEN '0' THEN 'StopWords'
                                    WHEN '1' THEN 'CAS'
                                    WHEN '2' THEN 'Similarity'
                                    WHEN '3' THEN 'Bayes'
                                    WHEN '4' THEN 'Spacing'
                                    WHEN '5' THEN 'InvisibleChars'
                                    WHEN '6' THEN 'OpenAI'
                                    WHEN '7' THEN 'ThreatIntel'
                                    WHEN '8' THEN 'UrlBlocklist'
                                    WHEN '9' THEN 'SeoScraping'
                                    WHEN '10' THEN 'ImageSpam'
                                    WHEN '11' THEN 'VideoSpam'
                                    WHEN '12' THEN 'FileScanning'
                                    ELSE elem->>'CheckName'
                                END,
                                'Result', (elem->>'Result')::int,
                                'Confidence', (elem->>'Confidence')::int,
                                'Reason', elem->>'Reason',
                                'ProcessingTimeMs', COALESCE((elem->>'ProcessingTimeMs')::float, 0)
                            )
                        )
                    )
                    FROM jsonb_array_elements(check_results_json->'Checks') AS elem
                )
                WHERE check_results_json IS NOT NULL
                  AND check_results_json->'Checks' IS NOT NULL;
            ");
        }
    }
}
