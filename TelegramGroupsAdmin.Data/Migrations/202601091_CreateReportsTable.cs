using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Creates reports table for user-submitted reports (/report command).
/// Reports reference messages in the messages table via FK for full context.
/// </summary>
[Migration(202601091)]
public class CreateReportsTable : Migration
{
    public override void Up()
    {
        Create.Table("reports")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt32().NotNullable()
            .WithColumn("chat_id").AsInt64().NotNullable()
            .WithColumn("report_command_message_id").AsInt32().NotNullable()
            .WithColumn("reported_by_user_id").AsInt64().NotNullable()
            .WithColumn("reported_by_user_name").AsString(255).Nullable()
            .WithColumn("reported_at").AsInt64().NotNullable()
            .WithColumn("status").AsInt32().NotNullable() // 0=Pending, 1=Reviewed, 2=Dismissed
            .WithColumn("reviewed_by").AsString(450).Nullable() // FK to users.id (string type)
            .WithColumn("reviewed_at").AsInt64().Nullable()
            .WithColumn("action_taken").AsString(50).Nullable() // 'spam', 'ban', 'warn', 'dismiss'
            .WithColumn("admin_notes").AsString(500).Nullable();

        // FK to messages table (the reported message)
        Create.ForeignKey("fk_reports_message")
            .FromTable("reports").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        // FK to messages table (the /report command message)
        Create.ForeignKey("fk_reports_command_message")
            .FromTable("reports").ForeignColumn("report_command_message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        // FK to users table (reviewer)
        Create.ForeignKey("fk_reports_reviewer")
            .FromTable("reports").ForeignColumn("reviewed_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        // Indexes for common queries
        Create.Index("idx_reports_status")
            .OnTable("reports")
            .OnColumn("status");

        Create.Index("idx_reports_chat_status")
            .OnTable("reports")
            .OnColumn("chat_id").Ascending()
            .OnColumn("status").Ascending();

        Create.Index("idx_reports_reported_at")
            .OnTable("reports")
            .OnColumn("reported_at").Descending();
    }

    public override void Down()
    {
        Delete.Table("reports");
    }
}
