using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Migration: Rework notification_preferences schema from multiple columns to Channel×Event matrix.
    /// Old schema: telegram_dm_enabled, email_enabled, channel_configs, event_filters, protected_secrets
    /// New schema: config (JSONB containing channel array with enabled events per channel)
    /// </summary>
    public partial class ReworkNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add new config column with default empty channels array
            migrationBuilder.AddColumn<string>(
                name: "config",
                table: "notification_preferences",
                type: "jsonb",
                nullable: false,
                defaultValue: """{"channels":[]}""");

            // 2. Migrate existing data: convert old columns to new Channel×Event matrix format
            // NotificationChannel: TelegramDm=0, Email=1, WebPush=2
            // NotificationEventType: SpamDetected=0, SpamAutoDeleted=1, UserBanned=2, MessageReported=3,
            //                        MalwareDetected=4, ChatAdminChanged=5, ChatHealthWarning=6, BackupFailed=7
            migrationBuilder.Sql("""
                UPDATE notification_preferences SET config = jsonb_build_object(
                  'channels', jsonb_build_array(
                    -- Telegram DM channel (channel=0)
                    jsonb_build_object(
                      'channel', 0,
                      'enabledEvents', CASE WHEN telegram_dm_enabled THEN COALESCE((
                        SELECT jsonb_agg(
                          CASE key
                            WHEN 'spam_detected' THEN 0
                            WHEN 'spam_auto_deleted' THEN 1
                            WHEN 'user_banned' THEN 2
                            WHEN 'message_reported' THEN 3
                            WHEN 'malware_detected' THEN 4
                            WHEN 'chat_admin_changed' THEN 5
                            WHEN 'chat_health_warning' THEN 6
                            WHEN 'backup_failed' THEN 7
                          END
                        ) FROM jsonb_each_text(event_filters) WHERE value = 'true'
                      ), '[]'::jsonb) ELSE '[]'::jsonb END,
                      'digestMinutes', 0
                    ),
                    -- Email channel (channel=1)
                    jsonb_build_object(
                      'channel', 1,
                      'enabledEvents', CASE WHEN email_enabled THEN COALESCE((
                        SELECT jsonb_agg(
                          CASE key
                            WHEN 'spam_detected' THEN 0
                            WHEN 'spam_auto_deleted' THEN 1
                            WHEN 'user_banned' THEN 2
                            WHEN 'message_reported' THEN 3
                            WHEN 'malware_detected' THEN 4
                            WHEN 'chat_admin_changed' THEN 5
                            WHEN 'chat_health_warning' THEN 6
                            WHEN 'backup_failed' THEN 7
                          END
                        ) FROM jsonb_each_text(event_filters) WHERE value = 'true'
                      ), '[]'::jsonb) ELSE '[]'::jsonb END,
                      'digestMinutes', COALESCE((channel_configs->'email'->>'digestMinutes')::int, 0)
                    ),
                    -- WebPush channel (channel=2) - new, starts empty
                    jsonb_build_object(
                      'channel', 2,
                      'enabledEvents', '[]'::jsonb,
                      'digestMinutes', 0
                    )
                  )
                );
                """);

            // 3. Drop old columns (no longer needed after data migration)
            migrationBuilder.DropColumn(
                name: "telegram_dm_enabled",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "email_enabled",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "channel_configs",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "event_filters",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "protected_secrets",
                table: "notification_preferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down migration loses event-channel specific configuration
            // All events will be enabled for both channels if they were enabled for any channel

            // 1. Add back old columns
            migrationBuilder.AddColumn<bool>(
                name: "telegram_dm_enabled",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "email_enabled",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "channel_configs",
                table: "notification_preferences",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "event_filters",
                table: "notification_preferences",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "protected_secrets",
                table: "notification_preferences",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            // 2. Migrate data back (best effort - loses per-channel specificity)
            migrationBuilder.Sql("""
                UPDATE notification_preferences SET
                  telegram_dm_enabled = (
                    SELECT jsonb_array_length(c->'enabledEvents') > 0
                    FROM jsonb_array_elements(config->'channels') c
                    WHERE (c->>'channel')::int = 0
                  ),
                  email_enabled = (
                    SELECT jsonb_array_length(c->'enabledEvents') > 0
                    FROM jsonb_array_elements(config->'channels') c
                    WHERE (c->>'channel')::int = 1
                  ),
                  channel_configs = jsonb_build_object(
                    'email', jsonb_build_object(
                      'digestMinutes', COALESCE((
                        SELECT (c->>'digestMinutes')::int
                        FROM jsonb_array_elements(config->'channels') c
                        WHERE (c->>'channel')::int = 1
                      ), 0)
                    )
                  ),
                  event_filters = (
                    SELECT jsonb_object_agg(
                      CASE e
                        WHEN 0 THEN 'spam_detected'
                        WHEN 1 THEN 'spam_auto_deleted'
                        WHEN 2 THEN 'user_banned'
                        WHEN 3 THEN 'message_reported'
                        WHEN 4 THEN 'malware_detected'
                        WHEN 5 THEN 'chat_admin_changed'
                        WHEN 6 THEN 'chat_health_warning'
                        WHEN 7 THEN 'backup_failed'
                      END,
                      'true'
                    )
                    FROM (
                      SELECT DISTINCT e::int AS e
                      FROM jsonb_array_elements(config->'channels') c,
                           jsonb_array_elements(c->'enabledEvents') e
                    ) events
                  );
                """);

            // 3. Drop config column
            migrationBuilder.DropColumn(
                name: "config",
                table: "notification_preferences");
        }
    }
}
