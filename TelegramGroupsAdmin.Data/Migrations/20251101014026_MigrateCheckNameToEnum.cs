using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateCheckNameToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert old JSON format to new proper format with PascalCase field names and enums as integers
            // Old: {"checks": [{"name": "StopWords", "result": "spam", "conf": 95, "reason": "..."}]}
            // New: {"Checks": [{"CheckName": 0, "Result": 1, "Confidence": 95, "Reason": "..."}]}
            //
            // Using a PL/pgSQL function to avoid Npgsql parsing issues with deeply nested REPLACE calls
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION migrate_check_results_json() RETURNS void AS $$
DECLARE
    check_name_map jsonb := '{
        ""StopWords"": 0, ""CAS"": 1, ""Similarity"": 2, ""Bayes"": 3,
        ""Spacing"": 4, ""InvisibleChars"": 5, ""OpenAI"": 6, ""ThreatIntel"": 7,
        ""UrlBlocklist"": 8, ""SeoScraping"": 9, ""ImageSpam"": 10, ""VideoSpam"": 11, ""FileScanning"": 12
    }'::jsonb;
    result_map jsonb := '{
        ""clean"": 0, ""spam"": 1, ""review"": 2, ""malware"": 3, ""hardblock"": 4
    }'::jsonb;
BEGIN
    UPDATE detection_results
    SET check_results_json = (
        SELECT jsonb_build_object(
            'Checks', jsonb_agg(
                jsonb_build_object(
                    'CheckName', COALESCE(
                        -- If name is already an integer, use it directly
                        CASE WHEN jsonb_typeof(elem->'name') = 'number'
                             THEN (elem->>'name')::int
                             ELSE NULL
                        END,
                        -- Otherwise look up string name in map
                        (check_name_map->>(elem->>'name'))::int
                    ),
                    'Result', COALESCE(
                        -- If result is already an integer, use it directly
                        CASE WHEN jsonb_typeof(elem->'result') = 'number'
                             THEN (elem->>'result')::int
                             ELSE NULL
                        END,
                        -- Otherwise look up string result in map
                        (result_map->>(elem->>'result'))::int
                    ),
                    'Confidence', (elem->>'conf')::int,
                    'Reason', elem->>'reason'
                )
            )
        )
        FROM jsonb_array_elements(check_results_json->'checks') AS elem
    )
    WHERE check_results_json IS NOT NULL;
END;
$$ LANGUAGE plpgsql;

SELECT migrate_check_results_json();
DROP FUNCTION migrate_check_results_json();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert new format back to old format
            // Using a PL/pgSQL function for robust JSON manipulation
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION revert_check_results_json() RETURNS void AS $$
DECLARE
    check_name_map text[] := ARRAY['StopWords', 'CAS', 'Similarity', 'Bayes', 'Spacing', 'InvisibleChars', 'OpenAI', 'ThreatIntel', 'UrlBlocklist', 'SeoScraping', 'ImageSpam', 'VideoSpam', 'FileScanning'];
    result_map text[] := ARRAY['clean', 'spam', 'review', 'malware', 'hardblock'];
BEGIN
    UPDATE detection_results
    SET check_results_json = (
        SELECT jsonb_build_object(
            'checks', jsonb_agg(
                jsonb_build_object(
                    'name', check_name_map[(elem->>'CheckName')::int + 1],
                    'result', result_map[(elem->>'Result')::int + 1],
                    'conf', (elem->>'Confidence')::int,
                    'reason', elem->>'Reason'
                )
            )
        )
        FROM jsonb_array_elements(check_results_json->'Checks') AS elem
    )
    WHERE check_results_json IS NOT NULL;
END;
$$ LANGUAGE plpgsql;

SELECT revert_check_results_json();
DROP FUNCTION revert_check_results_json();
");
        }
    }
}
