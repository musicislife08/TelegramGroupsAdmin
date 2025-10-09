using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Migration to add spam_detection_configs table for storing complete SpamDetectionConfig objects.
/// This is separate from spam_check_configs which stores per-check settings.
/// </summary>
[Migration(202512093)]
public class SpamDetectionConfigTable : Migration
{
    public override void Up()
    {
        Create.Table("spam_detection_configs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsString(50).Nullable() // NULL = global config
            .WithColumn("config_json").AsString(4000).NotNullable() // Complete SpamDetectionConfig as JSON
            .WithColumn("last_updated").AsInt64().NotNullable()
            .WithColumn("updated_by").AsString(36).Nullable();

        Create.Index("idx_spam_detection_configs_chat_id")
            .OnTable("spam_detection_configs")
            .OnColumn("chat_id").Ascending();

        // Unique constraint for chat_id (only one config per chat, NULL for global)
        Create.UniqueConstraint("uc_spam_detection_configs_chat")
            .OnTable("spam_detection_configs")
            .Column("chat_id");
    }

    public override void Down()
    {
        Delete.Table("spam_detection_configs");
    }
}
