using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Migration to rename group_id columns to chat_id for consistency with Telegram API terminology.
/// In Telegram Bot API, everything is a "chat" - whether private chat, group, or supergroup.
/// </summary>
[Migration(202512092)]
public class RenameGroupIdToChatId : Migration
{
    public override void Up()
    {
        // Rename group_id to chat_id in spam_samples table
        Rename.Column("group_id").OnTable("spam_samples").To("chat_id");

        // Rename group_id to chat_id in training_samples table
        Rename.Column("group_id").OnTable("training_samples").To("chat_id");

        // Rename group_id to chat_id in group_prompts table (and rename table to chat_prompts)
        Rename.Table("group_prompts").To("chat_prompts");
        Rename.Column("group_id").OnTable("chat_prompts").To("chat_id");

        // Rename group_id to chat_id in spam_check_configs table
        Rename.Column("group_id").OnTable("spam_check_configs").To("chat_id");

        // Update indexes to use new column names
        Delete.Index("idx_spam_samples_group_id").OnTable("spam_samples");
        Create.Index("idx_spam_samples_chat_id")
            .OnTable("spam_samples")
            .OnColumn("chat_id").Ascending();

        Delete.Index("idx_group_prompts_group_id").OnTable("chat_prompts");
        Create.Index("idx_chat_prompts_chat_id")
            .OnTable("chat_prompts")
            .OnColumn("chat_id").Ascending();

        Delete.Index("idx_spam_check_configs_group_id").OnTable("spam_check_configs");
        Create.Index("idx_spam_check_configs_chat_id")
            .OnTable("spam_check_configs")
            .OnColumn("chat_id").Ascending();

        // Update unique constraint to use new column name
        Delete.UniqueConstraint("uc_spam_check_configs_group_check").FromTable("spam_check_configs");
        Create.UniqueConstraint("uc_spam_check_configs_chat_check")
            .OnTable("spam_check_configs")
            .Columns("chat_id", "check_name");
    }

    public override void Down()
    {
        // Reverse the changes - rename chat_id back to group_id
        Rename.Column("chat_id").OnTable("spam_samples").To("group_id");
        Rename.Column("chat_id").OnTable("training_samples").To("group_id");

        Rename.Table("chat_prompts").To("group_prompts");
        Rename.Column("chat_id").OnTable("group_prompts").To("group_id");

        Rename.Column("chat_id").OnTable("spam_check_configs").To("group_id");

        // Restore original indexes
        Delete.Index("idx_spam_samples_chat_id").OnTable("spam_samples");
        Create.Index("idx_spam_samples_group_id")
            .OnTable("spam_samples")
            .OnColumn("group_id").Ascending();

        Delete.Index("idx_chat_prompts_chat_id").OnTable("group_prompts");
        Create.Index("idx_group_prompts_group_id")
            .OnTable("group_prompts")
            .OnColumn("group_id").Ascending();

        Delete.Index("idx_spam_check_configs_chat_id").OnTable("spam_check_configs");
        Create.Index("idx_spam_check_configs_group_id")
            .OnTable("spam_check_configs")
            .OnColumn("group_id").Ascending();

        // Restore original unique constraint
        Delete.UniqueConstraint("uc_spam_check_configs_chat_check").FromTable("spam_check_configs");
        Create.UniqueConstraint("uc_spam_check_configs_group_check")
            .OnTable("spam_check_configs")
            .Columns("group_id", "check_name");
    }
}