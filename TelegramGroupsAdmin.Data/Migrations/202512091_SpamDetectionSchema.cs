using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Migration for enhanced spam detection system with database-driven configuration.
/// Adds tables for stop words, training samples, spam samples, and group-specific prompts.
/// </summary>
[Migration(202512091)]
public class SpamDetectionSchema : Migration
{
    public override void Up()
    {
        // ===== STOP WORDS TABLE =====
        // Stores stop words for spam detection with UI management
        Create.Table("stop_words")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("word").AsString(100).NotNullable().Unique()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("added_by").AsString(36).Nullable() // User ID who added it
            .WithColumn("notes").AsString(500).Nullable(); // Context about why this word was added

        Create.Index("idx_stop_words_enabled")
            .OnTable("stop_words")
            .OnColumn("enabled").Ascending();

        // ===== TRAINING SAMPLES TABLE =====
        // Stores training data for Bayes classifier with continuous learning
        Create.Table("training_samples")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_text").AsString(4000).NotNullable()
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("source").AsString(50).NotNullable() // 'manual', 'auto_detection', 'false_positive', etc.
            .WithColumn("confidence_when_added").AsInt32().Nullable() // Confidence score when auto-detected
            .WithColumn("group_id").AsString(50).Nullable() // Which group this came from
            .WithColumn("added_by").AsString(36).Nullable(); // User ID for manual additions

        Create.Index("idx_training_samples_is_spam")
            .OnTable("training_samples")
            .OnColumn("is_spam").Ascending();
        Create.Index("idx_training_samples_added_date")
            .OnTable("training_samples")
            .OnColumn("added_date").Descending();
        Create.Index("idx_training_samples_source")
            .OnTable("training_samples")
            .OnColumn("source").Ascending();

        // ===== SPAM SAMPLES TABLE =====
        // Stores spam patterns for similarity checking with TF-IDF
        Create.Table("spam_samples")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("sample_text").AsString(4000).NotNullable()
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("source").AsString(50).NotNullable() // 'manual', 'auto_detection', 'import', etc.
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("group_id").AsString(50).Nullable() // Group-specific or global (null)
            .WithColumn("added_by").AsString(36).Nullable() // User ID who added it
            .WithColumn("detection_count").AsInt32().NotNullable().WithDefaultValue(0) // How many times this pattern caught spam
            .WithColumn("last_detected_date").AsInt64().Nullable(); // Last time this pattern caught something

        Create.Index("idx_spam_samples_enabled")
            .OnTable("spam_samples")
            .OnColumn("enabled").Ascending();
        Create.Index("idx_spam_samples_group_id")
            .OnTable("spam_samples")
            .OnColumn("group_id").Ascending();
        Create.Index("idx_spam_samples_detection_count")
            .OnTable("spam_samples")
            .OnColumn("detection_count").Descending();

        // ===== GROUP PROMPTS TABLE =====
        // Stores custom OpenAI prompts per group for veto mode
        Create.Table("group_prompts")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("group_id").AsString(50).NotNullable()
            .WithColumn("custom_prompt").AsString(2000).NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("added_by").AsString(36).Nullable() // User ID who added it
            .WithColumn("notes").AsString(500).Nullable(); // Description of what this prompt targets

        Create.Index("idx_group_prompts_group_id")
            .OnTable("group_prompts")
            .OnColumn("group_id").Ascending();
        Create.Index("idx_group_prompts_enabled")
            .OnTable("group_prompts")
            .OnColumn("enabled").Ascending();

        // ===== SPAM CHECK CONFIGURATIONS TABLE =====
        // Stores per-group configurations for each spam check
        Create.Table("spam_check_configs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("group_id").AsString(50).NotNullable()
            .WithColumn("check_name").AsString(50).NotNullable() // 'StopWords', 'CAS', 'Similarity', etc.
            .WithColumn("enabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("confidence_threshold").AsInt32().Nullable() // Override default threshold
            .WithColumn("configuration_json").AsString(2000).Nullable() // Check-specific config as JSON
            .WithColumn("modified_date").AsInt64().NotNullable()
            .WithColumn("modified_by").AsString(36).Nullable();

        Create.Index("idx_spam_check_configs_group_id")
            .OnTable("spam_check_configs")
            .OnColumn("group_id").Ascending();
        Create.Index("idx_spam_check_configs_check_name")
            .OnTable("spam_check_configs")
            .OnColumn("check_name").Ascending();

        // Unique constraint for group_id + check_name combination
        Create.UniqueConstraint("uc_spam_check_configs_group_check")
            .OnTable("spam_check_configs")
            .Columns("group_id", "check_name");

        // ===== MESSAGE HISTORY TABLE (for OpenAI context) =====
        // Already exists but add chat_id index if not present (for OpenAI message history context)
        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS idx_messages_chat_id_timestamp
            ON messages(chat_id, timestamp DESC)
        ");
    }

    public override void Down()
    {
        // Drop indexes
        Execute.Sql("DROP INDEX IF EXISTS idx_messages_chat_id_timestamp");

        // Drop tables in reverse order
        Delete.Table("spam_check_configs");
        Delete.Table("group_prompts");
        Delete.Table("spam_samples");
        Delete.Table("training_samples");
        Delete.Table("stop_words");
    }
}