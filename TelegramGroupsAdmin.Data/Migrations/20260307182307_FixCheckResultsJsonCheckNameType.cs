using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <summary>
    /// Fixes a bug in the RemoveV1ContentDetectionBridge migration where the SQL used
    /// elem->>'CheckName' (text extraction via ->>) which converted CheckName from a
    /// JSON integer (5) to a JSON string ("5"). System.Text.Json cannot deserialize a
    /// string into the CheckName enum, causing CheckResultsSerializer.Deserialize() to
    /// silently return [] for all historical rows — breaking the analytics tables.
    /// </summary>
    public partial class FixCheckResultsJsonCheckNameType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE detection_results
                SET check_results_json = COALESCE(
                    (SELECT jsonb_build_object('Checks',
                        jsonb_agg(
                            jsonb_build_object(
                                'CheckName', (elem->>'CheckName')::int,
                                'Score', (elem->>'Score')::double precision,
                                'Abstained', (elem->>'Abstained')::boolean,
                                'Details', COALESCE(elem->>'Details', ''),
                                'ProcessingTimeMs', COALESCE((elem->>'ProcessingTimeMs')::double precision, 0)
                            )
                        )
                    )
                    FROM jsonb_array_elements(check_results_json->'Checks') AS elem),
                    check_results_json
                )
                WHERE check_results_json IS NOT NULL
                  AND check_results_json ? 'Checks'
                  AND jsonb_typeof(check_results_json->'Checks') = 'array'
                  AND check_results_json->'Checks' != '[]'::jsonb
                  AND jsonb_typeof(check_results_json->'Checks'->0->'CheckName') = 'string';
                """);
        }

        /// <summary>
        /// Down is intentionally empty — reverting CheckName back to string would
        /// re-break deserialization.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
