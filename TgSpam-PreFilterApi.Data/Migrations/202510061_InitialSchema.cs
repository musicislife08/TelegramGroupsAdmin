using FluentMigrator;

namespace TgSpam_PreFilterApi.Data.Migrations;

[Migration(202510061)]
public class InitialSchema : Migration
{
    public override void Up()
    {
        Create.Table("messages")
            .WithColumn("message_id").AsInt64().PrimaryKey()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("user_name").AsString().Nullable()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("timestamp").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("message_text").AsString().Nullable()
            .WithColumn("photo_file_id").AsString().Nullable()
            .WithColumn("photo_file_size").AsInt32().Nullable()
            .WithColumn("urls").AsString().Nullable();

        Create.Index("idx_user_chat_timestamp")
            .OnTable("messages")
            .OnColumn("user_id").Ascending()
            .OnColumn("chat_id").Ascending()
            .OnColumn("timestamp").Descending();

        Create.Index("idx_expires_at")
            .OnTable("messages")
            .OnColumn("expires_at").Ascending();

        Execute.Sql(@"
            CREATE INDEX idx_user_chat_photo ON messages(user_id, chat_id, photo_file_id)
                WHERE photo_file_id IS NOT NULL;
        ");
    }

    public override void Down()
    {
        Delete.Index("idx_user_chat_photo").OnTable("messages");
        Delete.Index("idx_expires_at").OnTable("messages");
        Delete.Index("idx_user_chat_timestamp").OnTable("messages");
        Delete.Table("messages");
    }
}
