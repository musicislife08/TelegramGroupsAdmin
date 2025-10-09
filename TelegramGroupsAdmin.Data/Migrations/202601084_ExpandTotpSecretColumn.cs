using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

[Migration(202601084)]
public class ExpandTotpSecretColumn : Migration
{
    public override void Up()
    {
        // Expand totp_secret to accommodate ASP.NET Data Protection encrypted values
        // Data Protection encrypted strings are typically 200-500 characters
        Alter.Column("totp_secret")
            .OnTable("users")
            .AsString(512)
            .Nullable();
    }

    public override void Down()
    {
        Alter.Column("totp_secret")
            .OnTable("users")
            .AsString(64)
            .Nullable();
    }
}
