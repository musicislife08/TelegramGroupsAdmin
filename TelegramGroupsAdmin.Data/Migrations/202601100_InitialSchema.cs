using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Consolidated initial schema for TelegramGroupsAdmin
///
/// Tables:
/// 1. messages - Message history from Telegram
/// 2. message_edits - Edit audit trail
/// 3. detection_results - Spam/ham classifications
/// 4. user_actions - Moderation actions (ban, warn, mute, trust)
/// 5. managed_chats - Bot's chat membership tracking
/// 6. chat_admins - Per-chat admin caching
/// 7. users - Web app user accounts
/// 8. invites - Invite token system
/// 9. recovery_codes - TOTP recovery codes
/// 10. audit_log - Security audit trail
/// 11. verification_tokens - Email/password reset tokens
/// 12. telegram_user_mappings - Link Telegram accounts to web users
/// 13. telegram_link_tokens - One-time linking tokens
/// 14. stop_words - Keyword blocklist for spam detection
/// 15. chat_prompts - Custom OpenAI prompts per chat
/// 16. spam_detection_configs - Global spam detection config
/// 17. spam_check_configs - Per-check algorithm config
/// 18. reports - User-submitted message reports
/// </summary>
[Migration(202601100)]
public class InitialSchema : Migration
{
    public override void Up()
    {
        // ================================================================
        // MESSAGES & HISTORY
        // ================================================================

        Create.Table("messages")
            .WithColumn("message_id").AsInt64().PrimaryKey()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("user_name").AsString().Nullable()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("timestamp").AsInt64().NotNullable()
            .WithColumn("message_text").AsString().Nullable()
            .WithColumn("photo_file_id").AsString().Nullable()
            .WithColumn("photo_file_size").AsInt32().Nullable()
            .WithColumn("photo_local_path").AsString().Nullable()
            .WithColumn("photo_thumbnail_path").AsString().Nullable()
            .WithColumn("urls").AsString().Nullable()
            .WithColumn("content_hash").AsString(64).Nullable()
            .WithColumn("chat_name").AsString().Nullable()
            .WithColumn("edit_date").AsInt64().Nullable()
            .WithColumn("deleted_at").AsInt64().Nullable()
            .WithColumn("deletion_source").AsString(50).Nullable();

        Create.Index("idx_user_chat_timestamp")
            .OnTable("messages")
            .OnColumn("user_id").Ascending()
            .OnColumn("chat_id").Ascending()
            .OnColumn("timestamp").Descending();

        Create.Index("idx_content_hash")
            .OnTable("messages")
            .OnColumn("content_hash").Ascending();

        Create.Index("idx_chat_name")
            .OnTable("messages")
            .OnColumn("chat_name").Ascending();

        Create.Index("idx_messages_chat_id_timestamp")
            .OnTable("messages")
            .OnColumn("chat_id").Ascending()
            .OnColumn("timestamp").Descending();

        Create.Index("idx_messages_user_chat_time")
            .OnTable("messages")
            .OnColumn("user_id").Ascending()
            .OnColumn("chat_id").Ascending()
            .OnColumn("timestamp").Descending();

        Execute.Sql(@"
            CREATE INDEX idx_user_chat_photo
            ON messages (user_id, chat_id, photo_file_id)
            WHERE photo_file_id IS NOT NULL;

            CREATE INDEX idx_messages_deleted
            ON messages (chat_id, deleted_at)
            WHERE deleted_at IS NOT NULL;
        ");

        Create.Table("message_edits")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt64().NotNullable()
            .WithColumn("edit_date").AsInt64().NotNullable()
            .WithColumn("old_text").AsString().Nullable()
            .WithColumn("new_text").AsString().Nullable()
            .WithColumn("old_content_hash").AsString(64).Nullable()
            .WithColumn("new_content_hash").AsString(64).Nullable();

        Create.ForeignKey("FK_message_edits_message_id_messages_message_id")
            .FromTable("message_edits").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("idx_message_edits_msg")
            .OnTable("message_edits")
            .OnColumn("message_id").Ascending()
            .OnColumn("edit_date").Descending();

        // ================================================================
        // SPAM DETECTION
        // ================================================================

        Create.Table("detection_results")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt64().NotNullable()
            .WithColumn("detected_at").AsInt64().NotNullable()
            .WithColumn("detection_source").AsString(50).NotNullable()
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("confidence").AsInt32().Nullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("detection_method").AsString(100).Nullable()
            .WithColumn("added_by").AsString(36).Nullable();

        Create.ForeignKey("FK_detection_results_message_id_messages_message_id")
            .FromTable("detection_results").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("idx_detection_results_message_id")
            .OnTable("detection_results")
            .OnColumn("message_id").Ascending();

        Create.Index("idx_detection_results_detected_at")
            .OnTable("detection_results")
            .OnColumn("detected_at").Descending();

        Create.Index("idx_detection_results_is_spam_source")
            .OnTable("detection_results")
            .OnColumn("is_spam").Ascending()
            .OnColumn("detection_source").Ascending()
            .OnColumn("detected_at").Descending();

        Create.Table("stop_words")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("word").AsString(100).NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("added_by").AsString(36).Nullable()
            .WithColumn("notes").AsString(500).Nullable();

        Create.Index("IX_stop_words_word")
            .OnTable("stop_words")
            .OnColumn("word").Ascending()
            .WithOptions().Unique();

        Create.Index("idx_stop_words_enabled")
            .OnTable("stop_words")
            .OnColumn("enabled").Ascending();

        Execute.Sql(@"
            CREATE INDEX idx_enabled_stop_words_word
            ON stop_words (word)
            WHERE enabled = true;
        ");

        Create.Table("chat_prompts")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsString(50).NotNullable()
            .WithColumn("custom_prompt").AsString(2000).NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("added_by").AsString(36).Nullable()
            .WithColumn("notes").AsString(500).Nullable();

        Create.Index("idx_chat_prompts_chat_id")
            .OnTable("chat_prompts")
            .OnColumn("chat_id").Ascending();

        Execute.Sql(@"
            CREATE INDEX idx_group_prompts_enabled
            ON chat_prompts (enabled);
        ");

        Create.Table("spam_detection_configs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsString(50).Nullable()
            .WithColumn("config_json").AsString(4000).NotNullable()
            .WithColumn("last_updated").AsInt64().NotNullable()
            .WithColumn("updated_by").AsString(36).Nullable();

        Create.Index("idx_spam_detection_configs_chat_id")
            .OnTable("spam_detection_configs")
            .OnColumn("chat_id").Ascending();

        Create.UniqueConstraint("uc_spam_detection_configs_chat")
            .OnTable("spam_detection_configs")
            .Column("chat_id");

        Create.Table("spam_check_configs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsString(50).NotNullable()
            .WithColumn("check_name").AsString(50).NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("confidence_threshold").AsInt32().Nullable()
            .WithColumn("configuration_json").AsString(2000).Nullable()
            .WithColumn("modified_date").AsInt64().NotNullable()
            .WithColumn("modified_by").AsString(36).Nullable();

        Create.Index("idx_spam_check_configs_chat_id")
            .OnTable("spam_check_configs")
            .OnColumn("chat_id").Ascending();

        Create.Index("idx_spam_check_configs_check_name")
            .OnTable("spam_check_configs")
            .OnColumn("check_name").Ascending();

        Create.UniqueConstraint("uc_spam_check_configs_chat_check")
            .OnTable("spam_check_configs")
            .Columns("chat_id", "check_name");

        // ================================================================
        // USER ACTIONS & MODERATION
        // ================================================================

        Create.Table("user_actions")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("chat_ids").AsCustom("BIGINT[]").Nullable()
            .WithColumn("action_type").AsInt32().NotNullable()
            .WithColumn("message_id").AsInt64().Nullable()
            .WithColumn("issued_by").AsString(36).Nullable()
            .WithColumn("issued_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().Nullable()
            .WithColumn("reason").AsString().Nullable();

        Create.ForeignKey("FK_user_actions_message_id_messages_message_id")
            .FromTable("user_actions").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.Index("idx_user_actions_user_id")
            .OnTable("user_actions")
            .OnColumn("user_id").Ascending();

        Create.Index("idx_user_actions_issued_at")
            .OnTable("user_actions")
            .OnColumn("issued_at").Descending();

        Create.Index("idx_user_actions_action_type")
            .OnTable("user_actions")
            .OnColumn("action_type").Ascending();

        Create.Table("managed_chats")
            .WithColumn("chat_id").AsInt64().PrimaryKey()
            .WithColumn("chat_name").AsString().Nullable()
            .WithColumn("chat_type").AsInt32().NotNullable()
            .WithColumn("bot_status").AsInt32().NotNullable()
            .WithColumn("is_admin").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("added_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("last_seen_at").AsInt64().Nullable()
            .WithColumn("settings_json").AsString().Nullable();

        Create.Index("idx_managed_chats_active")
            .OnTable("managed_chats")
            .OnColumn("is_active").Ascending();

        Execute.Sql(@"
            CREATE INDEX idx_managed_chats_admin
            ON managed_chats (is_admin)
            WHERE is_active = true;
        ");

        Create.Table("chat_admins")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("telegram_id").AsInt64().NotNullable()
            .WithColumn("is_creator").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("promoted_at").AsInt64().NotNullable()
            .WithColumn("last_verified_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);

        // Full unique constraint for ON CONFLICT upserts
        Create.UniqueConstraint("uq_chat_admins_chat_telegram")
            .OnTable("chat_admins")
            .Columns("chat_id", "telegram_id");

        // Partial indexes for active admins only (performance optimization)
        Execute.Sql(@"
            CREATE INDEX idx_chat_admins_chat_id
            ON chat_admins (chat_id)
            WHERE is_active = true;

            CREATE INDEX idx_chat_admins_telegram_id
            ON chat_admins (telegram_id)
            WHERE is_active = true;
        ");

        Create.ForeignKey("fk_chat_admins_chat_id")
            .FromTable("chat_admins").ForeignColumn("chat_id")
            .ToTable("managed_chats").PrimaryColumn("chat_id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Table("reports")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt32().NotNullable()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("report_command_message_id").AsInt32().NotNullable()
            .WithColumn("reported_by_user_id").AsInt64().NotNullable()
            .WithColumn("reported_by_user_name").AsString(255).Nullable()
            .WithColumn("reported_at").AsInt64().NotNullable()
            .WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("reviewed_by").AsString(450).Nullable()
            .WithColumn("reviewed_at").AsInt64().Nullable()
            .WithColumn("action_taken").AsString(50).Nullable()
            .WithColumn("admin_notes").AsString(500).Nullable();

        Create.Index("idx_reports_chat_status")
            .OnTable("reports")
            .OnColumn("chat_id").Ascending()
            .OnColumn("status").Ascending();

        Create.Index("idx_reports_status")
            .OnTable("reports")
            .OnColumn("status").Ascending();

        Create.Index("idx_reports_reported_at")
            .OnTable("reports")
            .OnColumn("reported_at").Descending();

        // ================================================================
        // IDENTITY & AUTH
        // ================================================================

        Create.Table("users")
            .WithColumn("id").AsString(36).PrimaryKey()
            .WithColumn("email").AsString(256).NotNullable()
            .WithColumn("normalized_email").AsString(256).NotNullable()
            .WithColumn("password_hash").AsString(256).NotNullable()
            .WithColumn("security_stamp").AsString(36).NotNullable()
            .WithColumn("permission_level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("invited_by").AsString(36).Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("totp_secret").AsString(512).Nullable()
            .WithColumn("totp_enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("totp_setup_started_at").AsInt64().Nullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("last_login_at").AsInt64().Nullable()
            .WithColumn("status").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("modified_by").AsString(36).Nullable()
            .WithColumn("modified_at").AsInt64().Nullable()
            .WithColumn("email_verified").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("email_verification_token").AsString().Nullable()
            .WithColumn("email_verification_token_expires_at").AsInt64().Nullable()
            .WithColumn("password_reset_token").AsString().Nullable()
            .WithColumn("password_reset_token_expires_at").AsInt64().Nullable();

        Create.Index("IX_users_email")
            .OnTable("users")
            .OnColumn("email").Ascending()
            .WithOptions().Unique();

        Create.Index("idx_users_normalized_email")
            .OnTable("users")
            .OnColumn("normalized_email").Ascending();

        Create.Index("idx_users_permission_level")
            .OnTable("users")
            .OnColumn("permission_level").Ascending();

        Create.Index("idx_users_is_active")
            .OnTable("users")
            .OnColumn("is_active").Ascending();

        Create.Index("idx_users_status")
            .OnTable("users")
            .OnColumn("status").Ascending();

        Create.Index("idx_users_modified_at")
            .OnTable("users")
            .OnColumn("modified_at").Descending();

        Execute.Sql(@"
            CREATE INDEX idx_active_users_email
            ON users (email, normalized_email)
            WHERE status = 1;
        ");

        // Foreign keys for users table (self-referencing)
        Create.ForeignKey("fk_users_invited_by")
            .FromTable("users").ForeignColumn("invited_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("fk_users_modified_by")
            .FromTable("users").ForeignColumn("modified_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        // Foreign keys from spam detection tables
        Create.ForeignKey("FK_detection_results_added_by_users_id")
            .FromTable("detection_results").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("FK_user_actions_issued_by_users_id")
            .FromTable("user_actions").ForeignColumn("issued_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.Table("recovery_codes")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsString(36).NotNullable()
            .WithColumn("code_hash").AsString(256).NotNullable()
            .WithColumn("used_at").AsInt64().Nullable();

        Create.ForeignKey("FK_recovery_codes_user_id_users_id")
            .FromTable("recovery_codes").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("idx_recovery_codes_user")
            .OnTable("recovery_codes")
            .OnColumn("user_id").Ascending();

        Create.Table("invites")
            .WithColumn("token").AsString(36).PrimaryKey()
            .WithColumn("created_by").AsString(36).NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("used_by").AsString(36).Nullable()
            .WithColumn("permission_level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("modified_at").AsInt64().Nullable();

        Create.ForeignKey("FK_invites_created_by_users_id")
            .FromTable("invites").ForeignColumn("created_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("FK_invites_used_by_users_id")
            .FromTable("invites").ForeignColumn("used_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.Index("idx_invites_expires")
            .OnTable("invites")
            .OnColumn("expires_at").Ascending();

        Create.Index("idx_invites_created_by")
            .OnTable("invites")
            .OnColumn("created_by").Ascending();

        Create.Index("idx_invites_status")
            .OnTable("invites")
            .OnColumn("status").Ascending();

        Create.Index("idx_invites_creator_status")
            .OnTable("invites")
            .OnColumn("created_by").Ascending()
            .OnColumn("status").Ascending()
            .OnColumn("created_at").Descending();

        Execute.Sql(@"
            CREATE INDEX idx_pending_invites_expires
            ON invites (expires_at)
            WHERE status = 0;
        ");

        Create.Table("audit_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("event_type").AsInt32().NotNullable()
            .WithColumn("timestamp").AsInt64().NotNullable()
            .WithColumn("actor_user_id").AsString(36).Nullable()
            .WithColumn("target_user_id").AsString(36).Nullable()
            .WithColumn("value").AsString(500).Nullable();

        Create.Index("idx_audit_log_timestamp")
            .OnTable("audit_log")
            .OnColumn("timestamp").Descending();

        Create.Index("idx_audit_log_actor")
            .OnTable("audit_log")
            .OnColumn("actor_user_id").Ascending();

        Create.Index("idx_audit_log_target")
            .OnTable("audit_log")
            .OnColumn("target_user_id").Ascending();

        Create.Index("idx_audit_log_event_type")
            .OnTable("audit_log")
            .OnColumn("event_type").Ascending();

        Create.Index("idx_audit_log_target_event_time")
            .OnTable("audit_log")
            .OnColumn("target_user_id").Ascending()
            .OnColumn("event_type").Ascending()
            .OnColumn("timestamp").Descending();

        Create.ForeignKey("fk_audit_log_actor")
            .FromTable("audit_log").ForeignColumn("actor_user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("fk_audit_log_target")
            .FromTable("audit_log").ForeignColumn("target_user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.Table("verification_tokens")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsString().NotNullable()
            .WithColumn("token_type").AsString().NotNullable()
            .WithColumn("token").AsString().NotNullable()
            .WithColumn("value").AsString().Nullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("used_at").AsInt64().Nullable();

        Create.Index("IX_verification_tokens_token")
            .OnTable("verification_tokens")
            .OnColumn("token").Ascending()
            .WithOptions().Unique();

        Create.Index("idx_verification_tokens_user_id")
            .OnTable("verification_tokens")
            .OnColumn("user_id").Ascending();

        Create.Index("idx_verification_tokens_type")
            .OnTable("verification_tokens")
            .OnColumn("token_type").Ascending();

        Create.Index("idx_verification_tokens_user_type")
            .OnTable("verification_tokens")
            .OnColumn("user_id").Ascending()
            .OnColumn("token_type").Ascending()
            .OnColumn("expires_at").Descending();

        Execute.Sql(@"
            CREATE INDEX idx_valid_verification_tokens
            ON verification_tokens (token, token_type)
            WHERE used_at IS NULL;
        ");

        Create.ForeignKey("fk_verification_tokens_user")
            .FromTable("verification_tokens").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        // ================================================================
        // TELEGRAM INTEGRATION
        // ================================================================

        Create.Table("telegram_user_mappings")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("telegram_id").AsInt64().NotNullable()
            .WithColumn("telegram_username").AsString(256).Nullable()
            .WithColumn("user_id").AsString(36).NotNullable()
            .WithColumn("linked_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);

        Create.ForeignKey("fk_telegram_user_mappings_user_id")
            .FromTable("telegram_user_mappings").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("IX_telegram_user_mappings_telegram_id")
            .OnTable("telegram_user_mappings")
            .OnColumn("telegram_id").Ascending()
            .WithOptions().Unique();

        Create.Index("idx_telegram_user_mappings_user_id")
            .OnTable("telegram_user_mappings")
            .OnColumn("user_id").Ascending();

        Create.Table("telegram_link_tokens")
            .WithColumn("token").AsString(64).PrimaryKey()
            .WithColumn("user_id").AsString(36).NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("used_at").AsInt64().Nullable()
            .WithColumn("used_by_telegram_id").AsInt64().Nullable();

        Create.ForeignKey("fk_telegram_link_tokens_user_id")
            .FromTable("telegram_link_tokens").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("idx_telegram_link_tokens_user_id")
            .OnTable("telegram_link_tokens")
            .OnColumn("user_id").Ascending();

        Create.Index("idx_telegram_link_tokens_expires_at")
            .OnTable("telegram_link_tokens")
            .OnColumn("expires_at").Ascending();

        // ================================================================
        // FOREIGN KEYS (added after all tables exist)
        // ================================================================

        // Spam detection foreign keys
        Create.ForeignKey("fk_stop_words_added_by")
            .FromTable("stop_words").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("fk_chat_prompts_added_by")
            .FromTable("chat_prompts").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("fk_spam_check_configs_modified_by")
            .FromTable("spam_check_configs").ForeignColumn("modified_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);
    }

    public override void Down()
    {
        // Drop in reverse order to respect foreign keys
        Delete.Table("telegram_link_tokens");
        Delete.Table("telegram_user_mappings");
        Delete.Table("verification_tokens");
        Delete.Table("audit_log");
        Delete.Table("invites");
        Delete.Table("recovery_codes");
        Delete.Table("users");
        Delete.Table("reports");
        Delete.Table("chat_admins");
        Delete.Table("managed_chats");
        Delete.Table("user_actions");
        Delete.Table("spam_check_configs");
        Delete.Table("spam_detection_configs");
        Delete.Table("chat_prompts");
        Delete.Table("stop_words");
        Delete.Table("detection_results");
        Delete.Table("message_edits");
        Delete.Table("messages");
    }
}
