using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

[Migration(202601088)]
public class AddTelegramUserMappings : Migration
{
    public override void Up()
    {
        // Create telegram_user_mappings table
        Create.Table("telegram_user_mappings")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("telegram_id").AsInt64().NotNullable().Unique()
            .WithColumn("telegram_username").AsString().Nullable()
            .WithColumn("user_id").AsString().NotNullable()
                .ForeignKey("fk_telegram_user_mappings_user_id", "users", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("linked_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);

        // Create index on user_id for faster lookups
        Create.Index("idx_telegram_user_mappings_user_id")
            .OnTable("telegram_user_mappings")
            .OnColumn("user_id");

        // Create telegram_link_tokens table
        Create.Table("telegram_link_tokens")
            .WithColumn("token").AsString(64).PrimaryKey()
            .WithColumn("user_id").AsString().NotNullable()
                .ForeignKey("fk_telegram_link_tokens_user_id", "users", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("used_at").AsInt64().Nullable()
            .WithColumn("used_by_telegram_id").AsInt64().Nullable();

        // Create index on expires_at for cleanup queries
        Create.Index("idx_telegram_link_tokens_expires_at")
            .OnTable("telegram_link_tokens")
            .OnColumn("expires_at");

        // Create index on user_id for user's token lookups
        Create.Index("idx_telegram_link_tokens_user_id")
            .OnTable("telegram_link_tokens")
            .OnColumn("user_id");
    }

    public override void Down()
    {
        Delete.Table("telegram_link_tokens");
        Delete.Table("telegram_user_mappings");
    }
}
