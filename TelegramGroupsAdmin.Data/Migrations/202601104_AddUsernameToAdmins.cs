using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Add username column to chat_admins table for better admin identification
/// </summary>
[Migration(202601104)]
public class AddUsernameToAdmins : Migration
{
    public override void Up()
    {
        Alter.Table("chat_admins")
            .AddColumn("username").AsString(255).Nullable();
    }

    public override void Down()
    {
        Delete.Column("username").FromTable("chat_admins");
    }
}
