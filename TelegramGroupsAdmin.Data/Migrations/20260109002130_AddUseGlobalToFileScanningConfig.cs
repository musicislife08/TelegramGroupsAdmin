using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUseGlobalToFileScanningConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add useGlobal property to existing fileScanning sub-configs
            // The previous migration added fileScanning without useGlobal
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(
                    config_json,
                    '{fileScanning}',
                    (config_json->'fileScanning') || '{"useGlobal": true}'::jsonb
                )
                WHERE config_json->'fileScanning' IS NOT NULL
                AND config_json->'fileScanning'->>'useGlobal' IS NULL;
                """);

            // Convert all JSON keys from camelCase to PascalCase to match C# property names
            // EF Core's ToJson() expects property names to match exactly
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pg_temp.to_pascal_case(input text) RETURNS text AS $$
                BEGIN
                    RETURN UPPER(LEFT(input, 1)) || SUBSTRING(input, 2);
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION pg_temp.transform_keys_to_pascal(input jsonb) RETURNS jsonb AS $$
                DECLARE
                    result jsonb;
                    key text;
                    value jsonb;
                BEGIN
                    IF jsonb_typeof(input) = 'object' THEN
                        result := '{}';
                        FOR key, value IN SELECT * FROM jsonb_each(input)
                        LOOP
                            result := result || jsonb_build_object(
                                pg_temp.to_pascal_case(key),
                                pg_temp.transform_keys_to_pascal(value)
                            );
                        END LOOP;
                        RETURN result;
                    ELSIF jsonb_typeof(input) = 'array' THEN
                        result := '[]';
                        FOR value IN SELECT * FROM jsonb_array_elements(input)
                        LOOP
                            result := result || jsonb_build_array(pg_temp.transform_keys_to_pascal(value));
                        END LOOP;
                        RETURN result;
                    ELSE
                        RETURN input;
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                UPDATE content_detection_configs
                SET config_json = pg_temp.transform_keys_to_pascal(config_json);

                -- Special case: rename OpenAI to AIVeto to match C# property name
                -- (the JsonPropertyName attribute mapped AIVeto -> openAI, now we're removing that)
                UPDATE content_detection_configs
                SET config_json = config_json - 'OpenAI' || jsonb_build_object('AIVeto', config_json->'OpenAI')
                WHERE config_json ? 'OpenAI';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove useGlobal property from fileScanning sub-configs
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(
                    config_json,
                    '{fileScanning}',
                    (config_json->'fileScanning') - 'useGlobal'
                )
                WHERE config_json->'fileScanning' IS NOT NULL;
                """);
        }
    }
}
