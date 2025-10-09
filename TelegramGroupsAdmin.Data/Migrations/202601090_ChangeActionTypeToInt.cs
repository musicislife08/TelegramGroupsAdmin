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
        // Add temporary integer column
        Alter.Table("user_actions")
            .AddColumn("action_type_int").AsInt32().Nullable();

        // Convert string values to integers in temp column
        Execute.Sql(@"
            UPDATE user_actions
            SET action_type_int = CASE
                WHEN action_type = 'ban' THEN 0
                WHEN action_type = 'warn' THEN 1
                WHEN action_type = 'mute' THEN 2
                WHEN action_type = 'trust' THEN 3
                WHEN action_type = 'unban' THEN 4
                ELSE 0
            END;
        ");

        // Drop old column
        Delete.Column("action_type").FromTable("user_actions");

        // Rename temp column to action_type
        Rename.Column("action_type_int").OnTable("user_actions").To("action_type");

        // Make it NOT NULL
        Alter.Column("action_type")
            .OnTable("user_actions")
            .AsInt32()
            .NotNullable();
    }

    public override void Down()
    {
        // Add temporary string column
        Alter.Table("user_actions")
            .AddColumn("action_type_str").AsString(50).Nullable();

        // Convert integers back to strings in temp column
        Execute.Sql(@"
            UPDATE user_actions
            SET action_type_str = CASE
                WHEN action_type = 0 THEN 'ban'
                WHEN action_type = 1 THEN 'warn'
                WHEN action_type = 2 THEN 'mute'
                WHEN action_type = 3 THEN 'trust'
                WHEN action_type = 4 THEN 'unban'
                ELSE 'ban'
            END;
        ");

        // Drop old column
        Delete.Column("action_type").FromTable("user_actions");

        // Rename temp column to action_type
        Rename.Column("action_type_str").OnTable("user_actions").To("action_type");

        // Make it NOT NULL
        Alter.Column("action_type")
            .OnTable("user_actions")
            .AsString(50)
            .NotNullable();
    }
}
