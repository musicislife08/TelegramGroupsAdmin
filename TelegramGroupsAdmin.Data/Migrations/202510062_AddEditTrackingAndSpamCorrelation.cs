using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

[Migration(202510062)]
public class AddEditTrackingAndSpamCorrelation : Migration
{
    public override void Up()
    {
        // Messages table
        Alter.Table("messages")
            .AddColumn("edit_date").AsInt64().Nullable()
            .AddColumn("content_hash").AsString(64).Nullable()
            .AddColumn("chat_name").AsString().Nullable()
            .AddColumn("photo_local_path").AsString().Nullable()
            .AddColumn("photo_thumbnail_path").AsString().Nullable();

        Create.Index("idx_content_hash").OnTable("messages")
            .OnColumn("content_hash").Ascending();
        Create.Index("idx_chat_name").OnTable("messages")
            .OnColumn("chat_name").Ascending();

        // Message edits table
        Create.Table("message_edits")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt64().NotNullable()
                .ForeignKey("messages", "message_id")
            .WithColumn("edit_date").AsInt64().NotNullable()
            .WithColumn("old_text").AsString().Nullable()
            .WithColumn("new_text").AsString().Nullable()
            .WithColumn("old_content_hash").AsString(64).Nullable()
            .WithColumn("new_content_hash").AsString(64).Nullable();

        Create.Index("idx_message_edits_msg").OnTable("message_edits")
            .OnColumn("message_id").Ascending()
            .OnColumn("edit_date").Descending();

        // Spam checks table
        Create.Table("spam_checks")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("check_timestamp").AsInt64().NotNullable()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("content_hash").AsString(64).Nullable()
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("confidence").AsInt32().NotNullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("check_type").AsString().NotNullable() // "text" or "image"
            .WithColumn("matched_message_id").AsInt64().Nullable()
                .ForeignKey("messages", "message_id");

        Create.Index("idx_spam_checks_hash").OnTable("spam_checks")
            .OnColumn("content_hash").Ascending();
        Create.Index("idx_spam_checks_timestamp").OnTable("spam_checks")
            .OnColumn("check_timestamp").Descending();
    }

    public override void Down()
    {
        Delete.Table("spam_checks");
        Delete.Table("message_edits");
        Delete.Index("idx_chat_name").OnTable("messages");
        Delete.Index("idx_content_hash").OnTable("messages");
        Delete.Column("photo_thumbnail_path").FromTable("messages");
        Delete.Column("photo_local_path").FromTable("messages");
        Delete.Column("chat_name").FromTable("messages");
        Delete.Column("content_hash").FromTable("messages");
        Delete.Column("edit_date").FromTable("messages");
    }
}
