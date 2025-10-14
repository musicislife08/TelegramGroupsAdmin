using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Remove chat_ids column from user_actions table
/// All actions (bans, trusts, warns) are now global by default
/// Per-chat scoping was not being used and added unnecessary complexity
/// Origin chat can be tracked via message_id FK to messages table
/// </summary>
[Migration(202601105)]
public class RemoveChatIdsFromUserActions : Migration
{
    public override void Up()
    {
        Delete.Column("chat_ids").FromTable("user_actions");
    }

    public override void Down()
    {
        // Restore the column if needed (all values will be NULL for global actions)
        Alter.Table("user_actions")
            .AddColumn("chat_ids").AsCustom("BIGINT[]").Nullable();
    }
}
