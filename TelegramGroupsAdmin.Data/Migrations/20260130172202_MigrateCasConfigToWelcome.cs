using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Migrates CAS config from content_detection_configs to Welcome config in configs table.
    /// CAS is a join-time security check, not a content detection algorithm, so it now lives
    /// under WelcomeConfig.JoinSecurity.Cas.
    /// </summary>
    public partial class MigrateCasConfigToWelcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The configs table uses a wide-table pattern with separate JSONB columns per config type.
            // Welcome config is stored in the 'welcome_config' column, not a 'config_type' discriminator.

            // Step 1: For chats that have CAS config but no configs row, insert a new row
            migrationBuilder.Sql(@"
                INSERT INTO configs (chat_id, welcome_config, created_at, updated_at)
                SELECT
                    cdc.chat_id,
                    jsonb_build_object('joinSecurity', jsonb_build_object('cas', cdc.config_json->'cas')),
                    NOW(),
                    NOW()
                FROM content_detection_configs cdc
                WHERE cdc.config_json ? 'cas'
                  AND NOT EXISTS (
                      SELECT 1 FROM configs c
                      WHERE c.chat_id = cdc.chat_id
                  )
                ON CONFLICT DO NOTHING;
            ");

            // Step 2: For existing configs rows, merge CAS into welcome_config.joinSecurity
            migrationBuilder.Sql(@"
                UPDATE configs c
                SET welcome_config = COALESCE(c.welcome_config, '{}'::jsonb) ||
                    jsonb_build_object('joinSecurity',
                        COALESCE(c.welcome_config->'joinSecurity', '{}'::jsonb) ||
                        jsonb_build_object('cas', cdc.config_json->'cas')
                    ),
                    updated_at = NOW()
                FROM content_detection_configs cdc
                WHERE c.chat_id = cdc.chat_id
                  AND cdc.config_json ? 'cas';
            ");

            // Step 3: Remove the 'cas' key from content_detection_configs
            migrationBuilder.Sql(@"
                UPDATE content_detection_configs
                SET config_json = config_json - 'cas',
                    last_updated = NOW()
                WHERE config_json ? 'cas';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse migration: Move CAS config back to content_detection_configs
            // Note: This is a best-effort reversal - some data precision may be lost
            migrationBuilder.Sql(@"
                -- Move CAS config back to content_detection_configs
                UPDATE content_detection_configs cdc
                SET config_json = cdc.config_json ||
                    jsonb_build_object('cas', c.welcome_config->'joinSecurity'->'cas'),
                    last_updated = NOW()
                FROM configs c
                WHERE c.chat_id = cdc.chat_id
                  AND c.welcome_config->'joinSecurity'->'cas' IS NOT NULL;

                -- Remove joinSecurity.cas from welcome_config
                UPDATE configs
                SET welcome_config = jsonb_set(
                    welcome_config,
                    '{joinSecurity}',
                    (welcome_config->'joinSecurity') - 'cas'
                ),
                updated_at = NOW()
                WHERE welcome_config->'joinSecurity'->'cas' IS NOT NULL;
            ");
        }
    }
}
