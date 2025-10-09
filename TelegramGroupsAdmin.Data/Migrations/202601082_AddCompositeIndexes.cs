using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Adds composite indexes for common query patterns.
/// These significantly improve performance for multi-column WHERE clauses and JOINs.
/// </summary>
[Migration(202601082)]
public class AddCompositeIndexes : Migration
{
    public override void Up()
    {
        // Messages table - common query: get user's messages in a specific chat
        // Replaces separate queries for user_id and chat_id
        Create.Index("idx_messages_user_chat_time")
            .OnTable("messages")
            .OnColumn("user_id").Ascending()
            .OnColumn("chat_id").Ascending()
            .OnColumn("timestamp").Descending();

        // Spam checks - get user's spam history chronologically
        Create.Index("idx_spam_checks_user_timestamp")
            .OnTable("spam_checks")
            .OnColumn("user_id").Ascending()
            .OnColumn("check_timestamp").Descending();

        // Invites - filter by creator and status (e.g., "show me my pending invites")
        Create.Index("idx_invites_creator_status")
            .OnTable("invites")
            .OnColumn("created_by").Ascending()
            .OnColumn("status").Ascending()
            .OnColumn("created_at").Descending();

        // Audit log - get user's audit trail by event type
        Create.Index("idx_audit_log_target_event_time")
            .OnTable("audit_log")
            .OnColumn("target_user_id").Ascending()
            .OnColumn("event_type").Ascending()
            .OnColumn("timestamp").Descending();

        // Message edits - efficiently query edit history for a message
        // (Already has idx_message_edits_msg, but this is more specific)
        // Skip: idx_message_edits_msg already covers this pattern

        // Spam samples - filter enabled samples by chat
        Create.Index("idx_spam_samples_chat_enabled")
            .OnTable("spam_samples")
            .OnColumn("chat_id").Ascending()
            .OnColumn("enabled").Ascending()
            .OnColumn("detection_count").Descending();

        // Training samples - filter by spam/ham and source
        Create.Index("idx_training_samples_spam_source")
            .OnTable("training_samples")
            .OnColumn("is_spam").Ascending()
            .OnColumn("source").Ascending()
            .OnColumn("added_date").Descending();

        // Spam check configs - lookup config for specific chat + check
        // Skip: unique constraint uc_spam_check_configs_chat_check already indexes this

        // Verification tokens - lookup by user and type (not expired)
        Create.Index("idx_verification_tokens_user_type")
            .OnTable("verification_tokens")
            .OnColumn("user_id").Ascending()
            .OnColumn("token_type").Ascending()
            .OnColumn("expires_at").Descending();
    }

    public override void Down()
    {
        // Drop composite indexes
        Delete.Index("idx_verification_tokens_user_type").OnTable("verification_tokens");
        Delete.Index("idx_training_samples_spam_source").OnTable("training_samples");
        Delete.Index("idx_spam_samples_chat_enabled").OnTable("spam_samples");
        Delete.Index("idx_audit_log_target_event_time").OnTable("audit_log");
        Delete.Index("idx_invites_creator_status").OnTable("invites");
        Delete.Index("idx_spam_checks_user_timestamp").OnTable("spam_checks");
        Delete.Index("idx_messages_user_chat_time").OnTable("messages");
    }
}
