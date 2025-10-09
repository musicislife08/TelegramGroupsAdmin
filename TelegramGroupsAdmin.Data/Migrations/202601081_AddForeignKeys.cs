using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Adds missing foreign key constraints for data integrity.
/// PostgreSQL supports foreign keys much better than SQLite.
/// </summary>
[Migration(202601081)]
public class AddForeignKeys : Migration
{
    public override void Up()
    {
        // Users table - self-referencing FKs
        Create.ForeignKey("fk_users_invited_by")
            .FromTable("users").ForeignColumn("invited_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull); // If inviter is deleted, set to NULL

        Create.ForeignKey("fk_users_modified_by")
            .FromTable("users").ForeignColumn("modified_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull); // If modifier is deleted, set to NULL

        // Audit log - optional FKs with SET NULL on delete
        Create.ForeignKey("fk_audit_log_actor")
            .FromTable("audit_log").ForeignColumn("actor_user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull); // Keep audit trail even if user deleted

        Create.ForeignKey("fk_audit_log_target")
            .FromTable("audit_log").ForeignColumn("target_user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull); // Keep audit trail even if user deleted

        // Spam detection tables - track who added data
        Create.ForeignKey("fk_spam_samples_added_by")
            .FromTable("spam_samples").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("fk_training_samples_added_by")
            .FromTable("training_samples").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

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

        // Verification tokens - user reference
        Create.ForeignKey("fk_verification_tokens_user")
            .FromTable("verification_tokens").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade); // Delete tokens if user deleted
    }

    public override void Down()
    {
        // Drop foreign keys in reverse order
        Delete.ForeignKey("fk_verification_tokens_user").OnTable("verification_tokens");
        Delete.ForeignKey("fk_spam_check_configs_modified_by").OnTable("spam_check_configs");
        Delete.ForeignKey("fk_chat_prompts_added_by").OnTable("chat_prompts");
        Delete.ForeignKey("fk_stop_words_added_by").OnTable("stop_words");
        Delete.ForeignKey("fk_training_samples_added_by").OnTable("training_samples");
        Delete.ForeignKey("fk_spam_samples_added_by").OnTable("spam_samples");
        Delete.ForeignKey("fk_audit_log_target").OnTable("audit_log");
        Delete.ForeignKey("fk_audit_log_actor").OnTable("audit_log");
        Delete.ForeignKey("fk_users_modified_by").OnTable("users");
        Delete.ForeignKey("fk_users_invited_by").OnTable("users");
    }
}
