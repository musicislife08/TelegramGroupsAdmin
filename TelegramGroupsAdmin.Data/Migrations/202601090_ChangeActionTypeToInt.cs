using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Change user_actions.action_type from VARCHAR(50) to INT
/// Maps: ban=0, warn=1, mute=2, trust=3, unban=4
/// </summary>
[Migration(202601090)]
public class ChangeActionTypeToInt : Migration
{
    public override void Up()
    {
        // Update existing string values to integers
        Execute.Sql(@"
            UPDATE user_actions
            SET action_type = CASE
                WHEN action_type = 'ban' THEN '0'
                WHEN action_type = 'warn' THEN '1'
                WHEN action_type = 'mute' THEN '2'
                WHEN action_type = 'trust' THEN '3'
                WHEN action_type = 'unban' THEN '4'
                ELSE '0'
            END;
        ");

        // Change column type to INTEGER
        Alter.Column("action_type")
            .OnTable("user_actions")
            .AsInt32()
            .NotNullable();
    }

    public override void Down()
    {
        // Change back to VARCHAR
        Alter.Column("action_type")
            .OnTable("user_actions")
            .AsString(50)
            .NotNullable();

        // Convert integers back to strings
        Execute.Sql(@"
            UPDATE user_actions
            SET action_type = CASE
                WHEN action_type = '0' THEN 'ban'
                WHEN action_type = '1' THEN 'warn'
                WHEN action_type = '2' THEN 'mute'
                WHEN action_type = '3' THEN 'trust'
                WHEN action_type = '4' THEN 'unban'
                ELSE 'ban'
            END;
        ");
    }
}
