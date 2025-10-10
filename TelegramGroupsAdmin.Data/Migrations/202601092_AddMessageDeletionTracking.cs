using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Adds soft delete tracking for messages deleted by the bot.
/// Note: Telegram Bot API does not provide deletion events, so we can only track
/// deletions performed by our own bot (spam removal, moderation commands, etc.)
/// </summary>
[Migration(202601092)]
public class AddMessageDeletionTracking : Migration
{
    public override void Up()
    {
        // Add soft delete columns to messages table
        Alter.Table("messages")
            .AddColumn("deleted_at").AsInt64().Nullable()
            .AddColumn("deletion_source").AsString(50).Nullable();

        // Index for filtering deleted messages in UI
        Execute.Sql(@"
            CREATE INDEX idx_messages_deleted
            ON messages(chat_id, deleted_at)
            WHERE deleted_at IS NOT NULL;
        ");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS idx_messages_deleted;");

        Delete.Column("deletion_source").FromTable("messages");
        Delete.Column("deleted_at").FromTable("messages");
    }
}
