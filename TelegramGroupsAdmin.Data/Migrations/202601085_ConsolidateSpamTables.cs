using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

[Migration(202601085)]
public class ConsolidateSpamTables : Migration
{
    public override void Up()
    {
        // Add detection tracking columns to training_samples
        Alter.Table("training_samples")
            .AddColumn("detection_count").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("last_detected_date").AsInt64().Nullable();

        // Convert chat_id (single) to chat_ids (array) to track all chats where spam was seen
        // This gives us visibility into spam campaign reach across multiple groups
        Execute.Sql("ALTER TABLE training_samples ADD COLUMN chat_ids TEXT[]");
        Execute.Sql("UPDATE training_samples SET chat_ids = ARRAY[chat_id] WHERE chat_id IS NOT NULL");
        Execute.Sql("UPDATE training_samples SET chat_ids = ARRAY[]::TEXT[] WHERE chat_id IS NULL");
        Delete.Column("chat_id").FromTable("training_samples");

        // Add unique constraint to prevent duplicate message text (global deduplication)
        // Same message = same classification across all chats
        Create.Index("idx_training_samples_unique_message")
            .OnTable("training_samples")
            .OnColumn("message_text").Ascending()
            .WithOptions().Unique();

        // Migrate data from spam_samples to training_samples
        // All spam_samples are spam (is_spam = true)
        // If duplicate exists, update to spam and merge detection counts + chat arrays
        Execute.Sql(@"
            INSERT INTO training_samples (message_text, is_spam, added_date, source, confidence_when_added, chat_ids, added_by, detection_count, last_detected_date)
            SELECT
                sample_text,
                true,
                added_date,
                source,
                NULL,
                CASE WHEN chat_id IS NOT NULL THEN ARRAY[chat_id] ELSE ARRAY[]::TEXT[] END,
                added_by,
                detection_count,
                last_detected_date
            FROM spam_samples
            ON CONFLICT (message_text)
            DO UPDATE SET
                is_spam = EXCLUDED.is_spam,
                detection_count = training_samples.detection_count + EXCLUDED.detection_count,
                last_detected_date = GREATEST(training_samples.last_detected_date, EXCLUDED.last_detected_date),
                chat_ids = array_cat(training_samples.chat_ids, EXCLUDED.chat_ids);
        ");

        // Drop spam_samples table - no longer needed
        Delete.Table("spam_samples");
    }

    public override void Down()
    {
        // Recreate spam_samples table
        Create.Table("spam_samples")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("sample_text").AsString(4000).NotNullable()
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("source").AsString(50).NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("chat_id").AsString(50).Nullable()
            .WithColumn("added_by").AsString(36).Nullable()
            .WithColumn("detection_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("last_detected_date").AsInt64().Nullable();

        Create.Index("idx_spam_samples_enabled")
            .OnTable("spam_samples")
            .OnColumn("enabled").Ascending();
        Create.Index("idx_spam_samples_chat_id")
            .OnTable("spam_samples")
            .OnColumn("chat_id").Ascending();
        Create.Index("idx_spam_samples_detection_count")
            .OnTable("spam_samples")
            .OnColumn("detection_count").Descending();

        // Migrate spam data back from training_samples
        Execute.Sql(@"
            INSERT INTO spam_samples (sample_text, added_date, source, enabled, chat_id, added_by, detection_count, last_detected_date)
            SELECT message_text, added_date, source, true, chat_ids[1], added_by, detection_count, last_detected_date
            FROM training_samples
            WHERE is_spam = true;
        ");

        // Remove unique constraint
        Delete.Index("idx_training_samples_unique_message").OnTable("training_samples");

        // Convert chat_ids array back to single chat_id
        Execute.Sql("ALTER TABLE training_samples ADD COLUMN chat_id TEXT");
        Execute.Sql("UPDATE training_samples SET chat_id = chat_ids[1] WHERE array_length(chat_ids, 1) > 0");
        Delete.Column("chat_ids").FromTable("training_samples");

        // Remove detection columns from training_samples
        Delete.Column("detection_count").FromTable("training_samples");
        Delete.Column("last_detected_date").FromTable("training_samples");
    }
}
