using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Add chat_admins table for per-chat admin permission caching
///
/// Purpose:
/// - Cache Telegram admin status per chat (avoid API calls on every command)
/// - Support per-chat admin permissions (admin in Chat A ≠ admin in Chat B)
/// - Web app linked users still have global permissions
/// - Automatically updated via MyChatMember events and startup refresh
///
/// Design:
/// - One record per user per chat
/// - Soft delete via is_active flag when demoted
/// - Bidirectional indexes for fast lookups
/// - Tracks both creator and regular admins
/// </summary>
[Migration(202601089)]
public class AddChatAdminsTable : Migration
{
    public override void Up()
    {
        Create.Table("chat_admins")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("telegram_id").AsInt64().NotNullable()
            .WithColumn("is_creator").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("promoted_at").AsInt64().NotNullable()
            .WithColumn("last_verified_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);

        // Foreign key to managed_chats (cascade delete when chat removed)
        Create.ForeignKey("fk_chat_admins_chat_id")
            .FromTable("chat_admins").ForeignColumn("chat_id")
            .ToTable("managed_chats").PrimaryColumn("chat_id")
            .OnDelete(System.Data.Rule.Cascade);

        // Unique constraint: one record per user per chat
        Create.UniqueConstraint("uq_chat_admins_chat_telegram")
            .OnTable("chat_admins")
            .Columns("chat_id", "telegram_id");

        // Index: "Who are the active admins in this chat?"
        Execute.Sql(@"
            CREATE INDEX idx_chat_admins_chat_id
            ON chat_admins (chat_id)
            WHERE is_active = true;
        ");

        // Index: "Which chats is this user an admin in?"
        Execute.Sql(@"
            CREATE INDEX idx_chat_admins_telegram_id
            ON chat_admins (telegram_id)
            WHERE is_active = true;
        ");
    }

    public override void Down()
    {
        Delete.Table("chat_admins");
    }
}
